using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to the Google Gemini API with streaming.
/// Tokens are displayed on the console as they arrive via Host.UI.Write.
/// Returns an AIResponse object (empty format suppresses Out-Default display).
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

    protected override (string text, string model) CallAPI(string userContent)
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new PSInvalidOperationException("GEMINI_API_KEY environment variable is not set.");

        var model = string.IsNullOrEmpty(Model) ? DefaultModel : Model;

        var json = BuildJson(userContent);

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var text = ReadSSEStream(request, ParseDelta);
        return (text, model);
    }

    private string BuildJson(string userContent)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();

            if (!string.IsNullOrEmpty(SystemPrompt))
            {
                w.WritePropertyName("systemInstruction");
                w.WriteStartObject();
                w.WritePropertyName("parts");
                w.WriteStartArray();
                w.WriteStartObject();
                w.WriteString("text", SystemPrompt);
                w.WriteEndObject();
                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WritePropertyName("contents");
            w.WriteStartArray();
            w.WriteStartObject();
            w.WritePropertyName("parts");
            w.WriteStartArray();
            w.WriteStartObject();
            w.WriteString("text", userContent);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();

            if (MaxTokens != 4096)
            {
                w.WritePropertyName("generationConfig");
                w.WriteStartObject();
                w.WriteNumber("maxOutputTokens", MaxTokens);
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string? ParseDelta(JsonElement root)
    {
        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var text))
            {
                return text.GetString();
            }
        }

        return null;
    }
}
