using System.Management.Automation;
using System.Text;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to the DeepSeek API with streaming. OpenAI-compatible endpoint.
/// Supports multi-turn (-History) and reports token usage. DeepSeek's current models
/// (deepseek-v4-flash / deepseek-v4-pro) do not accept image input — -Image will
/// fail at the API.
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

    protected override ApiCallResult CallAPI(string userContent)
        => Call(userContent, SystemPrompt, Model, MaxTokens, History, Image, t => Host.UI.Write(t));

    public static ApiCallResult Call(
        string userContent,
        string? systemPrompt,
        string? model,
        int maxTokens,
        AIResponse? history,
        string[]? images,
        Action<string>? onToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? throw new PSInvalidOperationException("DEEPSEEK_API_KEY environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? DefaultModel : model;

        var json = OpenAICompat.BuildJson(resolvedModel, maxTokens, systemPrompt, history, userContent, images);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var (text, inTok, outTok) = ReadSSEStream(request, OpenAICompat.ParseDelta, OpenAICompat.ParseUsage, onToken);
        return new ApiCallResult(text, resolvedModel, inTok, outTok);
    }
}
