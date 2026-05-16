using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to the Anthropic Claude API with streaming.
/// Supports multi-turn (-History), image input (-Image), tool calling (-Tool),
/// and reports token usage.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "Claude")]
[OutputType(typeof(AIResponse))]
public class InvokeClaudeCmdlet : AIStreamingCmdletBase
{
    [Parameter]
    [ArgumentCompleter(typeof(ClaudeModelCompleter))]
    public new string? Model { get; set; }

    private const string DefaultModel = "claude-sonnet-4-20250514";

    protected override string ProviderName => "Anthropic";

    protected override ApiCallResult CallAPI(string userContent)
        => Call(userContent, SystemPrompt, Model, MaxTokens, History, Image,
                Temperature, TopP, StopSequence, Json.IsPresent, Schema, Tool, MaxToolIterations,
                BuildAIScriptContext(),
                t => Host.UI.Write(t));

    /// <summary>
    /// Compatibility overload used by Compare-AI and any external caller that
    /// doesn't want to expose the exec_powershell tool. Delegates with
    /// AIScriptContext set to null (Off-equivalent).
    /// </summary>
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

    /// <summary>Static helper with full surface (includes AI-script context).</summary>
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
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new PSInvalidOperationException("ANTHROPIC_API_KEY environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? DefaultModel : model;
        var effectiveSystemPrompt = systemPrompt ?? history?.SystemPrompt;
        var tools = ParseTools(toolsRaw);
        bool hasExec = scriptContext != null;
        bool hasAnyTool = tools != null || hasExec;

        // Anthropic has no native response_format. Best-effort: append schema to
        // system prompt and (below) prefill assistant with "{" so generation
        // starts as JSON. Tool calling and JSON prefill are mutually exclusive
        // — prefilled content blocks would collide with tool_use blocks.
        if (schema != null && hasAnyTool)
        {
            throw new PSArgumentException("-Schema cannot be combined with -Tool or exec_powershell. Tool use already structures the output.");
        }
        if (schema != null)
        {
            var schemaJson = JsonHelpers.SerializeHashtable(schema);
            var note = $"Respond with valid JSON only, conforming to this schema: {schemaJson}";
            effectiveSystemPrompt = string.IsNullOrEmpty(effectiveSystemPrompt) ? note : effectiveSystemPrompt + "\n\n" + note;
        }

        bool jsonPrefill = (json || schema != null) && !hasAnyTool;

        // No tools and no exec: existing single-shot fast path.
        if (!hasAnyTool)
        {
            var jsonText = BuildJson(resolvedModel, maxTokens, effectiveSystemPrompt, history, userContent, images,
                                     temperature, topP, stopSequence, jsonPrefill, tools: null);

            using var request = MakeRequest(apiKey, jsonText);
            var (text, inTok, outTok) = ReadSSEStream(request, ParseDelta, ParseUsage, onToken);
            if (jsonPrefill) text = "{" + text;
            return new ApiCallResult(text, resolvedModel, inTok, outTok);
        }

        // Tool-calling loop. We maintain a list of pre-serialized message JSON
        // strings so we can re-issue the request each iteration with the full
        // running conversation (assistant tool_use + user tool_result pairs).
        var messageJsons = new List<string>();
        if (history?.Turns != null)
        {
            foreach (var t in history.Turns)
                messageJsons.Add(SerializeSimpleMessage(t.Role, t.Content));
        }
        messageJsons.Add(SerializeUserMessage(userContent, images));

        var allText = new StringBuilder();
        var allToolCalls = new List<ToolCallRecord>();
        int totalIn = 0, totalOut = 0;

        for (int iter = 0; iter <= maxToolIterations; iter++)
        {
            var body = BuildJsonFromMessages(resolvedModel, maxTokens, effectiveSystemPrompt, messageJsons,
                                             temperature, topP, stopSequence, tools, hasExec);

            using var request = MakeRequest(apiKey, body);

            var rich = ReadClaudeStreamRich(request, onToken);
            totalIn += rich.InputTokens;
            totalOut += rich.OutputTokens;
            allText.Append(rich.Text);

            if (rich.StopReason != "tool_use" || rich.ToolUses.Count == 0)
            {
                break;
            }

            if (iter == maxToolIterations)
            {
                allText.Append($"\n[tool budget exhausted after {maxToolIterations} rounds]");
                break;
            }

            // Append the assistant's tool_use turn to the running conversation.
            messageJsons.Add(SerializeAssistantToolUseMessage(rich.Text, rich.ToolUses));

            // Execute each tool the model requested and collect tool_result blocks.
            var results = new List<ToolResultBlock>(rich.ToolUses.Count);
            foreach (var tu in rich.ToolUses)
            {
                // exec_powershell is implicit — dispatched to AIScriptExecutor.
                if (hasExec && tu.Name == ExecPowerShellName)
                {
                    var (er, ee) = RunExecPowerShell(tu.ArgumentsJson, scriptContext!);
                    if (ee != null)
                    {
                        var em = $"ERROR: {ee}";
                        results.Add(new ToolResultBlock(tu.Id, em, IsError: true));
                        allToolCalls.Add(new ToolCallRecord(tu.Name, tu.ArgumentsJson, string.Empty, ee));
                    }
                    else
                    {
                        results.Add(new ToolResultBlock(tu.Id, er, IsError: false));
                        allToolCalls.Add(new ToolCallRecord(tu.Name, tu.ArgumentsJson, er));
                    }
                    continue;
                }

                var tool = tools != null ? Array.Find(tools, x => x.Name == tu.Name) : null;
                if (tool == null)
                {
                    var msg = $"ERROR: unknown tool '{tu.Name}'";
                    results.Add(new ToolResultBlock(tu.Id, msg, IsError: true));
                    allToolCalls.Add(new ToolCallRecord(tu.Name, tu.ArgumentsJson, string.Empty, msg));
                    continue;
                }

                var argsHashtable = JsonHelpers.JsonObjectToHashtable(tu.ArgumentsJson);
                var (result, error) = RunTool(tool, argsHashtable);
                if (error != null)
                {
                    var msg = $"ERROR: {error}";
                    results.Add(new ToolResultBlock(tu.Id, msg, IsError: true));
                    allToolCalls.Add(new ToolCallRecord(tu.Name, tu.ArgumentsJson, string.Empty, error));
                }
                else
                {
                    results.Add(new ToolResultBlock(tu.Id, result, IsError: false));
                    allToolCalls.Add(new ToolCallRecord(tu.Name, tu.ArgumentsJson, result));
                }
            }

            messageJsons.Add(SerializeUserToolResultsMessage(results));
        }

        return new ApiCallResult(allText.ToString(), resolvedModel, totalIn, totalOut, allToolCalls);
    }

