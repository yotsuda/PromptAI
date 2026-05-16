using System.Management.Automation;
using System.Net.Http;
using System.Text;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to the OpenAI API with streaming.
/// Supports multi-turn (-History), image input (-Image), tool calling (-Tool),
/// and reports token usage.
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
        => Call(userContent, SystemPrompt, Model, MaxTokens, History, Image,
                Temperature, TopP, StopSequence, Json.IsPresent, Schema, Tool, MaxToolIterations,
                BuildAIScriptContext(),
                t => Host.UI.Write(t));

    /// <summary>Compat overload for Compare-AI and external callers; exec_powershell disabled.</summary>
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
        System.Collections.Hashtable[]? toolsRaw,
        int maxToolIterations,
        Action<string>? onToken)
        => Call(userContent, systemPrompt, model, maxTokens, history, images,
                temperature, topP, stopSequence, json, schema, toolsRaw, maxToolIterations,
                scriptContext: null, onToken);

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
        System.Collections.Hashtable[]? toolsRaw,
        int maxToolIterations,
        AIScriptContext? scriptContext,
        Action<string>? onToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new PSInvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? DefaultModel : model;
        var effectiveSystemPrompt = systemPrompt ?? history?.SystemPrompt;

        HttpRequestMessage MakeRequest(string body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return req;
        }

        return OpenAICompat.CallWithTools(
            resolvedModel, maxTokens, effectiveSystemPrompt, history, userContent, images,
            temperature, topP, stopSequence, json, schema, toolsRaw, maxToolIterations,
            scriptContext, MakeRequest, onToken);
    }
}
