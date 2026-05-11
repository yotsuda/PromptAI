using System.Management.Automation;
using System.Text;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to a Llama model via Groq (default), Meta's official Llama API,
/// or Together AI. All three are OpenAI-compatible — only the URL, env var, and
/// default model differ. Supports multi-turn (-History), image input (-Image —
/// vision-capable models only), and reports token usage.
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

    protected override ApiCallResult CallAPI(string userContent)
        => Call(Provider, userContent, SystemPrompt, Model, MaxTokens, History, Image,
                Temperature, TopP, StopSequence, Json.IsPresent, Schema, t => Host.UI.Write(t));

    public static ApiCallResult Call(
        string provider,
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
        var (envName, endpoint, defaultModel) = GetProviderConfig(provider);

        var apiKey = Environment.GetEnvironmentVariable(envName)
            ?? throw new PSInvalidOperationException($"{envName} environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? defaultModel : model;
        var effectiveSystemPrompt = systemPrompt ?? history?.SystemPrompt;

        var jsonText = OpenAICompat.BuildJson(resolvedModel, maxTokens, effectiveSystemPrompt, history, userContent, images,
                                              temperature, topP, stopSequence, json, schema);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(jsonText, Encoding.UTF8, "application/json");

        var (text, inTok, outTok) = ReadSSEStream(request, OpenAICompat.ParseDelta, OpenAICompat.ParseUsage, onToken);
        return new ApiCallResult(text, resolvedModel, inTok, outTok);
    }

    private static (string envName, string endpoint, string defaultModel) GetProviderConfig(string provider) => provider switch
    {
        "Meta"     => ("LLAMA_API_KEY",    "https://api.llama.com/v1/chat/completions",       "Llama-4-Maverick-17B-128E-Instruct-FP8"),
        "Together" => ("TOGETHER_API_KEY", "https://api.together.xyz/v1/chat/completions",    "meta-llama/Llama-3.3-70B-Instruct-Turbo"),
        _          => ("GROQ_API_KEY",     "https://api.groq.com/openai/v1/chat/completions", "llama-3.3-70b-versatile"),
    };
}