    private static HttpRequestMessage MakeRequest(string apiKey, string jsonBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return request;
    }

    private static string BuildJson(
        string model, int maxTokens, string? systemPrompt,
        AIResponse? history, string userContent, string[]? images,
        double? temperature, double? topP, string[]? stopSequence,
        bool jsonPrefill, ToolDescriptor[]? tools)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WriteNumber("max_tokens", maxTokens);
            w.WriteBoolean("stream", true);

            if (!string.IsNullOrEmpty(systemPrompt))
                w.WriteString("system", systemPrompt);

            if (temperature.HasValue) w.WriteNumber("temperature", temperature.Value);
            if (topP.HasValue)        w.WriteNumber("top_p", topP.Value);
            if (stopSequence != null && stopSequence.Length > 0)
            {
                w.WritePropertyName("stop_sequences");
                w.WriteStartArray();
                foreach (var s in stopSequence) w.WriteStringValue(s);
                w.WriteEndArray();
            }

            if (tools != null)
            {
                WriteToolsArray(w, tools, includeExecTool: false);
            }

            w.WritePropertyName("messages");
            w.WriteStartArray();

            // Prior turns (text-only — image history not re-attached).
            if (history?.Turns != null)
            {
                foreach (var t in history.Turns)
                {
                    w.WriteStartObject();
                    w.WriteString("role", t.Role);
                    w.WriteString("content", t.Content);
                    w.WriteEndObject();
                }
            }

