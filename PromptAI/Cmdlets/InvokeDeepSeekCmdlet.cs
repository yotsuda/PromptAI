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
        => Call(userContent, SystemPrompt, Model, MaxTokens, History, Image,
                Temperature, TopP, StopSequence, Json.IsPresent, Schema, t => Host.UI.Write(t));

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
        Action<string>? onToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? throw new PSInvalidOperationException("DEEPSEEK_API_KEY environment variable is not set.");

        var resolvedModel = string.IsNullOrEmpty(model) ? DefaultModel : model;
        var effectiveSystemPrompt = systemPrompt ?? history?.SystemPrompt;

        // DeepSeek supports response_format json_object but not strict json_schema
        // (varies by model). When -Schema is supplied we fall back to instructing
        // the schema in the system prompt + json_object mode for best-effort.
        if (schema != null)
        {
            var schemaJson = JsonHelpers.SerializeHashtable(schema);
            var note = $"Respond with valid JSON only, conforming to this schema: {schemaJson}";
            effectiveSystemPrompt = string.IsNullOrEmpty(effectiveSystemPrompt) ? note : effectiveSystemPrompt + "\n\n" + note;
        }

        var jsonText = OpenAICompat.BuildJson(resolvedModel, maxTokens, effectiveSystemPrompt, history, userContent, images,
                                              temperature, topP, stopSequence, json || schema != null, schema: null);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(jsonText, Encoding.UTF8, "application/json");

        var (text, inTok, outTok) = ReadSSEStream(request, OpenAICompat.ParseDelta, OpenAICompat.ParseUsage, onToken);
        return new ApiCallResult(text, resolvedModel, inTok, outTok);
    }
}
