using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to the Google Gemini API with streaming.
/// Supports multi-turn (-History), image input (-Image), and reports token usage.
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
                Temperature, TopP, StopSequence, Json.IsPresent, Schema, t => Host.UI.Write(t));

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
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new PSInvalidOperationException("GEMINI_API_KEY environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? DefaultModel : model;
        var effectiveSystemPrompt = systemPrompt ?? history?.SystemPrompt;

        var jsonText = BuildJson(maxTokens, effectiveSystemPrompt, history, userContent, images,
                                 temperature, topP, stopSequence, json, schema);

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{resolvedModel}:streamGenerateContent?alt=sse";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(jsonText, Encoding.UTF8, "application/json");

        var (text, inTok, outTok) = ReadSSEStream(request, ParseDelta, ParseUsage, onToken);
        return new ApiCallResult(text, resolvedModel, inTok, outTok);
    }

    private static string BuildJson(
        int maxTokens, string? systemPrompt,
        AIResponse? history, string userContent, string[]? images,
        double? temperature, double? topP, string[]? stopSequence,
        bool json, System.Collections.Hashtable? schema)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
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

            w.WritePropertyName("contents");
            w.WriteStartArray();

            // Prior turns. Gemini uses role "model" for assistant.
            if (history?.Turns != null)
            {
                foreach (var t in history.Turns)
                {
                    w.WriteStartObject();
                    w.WriteString("role", t.Role == "assistant" ? "model" : t.Role);
                    w.WritePropertyName("parts");
                    w.WriteStartArray();
                    w.WriteStartObject();
                    w.WriteString("text", t.Content);
                    w.WriteEndObject();
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
            }

            // Current user turn — text part + inline_data parts for each image.
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
                    // Gemini's file_data only accepts GCS URIs, so always inline base64.
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

            w.WriteEndArray();

            // generationConfig only emitted if any non-default knob is set.
            bool hasGenConfig = maxTokens != 4096 || temperature.HasValue || topP.HasValue
                                || (stopSequence != null && stopSequence.Length > 0)
                                || json || schema != null;
            if (hasGenConfig)
            {
                w.WritePropertyName("generationConfig");
                w.WriteStartObject();
                if (maxTokens != 4096)        w.WriteNumber("maxOutputTokens", maxTokens);
                if (temperature.HasValue)     w.WriteNumber("temperature",     temperature.Value);
                if (topP.HasValue)            w.WriteNumber("topP",            topP.Value);
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

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

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
