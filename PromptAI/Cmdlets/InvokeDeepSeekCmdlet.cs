using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to the DeepSeek API with streaming.
/// DeepSeek's official API is OpenAI-compatible, so the request/response shape
/// mirrors InvokeGPTCmdlet. Use deepseek-chat for general use, deepseek-reasoner for reasoning.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "DeepSeek")]
[OutputType(typeof(AIResponse))]
public class InvokeDeepSeekCmdlet : AIStreamingCmdletBase
{
    [Parameter]
    [ArgumentCompleter(typeof(DeepSeekModelCompleter))]
    public new string? Model { get; set; }

    private const string DefaultModel = "deepseek-v4-flash";

    protected override string ProviderName => "DeepSeek";

    protected override (string text, string model) CallAPI(string userContent)
    {
        var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? throw new PSInvalidOperationException("DEEPSEEK_API_KEY environment variable is not set.");

        var model = string.IsNullOrEmpty(Model) ? DefaultModel : Model;

        var json = BuildJson(model, userContent);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var text = ReadSSEStream(request, ParseDelta);
        return (text, model);
    }

    private string BuildJson(string model, string userContent)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WriteNumber("max_tokens", MaxTokens);
            w.WriteBoolean("stream", true);

            w.WritePropertyName("messages");
            w.WriteStartArray();

            if (!string.IsNullOrEmpty(SystemPrompt))
            {
                w.WriteStartObject();
                w.WriteString("role", "system");
                w.WriteString("content", SystemPrompt);
                w.WriteEndObject();
            }

            w.WriteStartObject();
            w.WriteString("role", "user");
            w.WriteString("content", userContent);
            w.WriteEndObject();

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string? ParseDelta(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0)
        {
            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var content))
            {
                return content.GetString();
            }
        }

        return null;
    }
}