            // Current user turn — image blocks first, then text.
            w.WriteStartObject();
            w.WriteString("role", "user");
            if (images != null && images.Length > 0)
            {
                w.WritePropertyName("content");
                w.WriteStartArray();
                foreach (var pathOrUrl in images)
                {
                    WriteImageBlock(w, pathOrUrl);
                }
                w.WriteStartObject();
                w.WriteString("type", "text");
                w.WriteString("text", userContent);
                w.WriteEndObject();
                w.WriteEndArray();
            }
            else
            {
                w.WriteString("content", userContent);
            }
            w.WriteEndObject();

            // Prefill the assistant turn with "{" so generation begins as JSON.
            if (jsonPrefill)
            {
                w.WriteStartObject();
                w.WriteString("role", "assistant");
                w.WriteString("content", "{");
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Builds the request body from a pre-serialized list of message JSON strings.
    /// Used by the tool-calling loop, where the message list evolves across iterations.
    /// </summary>
    private static string BuildJsonFromMessages(
        string model, int maxTokens, string? systemPrompt,
        List<string> messageJsons,
        double? temperature, double? topP, string[]? stopSequence,
        ToolDescriptor[]? tools, bool includeExecTool)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WriteNumber("max_tokens", maxTokens);
            w.WriteBoolean("stream", true);

            if (!string.IsNullOrEmpty(systemPrompt))
                w.WriteString("system", systemPrompt);

            if (temperature.HasValue) w.WriteNumber("temperature", temperature.Value);
            if (topP.HasValue)        w.WriteNumber("top_p", topP.Value);
            if (stopSequence != null && stopSequence.Length > 0)
            {
                w.WritePropertyName("stop_sequences");
                w.WriteStartArray();
                foreach (var s in stopSequence) w.WriteStringValue(s);
                w.WriteEndArray();
            }

            WriteToolsArray(w, tools, includeExecTool);

            w.WritePropertyName("messages");
            w.WriteStartArray();
            w.Flush();
            // Each entry in messageJsons is already an object — write raw.
            foreach (var msgJson in messageJsons)
            {
                using var doc = JsonDocument.Parse(msgJson);
                doc.RootElement.WriteTo(w);
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteToolsArray(Utf8JsonWriter w, ToolDescriptor[]? tools, bool includeExecTool = false)
    {
        w.WritePropertyName("tools");
        w.WriteStartArray();
        if (tools != null)
        {
            foreach (var t in tools)
            {
                w.WriteStartObject();
                w.WriteString("name", t.Name);
                w.WriteString("description", t.Description);
                w.WritePropertyName("input_schema");
                JsonHelpers.WriteHashtable(w, t.Parameters);
                w.WriteEndObject();
            }
        }
        if (includeExecTool)
        {
            w.WriteStartObject();
            w.WriteString("name", ExecPowerShellName);
            w.WriteString("description", ExecPowerShellDescription);
            w.WritePropertyName("input_schema");
            JsonHelpers.WriteHashtable(w, BuildExecPowerShellSchema());
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteImageBlock(Utf8JsonWriter w, string pathOrUrl)
    {
        var img = ImageLoader.Load(pathOrUrl);
        w.WriteStartObject();
        w.WriteString("type", "image");
        w.WritePropertyName("source");
        w.WriteStartObject();
        if (img.OriginalUrl != null)
        {
            w.WriteString("type", "url");
            w.WriteString("url", img.OriginalUrl);
        }
        else
        {
            w.WriteString("type", "base64");
            w.WriteString("media_type", img.MimeType);
            w.WriteString("data", img.EnsureBase64());
        }
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static string SerializeSimpleMessage(string role, string content)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("role", role);
            w.WriteString("content", content);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string SerializeUserMessage(string userContent, string[]? images)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("role", "user");
            if (images != null && images.Length > 0)
            {
                w.WritePropertyName("content");
                w.WriteStartArray();
                foreach (var p in images) WriteImageBlock(w, p);
                w.WriteStartObject();
                w.WriteString("type", "text");
                w.WriteString("text", userContent);
                w.WriteEndObject();
                w.WriteEndArray();
            }
            else
            {
                w.WriteString("content", userContent);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string SerializeAssistantToolUseMessage(string text, IReadOnlyList<ToolUseBlock> toolUses)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("role", "assistant");
            w.WritePropertyName("content");
            w.WriteStartArray();
            if (!string.IsNullOrEmpty(text))
            {
                w.WriteStartObject();
                w.WriteString("type", "text");
                w.WriteString("text", text);
                w.WriteEndObject();
            }
            foreach (var tu in toolUses)
            {
                w.WriteStartObject();
                w.WriteString("type", "tool_use");
                w.WriteString("id", tu.Id);
                w.WriteString("name", tu.Name);
                w.WritePropertyName("input");
                // Arguments are a JSON object; if the model emitted an empty
                // string treat as {} (no-arg tool).
                var argsJson = string.IsNullOrWhiteSpace(tu.ArgumentsJson) ? "{}" : tu.ArgumentsJson;
                using (var doc = JsonDocument.Parse(argsJson))
                {
                    doc.RootElement.WriteTo(w);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string SerializeUserToolResultsMessage(IReadOnlyList<ToolResultBlock> results)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("role", "user");
            w.WritePropertyName("content");
            w.WriteStartArray();
            foreach (var r in results)
            {
                w.WriteStartObject();
                w.WriteString("type", "tool_result");
                w.WriteString("tool_use_id", r.ToolUseId);
                w.WriteString("content", r.Content);
                if (r.IsError) w.WriteBoolean("is_error", true);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Reads an Anthropic SSE stream that may contain a mix of text and tool_use
    /// content blocks. Concatenates text deltas (and forwards them to onToken
    /// for live console output), accumulates input_json_delta partials per
    /// tool_use block, and surfaces the final stop_reason from message_delta.
    /// </summary>
    private static ClaudeStreamResult ReadClaudeStreamRich(HttpRequestMessage request, Action<string>? onToken)
    {
        var response = s_httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
        EnsureSuccess(response, "Claude");

        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream);

        var text = new StringBuilder();
        var toolUses = new List<ToolUseBlock>();
        var toolJson = new Dictionary<int, StringBuilder>(); // index → accumulating input_json_delta
        var toolMeta = new Dictionary<int, (string Id, string Name)>();
        int inputTokens = 0, outputTokens = 0;
        string? stopReason = null;

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
                if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String) continue;
                var type = typeProp.GetString();

                switch (type)
                {
                    case "message_start":
                        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object &&
                            msg.TryGetProperty("usage", out var u1) && u1.ValueKind == JsonValueKind.Object)
                        {
                            if (u1.TryGetProperty("input_tokens", out var i) && i.ValueKind == JsonValueKind.Number)
                                inputTokens = i.GetInt32();
                            if (u1.TryGetProperty("output_tokens", out var o) && o.ValueKind == JsonValueKind.Number)
                                outputTokens = o.GetInt32();
                        }
                        break;

                    case "content_block_start":
                        if (root.TryGetProperty("index", out var idxStart) && idxStart.ValueKind == JsonValueKind.Number &&
                            root.TryGetProperty("content_block", out var cb) && cb.ValueKind == JsonValueKind.Object &&
                            cb.TryGetProperty("type", out var cbType) && cbType.ValueKind == JsonValueKind.String &&
                            cbType.GetString() == "tool_use")
                        {
                            var idx = idxStart.GetInt32();
                            var id = cb.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                            var name = cb.TryGetProperty("name", out var nmEl) ? nmEl.GetString() ?? "" : "";
                            toolMeta[idx] = (id, name);
                            toolJson[idx] = new StringBuilder();
                        }
                        break;

                    case "content_block_delta":
                        if (!root.TryGetProperty("delta", out var d) || d.ValueKind != JsonValueKind.Object) break;
                        if (!d.TryGetProperty("type", out var dt) || dt.ValueKind != JsonValueKind.String) break;
                        var deltaType = dt.GetString();
                        if (deltaType == "text_delta" &&
                            d.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                        {
                            var chunk = txt.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                text.Append(chunk);
                                onToken?.Invoke(chunk!);
                            }
                        }
                        else if (deltaType == "input_json_delta" &&
                                 root.TryGetProperty("index", out var idxDelta) && idxDelta.ValueKind == JsonValueKind.Number &&
                                 d.TryGetProperty("partial_json", out var pj) && pj.ValueKind == JsonValueKind.String)
                        {
                            var idx = idxDelta.GetInt32();
                            if (!toolJson.TryGetValue(idx, out var sb))
                            {
                                sb = new StringBuilder();
                                toolJson[idx] = sb;
                            }
                            sb.Append(pj.GetString());
                        }
                        break;

                    case "message_delta":
                        if (root.TryGetProperty("delta", out var md) && md.ValueKind == JsonValueKind.Object &&
                            md.TryGetProperty("stop_reason", out var sr) && sr.ValueKind == JsonValueKind.String)
                        {
                            stopReason = sr.GetString();
                        }
                        if (root.TryGetProperty("usage", out var u2) && u2.ValueKind == JsonValueKind.Object &&
                            u2.TryGetProperty("output_tokens", out var o2) && o2.ValueKind == JsonValueKind.Number)
                        {
                            outputTokens = o2.GetInt32();
                        }
                        break;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
            {
                // Skip malformed / unexpected-shape SSE chunks rather than aborting the stream.
            }
        }

        // Materialize accumulated tool_use blocks in index order.
        foreach (var idx in new SortedSet<int>(toolMeta.Keys))
        {
            var (id, name) = toolMeta[idx];
            var args = toolJson.TryGetValue(idx, out var sb) ? sb.ToString() : "{}";
            toolUses.Add(new ToolUseBlock(id, name, args));
        }

        return new ClaudeStreamResult(text.ToString(), toolUses, inputTokens, outputTokens, stopReason);
    }

    private record ClaudeStreamResult(
        string Text,
        IReadOnlyList<ToolUseBlock> ToolUses,
        int InputTokens,
        int OutputTokens,
        string? StopReason);

    private record ToolUseBlock(string Id, string Name, string ArgumentsJson);

    private record ToolResultBlock(string ToolUseId, string Content, bool IsError);

    internal static string? ParseDelta(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return null;
        if (type.GetString() != "content_block_delta") return null;
        if (!root.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object) return null;
        if (delta.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            return text.GetString();
        }
        return null;
    }

    internal static (int? input, int? output) ParseUsage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return (null, null);
        if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String) return (null, null);
        var type = typeProp.GetString();

        // message_start has usage.input_tokens (and an initial output_tokens=1 hint)
        if (type == "message_start" &&
            root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object &&
            msg.TryGetProperty("usage", out var u1) && u1.ValueKind == JsonValueKind.Object)
        {
            int? input  = u1.TryGetProperty("input_tokens",  out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : null;
            int? output = u1.TryGetProperty("output_tokens", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : null;
            return (input, output);
        }
        // message_delta carries cumulative output_tokens
        if (type == "message_delta" &&
            root.TryGetProperty("usage", out var u2) && u2.ValueKind == JsonValueKind.Object &&
            u2.TryGetProperty("output_tokens", out var o2) && o2.ValueKind == JsonValueKind.Number)
        {
            return (null, o2.GetInt32());
        }
        return (null, null);
    }
}
