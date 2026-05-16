using System.Management.Automation;
using System.Net.Http;
using System.Text;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends a prompt to the DeepSeek API with streaming. OpenAI-compatible endpoint.
/// Supports multi-turn (-History), tool calling (-Tool), and reports token usage.
/// DeepSeek's current models (deepseek-v4-flash / deepseek-v4-pro) do not accept
/// image input — -Image will fail at the API.
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
        => Call(userContent, SystemPrompt, Model, MaxTokens, History, Image,
                Temperature, TopP, StopSequence, Json.IsPresent, Schema, Tool, MaxToolIterations,
                BuildAIScriptContext(),
                t => Host.UI.Write(t));

    /// <summary>Compat overload for Compare-AI; exec_powershell disabled.</summary>
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
        var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? throw new PSInvalidOperationException("DEEPSEEK_API_KEY environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? DefaultModel : model;
        var effectiveSystemPrompt = systemPrompt ?? history?.SystemPrompt;

        // DeepSeek supports response_format json_object but not strict json_schema
        // (varies by model). When -Schema is supplied we fall back to instructing
        // the schema in the system prompt + json_object mode for best-effort.
        // Skip when -Tool is set; CallWithTools throws on schema+tool combo.
        bool jsonForLoop = json;
        System.Collections.Hashtable? schemaForLoop = schema;
        if (schema != null && toolsRaw == null)
        {
            var schemaJson = JsonHelpers.SerializeHashtable(schema);
            var note = $"Respond with valid JSON only, conforming to this schema: {schemaJson}";
            effectiveSystemPrompt = string.IsNullOrEmpty(effectiveSystemPrompt) ? note : effectiveSystemPrompt + "\n\n" + note;
            jsonForLoop = true;
            schemaForLoop = null;
        }

        HttpRequestMessage MakeRequest(string body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return req;
        }

        return OpenAICompat.CallWithTools(
            resolvedModel, maxTokens, effectiveSystemPrompt, history, userContent, images,
            temperature, topP, stopSequence, jsonForLoop, schemaForLoop, toolsRaw, maxToolIterations,
            scriptContext, MakeRequest, onToken);
    }
}
