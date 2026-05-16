using System.Management.Automation;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to the Google Gemini API with streaming.
/// Supports multi-turn (-History), image input (-Image), tool calling (-Tool),
/// and reports token usage.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "Gemini")]
[OutputType(typeof(AIResponse))]
public class InvokeGeminiCmdlet : AIStreamingCmdletBase
{
    [Parameter]
    [ArgumentCompleter(typeof(GeminiModelCompleter))]
    public new string? Model { get; set; }

    private const string DefaultModel = "gemini-2.5-flash";

    protected override string ProviderName => "Google";

    protected override ApiCallResult CallAPI(string userContent)
        => Call(userContent, SystemPrompt, Model, MaxTokens, History, Image,
                Temperature, TopP, StopSequence, Json.IsPresent, Schema,
                Tool, MaxToolIterations,
                BuildAIScriptContext(),
                t => Host.UI.Write(t));

    /// <summary>Compat overload for Compare-AI; -Tool and exec_powershell disabled.</summary>
    public static ApiCallResult Call(
        string userContent,
        string? systemPrompt,
        string? model,
        int maxTokens,
        AIResponse? history,
        string[]? images,
        double? temperature,
        double? topP,
        string[]? stopSequence,
        bool json,
        System.Collections.Hashtable? schema,
        Action<string>? onToken)
    {
        return Call(userContent, systemPrompt, model, maxTokens, history, images,
                    temperature, topP, stopSequence, json, schema,
                    toolsRaw: null, maxToolIterations: 10, scriptContext: null, onToken);
    }

    /// <summary>Compat overload for existing 14-param callers; exec_powershell disabled.</summary>
    public static ApiCallResult Call(
        string userContent,
        string? systemPrompt,
        string? model,
        int maxTokens,
        AIResponse? history,
        string[]? images,
        double? temperature,
        double? topP,
        string[]? stopSequence,
        bool json,
        System.Collections.Hashtable? schema,
        System.Collections.Hashtable[]? toolsRaw,
        int maxToolIterations,
        Action<string>? onToken)
        => Call(userContent, systemPrompt, model, maxTokens, history, images,
                temperature, topP, stopSequence, json, schema, toolsRaw, maxToolIterations,
                scriptContext: null, onToken);

