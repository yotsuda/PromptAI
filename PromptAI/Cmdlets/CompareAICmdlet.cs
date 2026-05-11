using System.Diagnostics;
using System.Management.Automation;

namespace PromptAI.Cmdlets;

/// <summary>
/// Sends the same prompt to multiple AI providers in parallel and outputs one
/// AIResponse per provider. Default: every provider whose API key environment
/// variable is set. Pipe through Format-Table for a side-by-side comparison.
/// </summary>
[Cmdlet(VerbsData.Compare, "AI")]
[OutputType(typeof(AIResponse))]
public class CompareAICmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string Prompt { get; set; } = null!;

    [Parameter(Position = 1)]
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Which providers to query. Defaults to every provider whose API key env var
    /// is set. For Llama, the default sub-provider is Groq.
    /// </summary>
    [Parameter]
    [ValidateSet("Claude", "GPT", "Gemini", "Llama", "DeepSeek")]
    public string[]? Provider { get; set; }

    [Parameter]
    [ValidateRange(1, 128000)]
    public int MaxTokens { get; set; } = 4096;

    private readonly List<string> _promptLines = [];

    protected override void ProcessRecord() => _promptLines.Add(Prompt);

    protected override void EndProcessing()
    {
        var userContent = string.Join("\n", _promptLines);
        var providers = Provider ?? DetectConfiguredProviders();

        if (providers.Length == 0)
        {
            WriteWarning("No providers selected and no API keys detected. Set at least one of: " +
                         "ANTHROPIC_API_KEY, OPENAI_API_KEY, GEMINI_API_KEY, GROQ_API_KEY, DEEPSEEK_API_KEY.");
            return;
        }

        // Capture parameters into locals so the lambdas don't close over `this` in unsafe ways.
        var sysPrompt = SystemPrompt;
        var maxTokens = MaxTokens;

        var tasks = providers.Select(p => Task.Run<AIResponse>(() =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = CallProvider(p, userContent, sysPrompt, maxTokens);
                sw.Stop();
                var cost = Pricing.Compute(result.Model, result.InputTokens, result.OutputTokens);
                var turns = new List<ConversationTurn>
                {
                    new("user", userContent),
                    new("assistant", result.Text),
                };
                return new AIResponse(
                    text: result.Text,
                    model: result.Model,
                    provider: p,
                    inputTokens: result.InputTokens,
                    outputTokens: result.OutputTokens,
                    estimatedCostUSD: cost,
                    duration: sw.Elapsed,
                    turns: turns);
            }
            catch (Exception ex)
            {
                sw.Stop();
                // Surface the failure as an AIResponse so the table view stays uniform.
                return new AIResponse(
                    text: $"[error: {ex.Message}]",
                    model: "(none)",
                    provider: p,
                    duration: sw.Elapsed);
            }
        })).ToArray();

        Task.WaitAll(tasks);

        foreach (var t in tasks)
        {
            WriteObject(t.Result);
        }
    }

    private static ApiCallResult CallProvider(string provider, string userContent, string? systemPrompt, int maxTokens)
        => provider switch
        {
            "Claude"   => InvokeClaudeCmdlet.Call(  userContent, systemPrompt, null, maxTokens, null, null, null, null, null, false, null, null),
            "GPT"      => InvokeGPTCmdlet.Call(     userContent, systemPrompt, null, maxTokens, null, null, null, null, null, false, null, null),
            "Gemini"   => InvokeGeminiCmdlet.Call(  userContent, systemPrompt, null, maxTokens, null, null, null, null, null, false, null, null),
            "Llama"    => InvokeLlamaCmdlet.Call(   "Groq", userContent, systemPrompt, null, maxTokens, null, null, null, null, null, false, null, null),
            "DeepSeek" => InvokeDeepSeekCmdlet.Call(userContent, systemPrompt, null, maxTokens, null, null, null, null, null, false, null, null),
            _          => throw new PSInvalidOperationException($"Unknown provider: {provider}"),
        };

    private static string[] DetectConfiguredProviders()
    {
        var configured = new List<string>();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))) configured.Add("Claude");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))    configured.Add("GPT");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GEMINI_API_KEY")))    configured.Add("Gemini");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GROQ_API_KEY")))      configured.Add("Llama");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")))  configured.Add("DeepSeek");
        return configured.ToArray();
    }
}
