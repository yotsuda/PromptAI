using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to the OpenAI API with streaming.
/// Supports multi-turn (-History), image input (-Image), and reports token usage.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "GPT")]
[OutputType(typeof(AIResponse))]
public class InvokeGPTCmdlet : AIStreamingCmdletBase
{
    [Parameter]
    [ArgumentCompleter(typeof(GPTModelCompleter))]
    public new string? Model { get; set; }

    private const string DefaultModel = "gpt-4o";

    protected override string ProviderName => "OpenAI";

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
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new PSInvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? DefaultModel : model;
        var effectiveSystemPrompt = systemPrompt ?? history?.SystemPrompt;

        var json = OpenAICompat.BuildJson(resolvedModel, maxTokens, effectiveSystemPrompt, history, userContent, images);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var (text, inTok, outTok) = ReadSSEStream(request, OpenAICompat.ParseDelta, OpenAICompat.ParseUsage, onToken);
        return new ApiCallResult(text, resolvedModel, inTok, outTok);
    }
}