    /// <summary>Full Call() variant used by the cmdlet's CallAPI override.</summary>
    public static ApiCallResult Call(
        string userContent,
        string? systemPrompt,
        string? model,
        int maxTokens,
        AIResponse? history,
        string[]? images,
        double? temperature,
        double? topP,
        string[]? stopSequence,
        bool json,
        System.Collections.Hashtable? schema,
        System.Collections.Hashtable[]? toolsRaw,
        int maxToolIterations,
        AIScriptContext? scriptContext,
        Action<string>? onToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new PSInvalidOperationException("GEMINI_API_KEY environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? DefaultModel : model;
        var effectiveSystemPrompt = systemPrompt ?? history?.SystemPrompt;
        var tools = ParseTools(toolsRaw);
        bool hasExec = scriptContext != null;
        bool hasAnyTool = tools != null || hasExec;

        if (schema != null && hasAnyTool)
        {
            throw new PSArgumentException("-Schema cannot be combined with -Tool or exec_powershell. Tool use already structures the output.");
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{resolvedModel}:streamGenerateContent?alt=sse";
        HttpRequestMessage MakeRequest(string body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("x-goog-api-key", apiKey);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return req;
        }

        // No tools and no exec: single-shot fast path.
        if (!hasAnyTool)
        {
            var body = BuildJsonSingleShot(maxTokens, effectiveSystemPrompt, history, userContent, images,
                                           temperature, topP, stopSequence, json, schema);
            using var request = MakeRequest(body);
            var (text, inTok, outTok) = ReadSSEStream(request, ParseDelta, ParseUsage, onToken);
            return new ApiCallResult(text, resolvedModel, inTok, outTok);
        }

        // Tool-calling loop. Each entry in contents is a pre-serialized content object.
        var contents = new List<string>();
        if (history?.Turns != null)
        {
            foreach (var t in history.Turns)
                contents.Add(SerializeTextContent(t.Role == "assistant" ? "model" : t.Role, t.Content));
        }
        contents.Add(SerializeUserContent(userContent, images));

        var allText = new StringBuilder();
        var allToolCalls = new List<ToolCallRecord>();
        int totalIn = 0, totalOut = 0;

        for (int iter = 0; iter <= maxToolIterations; iter++)
        {
            var body = BuildJsonFromContents(maxTokens, effectiveSystemPrompt, contents,
                                             temperature, topP, stopSequence, tools, hasExec);
            using var request = MakeRequest(body);
            var rich = ReadGeminiStreamRich(request, onToken);
            totalIn += rich.InputTokens;
            totalOut += rich.OutputTokens;
            allText.Append(rich.Text);

            if (rich.FunctionCalls.Count == 0)
            {
                break;
            }

            if (iter == maxToolIterations)
            {
                allText.Append($"\n[tool budget exhausted after {maxToolIterations} rounds]");
                break;
            }

            contents.Add(SerializeModelFunctionCallContent(rich.Text, rich.FunctionCalls));

            var responses = new List<FunctionResponsePart>(rich.FunctionCalls.Count);
            foreach (var fc in rich.FunctionCalls)
            {
                // exec_powershell is the implicit AI-script tool — route to AIScriptExecutor.
                if (hasExec && fc.Name == ExecPowerShellName)
                {
                    var (er, ee) = RunExecPowerShell(fc.ArgumentsJson, scriptContext!);
                    if (ee != null)
                    {
                        responses.Add(new FunctionResponsePart(fc.Name, $"ERROR: {ee}", IsError: true));
                        allToolCalls.Add(new ToolCallRecord(fc.Name, fc.ArgumentsJson, string.Empty, ee));
                    }
                    else
                    {
                        responses.Add(new FunctionResponsePart(fc.Name, er, IsError: false));
                        allToolCalls.Add(new ToolCallRecord(fc.Name, fc.ArgumentsJson, er));
                    }
                    continue;
                }

                var tool = tools != null ? Array.Find(tools, x => x.Name == fc.Name) : null;
                if (tool == null)
                {
                    responses.Add(new FunctionResponsePart(fc.Name, $"ERROR: unknown tool '{fc.Name}'", IsError: true));
                    allToolCalls.Add(new ToolCallRecord(fc.Name, fc.ArgumentsJson, string.Empty, $"unknown tool '{fc.Name}'"));
                    continue;
                }

                var argsHt = JsonHelpers.JsonObjectToHashtable(fc.ArgumentsJson);
                var (result, error) = RunTool(tool, argsHt);
                if (error != null)
                {
                    responses.Add(new FunctionResponsePart(fc.Name, $"ERROR: {error}", IsError: true));
                    allToolCalls.Add(new ToolCallRecord(fc.Name, fc.ArgumentsJson, string.Empty, error));
                }
                else
                {
                    responses.Add(new FunctionResponsePart(fc.Name, result, IsError: false));
                    allToolCalls.Add(new ToolCallRecord(fc.Name, fc.ArgumentsJson, result));
                }
            }

            contents.Add(SerializeUserFunctionResponsesContent(responses));
        }

        return new ApiCallResult(allText.ToString(), resolvedModel, totalIn, totalOut, allToolCalls);
    }

    private static string BuildJsonSingleShot(
        int maxTokens, string? systemPrompt,
        AIResponse? history, string userContent, string[]? images,
        double? temperature, double? topP, string[]? stopSequence,
        bool json, System.Collections.Hashtable? schema)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            WriteSystemInstruction(w, systemPrompt);

            w.WritePropertyName("contents");
            w.WriteStartArray();

            if (history?.Turns != null)
            {
                foreach (var t in history.Turns)
                {
                    WriteTextContent(w, t.Role == "assistant" ? "model" : t.Role, t.Content);
                }
            }
            WriteUserContent(w, userContent, images);

            w.WriteEndArray();

            WriteGenerationConfig(w, maxTokens, temperature, topP, stopSequence, json, schema);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildJsonFromContents(
        int maxTokens, string? systemPrompt,
        List<string> contents,
        double? temperature, double? topP, string[]? stopSequence,
        ToolDescriptor[]? tools, bool includeExecTool)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            WriteSystemInstruction(w, systemPrompt);

            // tools
            w.WritePropertyName("tools");
            w.WriteStartArray();
            w.WriteStartObject();
            w.WritePropertyName("functionDeclarations");
            w.WriteStartArray();
            if (tools != null)
            {
                foreach (var t in tools)
                {
                    WriteFunctionDeclaration(w, t.Name, t.Description, t.Parameters);
                }
            }
            if (includeExecTool)
            {
                WriteFunctionDeclaration(w,
                    ExecPowerShellName,
                    ExecPowerShellDescription,
                    BuildExecPowerShellSchema());
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();

            w.WritePropertyName("contents");
            w.WriteStartArray();
            foreach (var c in contents)
            {
                using var doc = JsonDocument.Parse(c);
                doc.RootElement.WriteTo(w);
            }
            w.WriteEndArray();

            // No JSON mode / schema in tool path (mutually exclusive).
            WriteGenerationConfig(w, maxTokens, temperature, topP, stopSequence, json: false, schema: null);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteFunctionDeclaration(Utf8JsonWriter w, string name, string description, System.Collections.Hashtable parameters)
    {
        w.WriteStartObject();
        w.WriteString("name", name);
        w.WriteString("description", description);
        w.WritePropertyName("parameters");
        JsonHelpers.WriteHashtable(w, parameters);
        w.WriteEndObject();
    }

    private static void WriteSystemInstruction(Utf8JsonWriter w, string? systemPrompt)
    {
        if (string.IsNullOrEmpty(systemPrompt)) return;
        w.WritePropertyName("systemInstruction");
        w.WriteStartObject();
        w.WritePropertyName("parts");
        w.WriteStartArray();
        w.WriteStartObject();
        w.WriteString("text", systemPrompt);
        w.WriteEndObject();
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteGenerationConfig(
        Utf8JsonWriter w, int maxTokens,
        double? temperature, double? topP, string[]? stopSequence,
        bool json, System.Collections.Hashtable? schema)
    {
        bool hasGenConfig = maxTokens != 4096 || temperature.HasValue || topP.HasValue
                            || (stopSequence != null && stopSequence.Length > 0)
                            || json || schema != null;
        if (!hasGenConfig) return;

        w.WritePropertyName("generationConfig");
        w.WriteStartObject();
        if (maxTokens != 4096)    w.WriteNumber("maxOutputTokens", maxTokens);
        if (temperature.HasValue) w.WriteNumber("temperature",     temperature.Value);
        if (topP.HasValue)        w.WriteNumber("topP",            topP.Value);
        if (stopSequence != null && stopSequence.Length > 0)
        {
            w.WritePropertyName("stopSequences");
            w.WriteStartArray();
            foreach (var s in stopSequence) w.WriteStringValue(s);
            w.WriteEndArray();
        }
        if (json || schema != null)
        {
            w.WriteString("responseMimeType", "application/json");
            if (schema != null)
            {
                w.WritePropertyName("responseSchema");
                JsonHelpers.WriteHashtable(w, schema);
            }
        }
        w.WriteEndObject();
    }

    private static void WriteTextContent(Utf8JsonWriter w, string role, string text)
    {
        w.WriteStartObject();
        w.WriteString("role", role);
        w.WritePropertyName("parts");
        w.WriteStartArray();
        w.WriteStartObject();
        w.WriteString("text", text);
        w.WriteEndObject();
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteUserContent(Utf8JsonWriter w, string userContent, string[]? images)
    {
        w.WriteStartObject();
        w.WriteString("role", "user");
        w.WritePropertyName("parts");
        w.WriteStartArray();
        w.WriteStartObject();
        w.WriteString("text", userContent);
        w.WriteEndObject();
        if (images != null)
        {
            foreach (var pathOrUrl in images)
            {
                var img = ImageLoader.Load(pathOrUrl);
                w.WriteStartObject();
                w.WritePropertyName("inline_data");
                w.WriteStartObject();
                w.WriteString("mime_type", img.MimeType);
                w.WriteString("data", img.EnsureBase64());
                w.WriteEndObject();
                w.WriteEndObject();
            }
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static string SerializeTextContent(string role, string text)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            WriteTextContent(w, role, text);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string SerializeUserContent(string userContent, string[]? images)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            WriteUserContent(w, userContent, images);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string SerializeModelFunctionCallContent(string text, IReadOnlyList<FunctionCallBlock> functionCalls)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("role", "model");
            w.WritePropertyName("parts");
            w.WriteStartArray();
            if (!string.IsNullOrEmpty(text))
            {
                w.WriteStartObject();
                w.WriteString("text", text);
                w.WriteEndObject();
            }
            foreach (var fc in functionCalls)
            {
                w.WriteStartObject();
                w.WritePropertyName("functionCall");
                w.WriteStartObject();
                w.WriteString("name", fc.Name);
                w.WritePropertyName("args");
                var argsJson = string.IsNullOrWhiteSpace(fc.ArgumentsJson) ? "{}" : fc.ArgumentsJson;
                using (var doc = JsonDocument.Parse(argsJson))
                {
                    doc.RootElement.WriteTo(w);
                }
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string SerializeUserFunctionResponsesContent(IReadOnlyList<FunctionResponsePart> responses)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("role", "user");
            w.WritePropertyName("parts");
            w.WriteStartArray();
            foreach (var r in responses)
            {
                w.WriteStartObject();
                w.WritePropertyName("functionResponse");
                w.WriteStartObject();
                w.WriteString("name", r.Name);
                w.WritePropertyName("response");
                w.WriteStartObject();
                if (r.IsError) w.WriteString("error", r.Content);
                else           w.WriteString("result", r.Content);
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static GeminiStreamResult ReadGeminiStreamRich(HttpRequestMessage request, Action<string>? onToken)
    {
        var response = s_httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
        EnsureSuccess(response, "Gemini");

        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream);

        var text = new StringBuilder();
        var functionCalls = new List<FunctionCallBlock>();
        int inputTokens = 0, outputTokens = 0;

        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var data = line.AsSpan(6);
            if (data.SequenceEqual("[DONE]")) break;

            try
            {
                using var doc = JsonDocument.Parse(data.ToString());
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                if (root.TryGetProperty("usageMetadata", out var um) && um.ValueKind == JsonValueKind.Object)
                {
                    if (um.TryGetProperty("promptTokenCount",     out var p) && p.ValueKind == JsonValueKind.Number) inputTokens  = p.GetInt32();
                    if (um.TryGetProperty("candidatesTokenCount", out var c) && c.ValueKind == JsonValueKind.Number) outputTokens = c.GetInt32();
                }

                if (!root.TryGetProperty("candidates", out var candidates)) continue;
                if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) continue;
                var candidate = candidates[0];
                if (candidate.ValueKind != JsonValueKind.Object) continue;
                if (!candidate.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object) continue;
                if (!content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array) continue;

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object) continue;
                    if (part.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                    {
                        var chunk = txt.GetString();
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            text.Append(chunk);
                            onToken?.Invoke(chunk!);
                        }
                    }
                    else if (part.TryGetProperty("functionCall", out var fc) && fc.ValueKind == JsonValueKind.Object)
                    {
                        var name = fc.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() ?? "" : "";
                        string argsJson = "{}";
                        if (fc.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
                        {
                            argsJson = args.GetRawText();
                        }
                        functionCalls.Add(new FunctionCallBlock(name, argsJson));
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
            {
                // Skip malformed / unexpected-shape SSE chunks rather than aborting the stream.
            }
        }

        return new GeminiStreamResult(text.ToString(), functionCalls, inputTokens, outputTokens);
    }

    private record GeminiStreamResult(
        string Text,
        IReadOnlyList<FunctionCallBlock> FunctionCalls,
        int InputTokens,
        int OutputTokens);

    private record FunctionCallBlock(string Name, string ArgumentsJson);

    private record FunctionResponsePart(string Name, string Content, bool IsError);

    internal static string? ParseDelta(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("candidates", out var candidates)) return null;
        if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return null;
        var candidate = candidates[0];
        if (candidate.ValueKind != JsonValueKind.Object) return null;
        if (!candidate.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object) return null;
        if (!content.TryGetProperty("parts", out var parts)) return null;
        if (parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0) return null;
        var first = parts[0];
        if (first.ValueKind != JsonValueKind.Object) return null;
        if (first.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            return text.GetString();
        }
        return null;
    }

    internal static (int? input, int? output) ParseUsage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return (null, null);
        if (!root.TryGetProperty("usageMetadata", out var u) || u.ValueKind != JsonValueKind.Object) return (null, null);
        int? input  = u.TryGetProperty("promptTokenCount",     out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
        int? output = u.TryGetProperty("candidatesTokenCount", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
        return (input, output);
    }
}
