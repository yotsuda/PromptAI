using System.Diagnostics;
using System.Management.Automation;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Result returned by per-provider API call helpers. Wraps the response text,
/// the resolved model name, and token usage extracted from the SSE stream.
/// AIStreamingCmdletBase wraps this into the user-facing AIResponse.
/// </summary>
public record ApiCallResult(string Text, string Model, int InputTokens, int OutputTokens);

/// <summary>
/// Base class for AI streaming cmdlets. Handles parameter binding (Prompt,
/// SystemPrompt, Model, MaxTokens, History, Image), pipeline accumulation,
/// stopwatch / cost / turns assembly, and AIResponse output. Each provider
/// implements <see cref="CallAPI"/> to do the provider-specific HTTP request.
/// </summary>
public abstract class AIStreamingCmdletBase : PSCmdlet
{
    internal static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Holds the text output from the last Invoke-X call. The MCP polling engine
    /// clears this before each command execution and reads it afterward, so only
    /// MCP-triggered results are included in the response.
    /// </summary>
    public static string? LastResponse { get; set; }

    private readonly List<string> _promptLines = [];

    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string Prompt { get; set; } = null!;

    [Parameter(Position = 1)]
    public string? SystemPrompt { get; set; }

    [Parameter]
    public string? Model { get; set; }

    [Parameter]
    [ValidateRange(1, 128000)]
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Prior conversation. Pass an AIResponse from an earlier call to continue
    /// the conversation. The new turn is appended; the returned AIResponse carries
    /// the full updated history.
    /// </summary>
    [Parameter]
    public AIResponse? History { get; set; }

    /// <summary>
    /// One or more image inputs for multimodal models. Each entry is a local file
    /// path or HTTPS URL. Only attached to the current turn (not re-attached when
    /// chaining via -History). Use a vision-capable model.
    /// </summary>
    [Parameter]
    public string[]? Image { get; set; }

    /// <summary>Provider name for AIResponse metadata (e.g., "Anthropic", "OpenAI").</summary>
    protected abstract string ProviderName { get; }

    protected override void ProcessRecord()
    {
        _promptLines.Add(Prompt);
    }

    protected override void EndProcessing()
    {
        var userContent = string.Join("\n", _promptLines);

        var sw = Stopwatch.StartNew();
        var result = CallAPI(userContent);
        sw.Stop();

        var cost = Pricing.Compute(result.Model, result.InputTokens, result.OutputTokens);

        // Accumulate turns: prior history (if any) + new user turn + new assistant turn.
        var turns = new List<ConversationTurn>();
        if (History?.Turns != null) turns.AddRange(History.Turns);
        turns.Add(new ConversationTurn("user", userContent, Image));
        turns.Add(new ConversationTurn("assistant", result.Text));

        var response = new AIResponse(
            text: result.Text,
            model: result.Model,
            provider: ProviderName,
            inputTokens: result.InputTokens,
            outputTokens: result.OutputTokens,
            estimatedCostUSD: cost,
            duration: sw.Elapsed,
            turns: turns);

        LastResponse = result.Text;
        WriteObject(response);
    }

    /// <summary>
    /// Implemented by derived classes. The instance has access to History, Image,
    /// SystemPrompt, Model, and MaxTokens via inherited properties.
    /// </summary>
    protected abstract ApiCallResult CallAPI(string userContent);

    /// <summary>
    /// Reads an SSE stream, extracts text deltas via the provided parser, optionally
    /// extracts usage info per chunk, streams each text chunk to the console via
    /// onToken, and returns the accumulated text plus final input/output token counts.
    /// </summary>
    internal static (string text, int inputTokens, int outputTokens) ReadSSEStream(
        HttpRequestMessage request,
        Func<JsonElement, string?> parseDelta,
        Func<JsonElement, (int? input, int? output)>? parseUsage = null,
        Action<string>? onToken = null)
    {
        var response = s_httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
        EnsureSuccess(response);

        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream);

        var result = new StringBuilder();
        int inputTokens = 0;
        int outputTokens = 0;

        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line.AsSpan(6);
            if (data.SequenceEqual("[DONE]"))
                break;

            try
            {
                using var doc = JsonDocument.Parse(data.ToString());
                var chunk = parseDelta(doc.RootElement);

                if (chunk != null)
                {
                    result.Append(chunk);
                    onToken?.Invoke(chunk);
                }

                if (parseUsage != null)
                {
                    var (inTok, outTok) = parseUsage(doc.RootElement);
                    if (inTok.HasValue)  inputTokens  = inTok.Value;
                    if (outTok.HasValue) outputTokens = outTok.Value;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
            {
                // Skip malformed / unexpected-shape SSE chunks rather than aborting the entire stream.
            }
        }

        return (result.ToString(), inputTokens, outputTokens);
    }

    internal static void EnsureSuccess(HttpResponseMessage response, string? cmdletName = null)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var label = cmdletName ?? "API";
            throw new HttpRequestException($"{label}: API returned {(int)response.StatusCode}: {body}");
        }
    }
}
