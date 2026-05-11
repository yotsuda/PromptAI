using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to the Anthropic Claude API with streaming.
/// Supports multi-turn (-History), image input (-Image), and reports token usage.
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
                Temperature, TopP, StopSequence, Json.IsPresent, Schema, t => Host.UI.Write(t));

    /// <summary>Static helper used by the cmdlet and by Compare-AI.</summary>
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
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new PSInvalidOperationException("ANTHROPIC_API_KEY environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? DefaultModel : model;
        var effectiveSystemPrompt = systemPrompt ?? history?.SystemPrompt;

        // Anthropic has no native response_format. Best-effort: append schema to
        // system prompt and (below) prefill assistant with "{" so generation
        // starts as JSON.
        if (schema != null)
        {
            var schemaJson = JsonHelpers.SerializeHashtable(schema);
            var note = $"Respond with valid JSON only, conforming to this schema: {schemaJson}";
            effectiveSystemPrompt = string.IsNullOrEmpty(effectiveSystemPrompt) ? note : effectiveSystemPrompt + "\n\n" + note;
        }

        var jsonText = BuildJson(resolvedModel, maxTokens, effectiveSystemPrompt, history, userContent, images,
                                 temperature, topP, stopSequence, json || schema != null);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(jsonText, Encoding.UTF8, "application/json");

        var (text, inTok, outTok) = ReadSSEStream(request, ParseDelta, ParseUsage, onToken);

        // The "{" prefilled by BuildJson is not echoed back by the API, so re-attach
        // it to the result so callers can ConvertFrom-Json $r.Text without surprises.
        if (json || schema != null) text = "{" + text;

        return new ApiCallResult(text, resolvedModel, inTok, outTok);
    }

    private static string BuildJson(
        string model, int maxTokens, string? systemPrompt,
        AIResponse? history, string userContent, string[]? images,
        double? temperature, double? topP, string[]? stopSequence,
        bool jsonPrefill)
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
            // Anthropic appends to the prefill, so the response stream omits "{"
            // — the caller re-attaches it after reading the stream.
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
