using System.Management.Automation;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Shared request-building, delta-parsing, and usage-parsing for providers that
/// expose an OpenAI-compatible chat completions endpoint: OpenAI itself, Groq /
/// Meta / Together (Llama), and DeepSeek. The wire format is identical at the
/// JSON level — only the URL, auth, and model name differ.
/// </summary>
internal static class OpenAICompat
{
    /// <summary>
    /// Entry point used by every OpenAI-compatible cmdlet. When -Tool is null,
    /// behaves identically to the legacy single-shot path (build body, stream
    /// once, return). When -Tool is supplied, runs the tool-calling loop:
    /// stream → execute scriptblocks → re-stream until finish_reason flips
    /// away from "tool_calls". <paramref name="makeRequest"/> creates the
    /// per-iteration HttpRequestMessage so URL and auth headers stay
    /// provider-specific.
    /// </summary>
    public static ApiCallResult CallWithTools(
        string model, int maxTokens, string? systemPrompt,
        AIResponse? history, string userContent, string[]? images,
        double? temperature, double? topP, string[]? stopSequence,
        bool json, System.Collections.Hashtable? schema,
        System.Collections.Hashtable[]? toolsRaw, int maxToolIterations,
        AIScriptContext? scriptContext,
        Func<string, HttpRequestMessage> makeRequest,
        Action<string>? onToken)
    {
        var tools = AIStreamingCmdletBase.ParseTools(toolsRaw);
        bool hasExec = scriptContext != null;
        bool hasAnyTool = tools != null || hasExec;

        if (schema != null && hasAnyTool)
        {
            throw new PSArgumentException("-Schema cannot be combined with -Tool or exec_powershell. Tool use already structures the output.");
        }

        // No tools and no exec: existing single-shot fast path.
        if (!hasAnyTool)
        {
            var body = BuildJson(model, maxTokens, systemPrompt, history, userContent, images,
                                 temperature, topP, stopSequence, json, schema);
            using var request = makeRequest(body);
            var (text, inTok, outTok) = AIStreamingCmdletBase.ReadSSEStream(request, ParseDelta, ParseUsage, onToken);
            return new ApiCallResult(text, model, inTok, outTok);
        }

        // Tool-calling loop. messageJsons is a pre-serialized representation
        // of the running conversation that grows by two entries (assistant
        // tool_calls + user tool results) each iteration.
        var messageJsons = new List<string>();
        if (!string.IsNullOrEmpty(systemPrompt))
            messageJsons.Add(SerializeSimpleMessage("system", systemPrompt!));
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
            var body = BuildJsonFromMessages(model, maxTokens, messageJsons, temperature, topP, stopSequence, tools, hasExec);
            using var request = makeRequest(body);

            var rich = ReadOpenAIStreamRich(request, onToken);
            totalIn += rich.InputTokens;
            totalOut += rich.OutputTokens;
            allText.Append(rich.Text);

            if (rich.FinishReason != "tool_calls" || rich.ToolCalls.Count == 0)
            {
                break;
            }

            if (iter == maxToolIterations)
            {
                allText.Append($"\n[tool budget exhausted after {maxToolIterations} rounds]");
                break;
            }

            messageJsons.Add(SerializeAssistantToolCallMessage(rich.Text, rich.ReasoningContent, rich.ToolCalls));

            foreach (var tc in rich.ToolCalls)
            {
                // exec_powershell is the implicit AI-script tool — dispatched to AIScriptExecutor.
                if (hasExec && tc.Name == AIStreamingCmdletBase.ExecPowerShellName)
                {
                    var (er, ee) = AIStreamingCmdletBase.RunExecPowerShell(tc.ArgumentsJson, scriptContext!);
                    if (ee != null)
                    {
                        messageJsons.Add(SerializeToolResultMessage(tc.Id, $"ERROR: {ee}"));
                        allToolCalls.Add(new ToolCallRecord(tc.Name, tc.ArgumentsJson, string.Empty, ee));
                    }
                    else
                    {
                        messageJsons.Add(SerializeToolResultMessage(tc.Id, er));
                        allToolCalls.Add(new ToolCallRecord(tc.Name, tc.ArgumentsJson, er));
                    }
                    continue;
                }

                var tool = tools != null ? Array.Find(tools, x => x.Name == tc.Name) : null;
                if (tool == null)
                {
                    var msg = $"ERROR: unknown tool '{tc.Name}'";
                    messageJsons.Add(SerializeToolResultMessage(tc.Id, msg));
                    allToolCalls.Add(new ToolCallRecord(tc.Name, tc.ArgumentsJson, string.Empty, $"unknown tool '{tc.Name}'"));
                    continue;
                }

                var argsHt = JsonHelpers.JsonObjectToHashtable(tc.ArgumentsJson);
                var (result, error) = AIStreamingCmdletBase.RunTool(tool, argsHt);
                if (error != null)
                {
                    messageJsons.Add(SerializeToolResultMessage(tc.Id, $"ERROR: {error}"));
                    allToolCalls.Add(new ToolCallRecord(tc.Name, tc.ArgumentsJson, string.Empty, error));
                }
                else
                {
                    messageJsons.Add(SerializeToolResultMessage(tc.Id, result));
                    allToolCalls.Add(new ToolCallRecord(tc.Name, tc.ArgumentsJson, result));
                }
            }
        }

