using System.Management.Automation;
using System.Net.Http;
using System.Text;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to a Llama model via Groq (default), Meta's official Llama API,
/// or Together AI. All three are OpenAI-compatible — only the URL, env var, and
/// default model differ. Supports multi-turn (-History), image input (-Image —
/// vision-capable models only), tool calling (-Tool), and reports token usage.
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
                Temperature, TopP, StopSequence, Json.IsPresent, Schema, Tool, MaxToolIterations,
                BuildAIScriptContext(),
                t => Host.UI.Write(t));

    /// <summary>Compat overload for Compare-AI; exec_powershell disabled.</summary>
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
        System.Collections.Hashtable[]? toolsRaw,
        int maxToolIterations,
        Action<string>? onToken)
        => Call(provider, userContent, systemPrompt, model, maxTokens, history, images,
                temperature, topP, stopSequence, json, schema, toolsRaw, maxToolIterations,
                scriptContext: null, onToken);

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
        System.Collections.Hashtable[]? toolsRaw,
        int maxToolIterations,
        AIScriptContext? scriptContext,
        Action<string>? onToken)
    {
        var (envName, endpoint, defaultModel) = GetProviderConfig(provider);

        var apiKey = Environment.GetEnvironmentVariable(envName)
            ?? throw new PSInvalidOperationException($"{envName} environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? defaultModel : model;
        var effectiveSystemPrompt = systemPrompt ?? history?.SystemPrompt;

        HttpRequestMessage MakeRequest(string body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return req;
        }

        return OpenAICompat.CallWithTools(
            resolvedModel, maxTokens, effectiveSystemPrompt, history, userContent, images,
            temperature, topP, stopSequence, json, schema, toolsRaw, maxToolIterations,
            scriptContext, MakeRequest, onToken);
    }

    private static (string envName, string endpoint, string defaultModel) GetProviderConfig(string provider) => provider switch
    {
        "Meta"     => ("LLAMA_API_KEY",    "https://api.llama.com/v1/chat/completions",       "Llama-4-Maverick-17B-128E-Instruct-FP8"),
        "Together" => ("TOGETHER_API_KEY", "https://api.together.xyz/v1/chat/completions",    "meta-llama/Llama-3.3-70B-Instruct-Turbo"),
        _          => ("GROQ_API_KEY",     "https://api.groq.com/openai/v1/chat/completions", "llama-3.3-70b-versatile"),
    };
}
