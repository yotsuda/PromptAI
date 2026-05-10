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
    /// <summary>Builds the chat completions request JSON.</summary>
    public static string BuildJson(
        string model, int maxTokens, string? systemPrompt,
        AIResponse? history, string userContent, string[]? images)
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
}