        return new ApiCallResult(allText.ToString(), model, totalIn, totalOut, allToolCalls);
    }

    /// <summary>Builds the chat completions request JSON.</summary>
    public static string BuildJson(
        string model, int maxTokens, string? systemPrompt,
        AIResponse? history, string userContent, string[]? images,
        double? temperature, double? topP, string[]? stopSequence,
        bool json, System.Collections.Hashtable? schema)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WriteNumber("max_tokens", maxTokens);
            w.WriteBoolean("stream", true);

            // stream_options.include_usage so the final SSE chunk reports prompt / completion tokens.
            w.WritePropertyName("stream_options");
            w.WriteStartObject();
            w.WriteBoolean("include_usage", true);
            w.WriteEndObject();

            if (temperature.HasValue) w.WriteNumber("temperature", temperature.Value);
            if (topP.HasValue)        w.WriteNumber("top_p", topP.Value);
            if (stopSequence != null && stopSequence.Length > 0)
            {
                w.WritePropertyName("stop");
                w.WriteStartArray();
                foreach (var s in stopSequence) w.WriteStringValue(s);
                w.WriteEndArray();
            }

            // response_format: json_schema (strict) when -Schema is set,
            // json_object when -Json is set without a schema.
            if (schema != null)
            {
                w.WritePropertyName("response_format");
                w.WriteStartObject();
                w.WriteString("type", "json_schema");
                w.WritePropertyName("json_schema");
                w.WriteStartObject();
                w.WriteString("name", "schema");
                w.WriteBoolean("strict", true);
                w.WritePropertyName("schema");
                JsonHelpers.WriteHashtable(w, schema);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            else if (json)
            {
                w.WritePropertyName("response_format");
                w.WriteStartObject();
                w.WriteString("type", "json_object");
                w.WriteEndObject();
            }

            w.WritePropertyName("messages");
            w.WriteStartArray();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                w.WriteStartObject();
                w.WriteString("role", "system");
                w.WriteString("content", systemPrompt);
                w.WriteEndObject();
            }

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

            // Current user turn — content array if images, else plain string.
            w.WriteStartObject();
            w.WriteString("role", "user");
            if (images != null && images.Length > 0)
            {
                w.WritePropertyName("content");
                w.WriteStartArray();
                w.WriteStartObject();
                w.WriteString("type", "text");
                w.WriteString("text", userContent);
                w.WriteEndObject();
                foreach (var pathOrUrl in images)
                {
                    var img = ImageLoader.Load(pathOrUrl);
                    w.WriteStartObject();
                    w.WriteString("type", "image_url");
                    w.WritePropertyName("image_url");
                    w.WriteStartObject();
                    var url = img.OriginalUrl ?? $"data:{img.MimeType};base64,{img.EnsureBase64()}";
                    w.WriteString("url", url);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            else
            {
                w.WriteString("content", userContent);
            }
            w.WriteEndObject();

            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Extracts a text delta from one SSE chunk.</summary>
    public static string? ParseDelta(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("choices", out var choices)) return null;
        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;
        var first = choices[0];
        if (first.ValueKind != JsonValueKind.Object) return null;
        if (!first.TryGetProperty("delta", out var delta)) return null;
        if (delta.ValueKind != JsonValueKind.Object) return null;
        if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }
        return null;
    }

    /// <summary>
    /// Extracts (input, output) tokens from the final usage chunk
    /// (only present when stream_options.include_usage = true).
    /// </summary>
    public static (int? input, int? output) ParseUsage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return (null, null);
        if (!root.TryGetProperty("usage", out var u)) return (null, null);
        if (u.ValueKind != JsonValueKind.Object) return (null, null);  // OpenAI sends "usage": null in non-final chunks
        int? input  = u.TryGetProperty("prompt_tokens", out var p)     && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
        int? output = u.TryGetProperty("completion_tokens", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
        return (input, output);
    }

    /// <summary>
    /// Builds the request body from a pre-serialized list of message JSON
    /// strings. Used inside the tool-calling loop, where the conversation
    /// grows across iterations.
    /// </summary>
    private static string BuildJsonFromMessages(
        string model, int maxTokens, List<string> messageJsons,
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
            w.WritePropertyName("stream_options");
            w.WriteStartObject();
            w.WriteBoolean("include_usage", true);
            w.WriteEndObject();

            if (temperature.HasValue) w.WriteNumber("temperature", temperature.Value);
            if (topP.HasValue)        w.WriteNumber("top_p", topP.Value);
            if (stopSequence != null && stopSequence.Length > 0)
            {
                w.WritePropertyName("stop");
                w.WriteStartArray();
                foreach (var s in stopSequence) w.WriteStringValue(s);
                w.WriteEndArray();
            }

            WriteToolsArray(w, tools, includeExecTool);

            w.WritePropertyName("messages");
            w.WriteStartArray();
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

    private static void WriteToolsArray(Utf8JsonWriter w, ToolDescriptor[]? tools, bool includeExecTool)
    {
        w.WritePropertyName("tools");
        w.WriteStartArray();
        if (tools != null)
        {
            foreach (var t in tools)
            {
                WriteFunctionTool(w, t.Name, t.Description, t.Parameters);
            }
        }
        if (includeExecTool)
        {
            WriteFunctionTool(w,
                AIStreamingCmdletBase.ExecPowerShellName,
                AIStreamingCmdletBase.ExecPowerShellDescription,
                AIStreamingCmdletBase.BuildExecPowerShellSchema());
        }
        w.WriteEndArray();
    }

    private static void WriteFunctionTool(Utf8JsonWriter w, string name, string description, System.Collections.Hashtable parameters)
    {
        w.WriteStartObject();
        w.WriteString("type", "function");
        w.WritePropertyName("function");
        w.WriteStartObject();
        w.WriteString("name", name);
        w.WriteString("description", description);
        w.WritePropertyName("parameters");
        JsonHelpers.WriteHashtable(w, parameters);
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
                w.WriteStartObject();
                w.WriteString("type", "text");
                w.WriteString("text", userContent);
                w.WriteEndObject();
                foreach (var pathOrUrl in images)
                {
                    var img = ImageLoader.Load(pathOrUrl);
                    w.WriteStartObject();
                    w.WriteString("type", "image_url");
                    w.WritePropertyName("image_url");
                    w.WriteStartObject();
                    var url = img.OriginalUrl ?? $"data:{img.MimeType};base64,{img.EnsureBase64()}";
                    w.WriteString("url", url);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
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

    private static string SerializeAssistantToolCallMessage(string text, string reasoningContent, IReadOnlyList<OpenAIToolCall> toolCalls)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("role", "assistant");
            // content is required by some OpenAI-compat providers even when empty.
            w.WriteString("content", text ?? string.Empty);
            // DeepSeek's reasoner models require the chain-of-thought to be replayed
            // back to the API in subsequent calls. Emit only when populated so this
            // is a no-op for non-reasoning providers (OpenAI / Groq / Together / Meta).
            if (!string.IsNullOrEmpty(reasoningContent))
            {
                w.WriteString("reasoning_content", reasoningContent);
            }
            w.WritePropertyName("tool_calls");
            w.WriteStartArray();
            foreach (var tc in toolCalls)
            {
                w.WriteStartObject();
                w.WriteString("id", tc.Id);
                w.WriteString("type", "function");
                w.WritePropertyName("function");
                w.WriteStartObject();
                w.WriteString("name", tc.Name);
                var args = string.IsNullOrWhiteSpace(tc.ArgumentsJson) ? "{}" : tc.ArgumentsJson;
                w.WriteString("arguments", args);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string SerializeToolResultMessage(string toolCallId, string content)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("role", "tool");
            w.WriteString("tool_call_id", toolCallId);
            w.WriteString("content", content);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Reads an OpenAI-compatible SSE stream that may include tool_calls.
    /// Accumulates text deltas (and forwards each chunk to onToken for live
    /// console output) and tool_call argument fragments by index. Captures
    /// finish_reason so the loop can decide whether to continue.
    /// </summary>
    private static OpenAIStreamResult ReadOpenAIStreamRich(HttpRequestMessage request, Action<string>? onToken)
    {
        var response = AIStreamingCmdletBase.s_httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
        AIStreamingCmdletBase.EnsureSuccess(response, "OpenAI-compat");

        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream);

        var text = new StringBuilder();
        var reasoning = new StringBuilder();
        var toolMeta = new SortedDictionary<int, (string Id, string Name, StringBuilder Args)>();
        int inputTokens = 0, outputTokens = 0;
        string? finishReason = null;

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

                // Usage chunk (final, when include_usage=true). Usage can also appear
                // alongside choices in some compat providers.
                if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
                {
                    if (u.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number)
                        inputTokens = p.GetInt32();
                    if (u.TryGetProperty("completion_tokens", out var c) && c.ValueKind == JsonValueKind.Number)
                        outputTokens = c.GetInt32();
                }

                if (!root.TryGetProperty("choices", out var choices)) continue;
                if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) continue;
                var first = choices[0];
                if (first.ValueKind != JsonValueKind.Object) continue;

                if (first.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                {
                    finishReason = fr.GetString();
                }

                if (!first.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object) continue;

                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    var chunk = content.GetString();
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        text.Append(chunk);
                        onToken?.Invoke(chunk!);
                    }
                }

                // DeepSeek reasoner emits chain-of-thought as a separate delta.reasoning_content
                // stream. Accumulate (but do not echo to the user terminal — reasoning is
                // internal). It is replayed back on subsequent tool-loop iterations.
                if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
                {
                    var rchunk = rc.GetString();
                    if (!string.IsNullOrEmpty(rchunk)) reasoning.Append(rchunk);
                }

                if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tc in tcs.EnumerateArray())
                    {
                        if (!tc.TryGetProperty("index", out var idxEl) || idxEl.ValueKind != JsonValueKind.Number) continue;
                        var idx = idxEl.GetInt32();
                        if (!toolMeta.TryGetValue(idx, out var meta))
                        {
                            meta = (Id: string.Empty, Name: string.Empty, Args: new StringBuilder());
                            toolMeta[idx] = meta;
                        }
                        if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        {
                            meta.Id = idEl.GetString() ?? meta.Id;
                            toolMeta[idx] = meta;
                        }
                        if (tc.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                        {
                            if (fn.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                            {
                                meta.Name = nm.GetString() ?? meta.Name;
                                toolMeta[idx] = meta;
                            }
                            if (fn.TryGetProperty("arguments", out var ag) && ag.ValueKind == JsonValueKind.String)
                            {
                                meta.Args.Append(ag.GetString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
            {
                // Skip malformed / unexpected-shape SSE chunks rather than aborting the stream.
            }
        }

        var toolCalls = new List<OpenAIToolCall>(toolMeta.Count);
        foreach (var kv in toolMeta)
        {
            toolCalls.Add(new OpenAIToolCall(kv.Value.Id, kv.Value.Name, kv.Value.Args.ToString()));
        }
        return new OpenAIStreamResult(text.ToString(), reasoning.ToString(), toolCalls, inputTokens, outputTokens, finishReason);
    }

    private record OpenAIStreamResult(
        string Text,
        string ReasoningContent,
        IReadOnlyList<OpenAIToolCall> ToolCalls,
        int InputTokens,
        int OutputTokens,
        string? FinishReason);

    private record OpenAIToolCall(string Id, string Name, string ArgumentsJson);
}
