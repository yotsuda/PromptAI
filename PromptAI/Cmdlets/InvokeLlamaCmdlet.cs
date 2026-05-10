using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to a Llama model with streaming.
/// Llama is open-weight, so multiple hosts serve the same model family;
/// -Provider switches between Groq (default), Meta's official Llama API, and Together AI.
/// All three expose OpenAI-compatible chat completions, so the request/response
/// shape mirrors InvokeGPTCmdlet.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "Llama")]
[OutputType(typeof(AIResponse))]
public class InvokeLlamaCmdlet : AIStreamingCmdletBase
{
    [Parameter]
    [ValidateSet("Groq", "Meta", "Together")]
    public string Provider { get; set; } = "Groq";

    [Parameter]
    [ArgumentCompleter(typeof(LlamaModelCompleter))]
    public new string? Model { get; set; }

    protected override string ProviderName => Provider;

    protected override (string text, string model) CallAPI(string userContent)
    {
        var (envName, endpoint, defaultModel) = GetProviderConfig();

        var apiKey = Environment.GetEnvironmentVariable(envName)
            ?? throw new PSInvalidOperationException($"{envName} environment variable is not set.");

        var model = string.IsNullOrEmpty(Model) ? defaultModel : Model;

        var json = BuildJson(model, userContent);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var text = ReadSSEStream(request, ParseDelta);
        return (text, model);
    }

    private (string envName, string endpoint, string defaultModel) GetProviderConfig() => Provider switch
    {
        "Meta"     => ("LLAMA_API_KEY",    "https://api.llama.com/v1/chat/completions",       "Llama-4-Maverick-17B-128E-Instruct-FP8"),
        "Together" => ("TOGETHER_API_KEY", "https://api.together.xyz/v1/chat/completions",    "meta-llama/Llama-3.3-70B-Instruct-Turbo"),
        _          => ("GROQ_API_KEY",     "https://api.groq.com/openai/v1/chat/completions", "llama-3.3-70b-versatile"),
    };

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
