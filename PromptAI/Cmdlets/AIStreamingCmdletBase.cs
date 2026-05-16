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
/// ToolCalls is non-null only when -Tool was supplied and the model invoked
/// at least one tool; the list is in invocation order across all iterations.
/// </summary>
public record ApiCallResult(
    string Text,
    string Model,
    int InputTokens,
    int OutputTokens,
    IReadOnlyList<ToolCallRecord>? ToolCalls = null);

/// <summary>
/// Validated tool descriptor parsed from a `-Tool` hashtable. Internal — providers
/// receive these via <see cref="AIStreamingCmdletBase.ParseTools"/> rather than
/// re-parsing the raw hashtable shape themselves.
/// </summary>
internal record ToolDescriptor(
    string Name,
    string Description,
    System.Collections.Hashtable Parameters,
    ScriptBlock Run);

/// <summary>
/// Runtime bundle threaded into each provider's tool loop so the implicit
/// exec_powershell tool can prompt the user and execute scripts in their
/// session. Null means the cmdlet is in policy=Off mode (or there is no
/// host / runspace) and exec_powershell must not be exposed to the AI.
/// </summary>
public record AIScriptContext(
    string Policy,
    System.Management.Automation.Host.PSHost Host,
    System.Management.Automation.Runspaces.Runspace Runspace);

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

    /// <summary>
    /// Sampling temperature. Lower = more deterministic, higher = more creative.
    /// Each provider defines its own valid range; check the provider's docs.
    /// Pass nothing to use the provider's default.
    /// </summary>
    [Parameter]
    public double? Temperature { get; set; }

    /// <summary>
    /// Nucleus sampling cutoff. Pass nothing to use the provider's default.
    /// </summary>
    [Parameter]
    public double? TopP { get; set; }

    /// <summary>
    /// One or more strings that, if generated, halt the response.
    /// Maps to provider-specific fields (Anthropic stop_sequences, OpenAI stop,
    /// Gemini stopSequences).
    /// </summary>
    [Parameter]
    public string[]? StopSequence { get; set; }

    /// <summary>
    /// Request a JSON-only response. Provider mapping:
    /// OpenAI/Llama/DeepSeek use response_format=json_object; Gemini uses
    /// responseMimeType=application/json; Claude uses an assistant-message
    /// prefill of "{". Pass -Schema instead for strict structured output.
    /// </summary>
    [Parameter]
    public SwitchParameter Json { get; set; }

    /// <summary>
    /// JSON schema (as a Hashtable) the response must conform to. Implies -Json.
    /// OpenAI/Llama use response_format json_schema (strict); Gemini uses
    /// responseSchema; Claude/DeepSeek fall back to schema-in-system-prompt
    /// (best-effort, not strict).
    /// </summary>
    [Parameter]
    public System.Collections.Hashtable? Schema { get; set; }

    /// <summary>
    /// One or more tool declarations the model may invoke. Each hashtable must
    /// contain Name (string), Description (string), Parameters (Hashtable —
    /// a JSON Schema describing the tool arguments), and Run (ScriptBlock —
    /// receives the parsed arguments as a Hashtable in $args[0] and returns
    /// the result, which is stringified and sent back to the model).
    /// </summary>
    [Parameter]
    public System.Collections.Hashtable[]? Tool { get; set; }

    /// <summary>
    /// Maximum number of tool-execution rounds per call. The model issues a
    /// batch of tool calls, the cmdlet executes them and feeds the results
    /// back, and the model decides whether to call more. This cap stops a
    /// runaway loop; raise it for genuinely long agent tasks.
    /// </summary>
    [Parameter]
    [ValidateRange(1, 100)]
    public int MaxToolIterations { get; set; } = 10;

    /// <summary>
    /// Controls whether the AI may write and execute ad-hoc PowerShell via the
    /// implicit exec_powershell tool.
    ///   Prompt         (default) — read-only scripts auto-run; state-modifying
    ///                              scripts get a WhatIf preview and require
    ///                              user approval (Yes/No/Edit/Quit).
    ///   AlwaysApprove          — run everything without prompting (CI / trusted).
    ///   AlwaysWhatIf           — never run for real; -WhatIf preview only.
    ///   Off                    — do not expose exec_powershell to the AI at all;
    ///                            the AI can only invoke tools passed via -Tool.
    /// </summary>
    [Parameter]
    [ValidateSet("Prompt", "AlwaysApprove", "AlwaysWhatIf", "Off")]
    public string AIScriptPolicy { get; set; } = "Prompt";

    /// <summary>The reserved name for the implicit AI-script execution tool.</summary>
    internal const string ExecPowerShellName = "exec_powershell";

    /// <summary>
    /// Description string the AI sees for the implicit tool. Concrete enough
    /// that the model knows when to reach for it and what to put in script/purpose.
    /// </summary>
    internal const string ExecPowerShellDescription =
        "Execute an ad-hoc PowerShell script in the user's current session and return its output. " +
        "Use this when no pre-declared tool covers the task. Read-only scripts run immediately. " +
        "State-modifying scripts show the user a -WhatIf preview and require explicit approval " +
        "before real execution. Errors and rejections are returned to you as the tool result.";

    internal static System.Collections.Hashtable BuildExecPowerShellSchema()
    {
        return new System.Collections.Hashtable
        {
            ["type"]       = "object",
            ["properties"] = new System.Collections.Hashtable
            {
                ["script"]  = new System.Collections.Hashtable
                {
                    ["type"]        = "string",
                    ["description"] = "The PowerShell code to execute. May span multiple lines. Prefer Get-* / read-only cmdlets when possible — state-modifying operations require user approval.",
                },
                ["purpose"] = new System.Collections.Hashtable
                {
                    ["type"]        = "string",
                    ["description"] = "One-sentence human-readable explanation of what this script does. Shown to the user during approval.",
                },
            },
            ["required"] = new[] { "script", "purpose" },
        };
    }

    /// <summary>Provider name for AIResponse metadata (e.g., "Anthropic", "OpenAI").</summary>
    protected abstract string ProviderName { get; }

    protected override void ProcessRecord()
    {
        _promptLines.Add(Prompt);
    }

    /// <summary>
    /// Builds the AIScriptContext for this invocation, or returns null when
    /// policy is Off (exec_powershell must not be exposed). Detection of
    /// non-interactive hosts happens lazily inside AIScriptExecutor when a
    /// real prompt is required — that way read-only scripts auto-execute even
    /// in CI / non-interactive sessions, and only state-modifying scripts in
    /// Prompt mode fail with a clear "no interactive host" error.
    /// </summary>
    protected AIScriptContext? BuildAIScriptContext()
    {
        if (string.Equals(AIScriptPolicy, "Off", StringComparison.OrdinalIgnoreCase))
            return null;

        var runspace = System.Management.Automation.Runspaces.Runspace.DefaultRunspace;
        if (runspace == null) return null;

        return new AIScriptContext(AIScriptPolicy, Host, runspace);
    }

    protected override void EndProcessing()
    {
        var userContent = string.Join("\n", _promptLines);

        // Effective system prompt: explicit -SystemPrompt wins; otherwise inherit
        // from -History so a chained conversation keeps its persona without the
        // caller having to re-specify it on every turn.
        var effectiveSystemPrompt = SystemPrompt ?? History?.SystemPrompt;

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
            turns: turns,
            systemPrompt: effectiveSystemPrompt,
            toolCalls: result.ToolCalls);

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

    /// <summary>
    /// Validates `-Tool` hashtables and returns descriptors. Throws a
    /// PSArgumentException with a precise message when any tool is malformed,
    /// so misconfigured tools fail before the API call rather than mid-stream.
    /// </summary>
    internal static ToolDescriptor[]? ParseTools(System.Collections.Hashtable[]? tools)
    {
        if (tools == null || tools.Length == 0) return null;

        var result = new ToolDescriptor[tools.Length];
        for (var i = 0; i < tools.Length; i++)
        {
            var t = tools[i];
            string name = (t["Name"] as string)
                ?? throw new PSArgumentException($"Tool[{i}].Name is required and must be a string.");
            string description = (t["Description"] as string)
                ?? throw new PSArgumentException($"Tool[{i}].Description is required and must be a string.");
            var parameters = (t["Parameters"] as System.Collections.Hashtable)
                ?? throw new PSArgumentException($"Tool[{i}].Parameters is required and must be a Hashtable (JSON Schema).");
            var run = (t["Run"] as ScriptBlock)
                ?? throw new PSArgumentException($"Tool[{i}].Run is required and must be a ScriptBlock.");
            result[i] = new ToolDescriptor(name, description, parameters, run);
        }
        return result;
    }

    /// <summary>
    /// Routes an exec_powershell tool call to AIScriptExecutor. Returns the
    /// real-execution result string (after WhatIf preview + approval if state-
    /// modifying), or the rejection notice when the user declined. Lets
    /// PipelineStoppedException propagate so a [Q]uit aborts the whole call.
    /// </summary>
    internal static (string Result, string? Error) RunExecPowerShell(string argumentsJson, AIScriptContext ctx)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var root = doc.RootElement;
            var script  = root.TryGetProperty("script",  out var s) ? s.GetString() ?? "" : "";
            var purpose = root.TryGetProperty("purpose", out var p) ? p.GetString() ?? "" : "(no purpose given)";
            if (string.IsNullOrWhiteSpace(script))
                return (string.Empty, "Missing or empty 'script' argument.");

            var output = AIScriptExecutor.ExecuteWithPolicy(script, purpose, ctx.Policy, ctx.Host, ctx.Runspace);
            return (output, null);
        }
        catch (PipelineStoppedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Runs a tool scriptblock with the model-supplied arguments and returns the
    /// stringified result, or the exception message when the scriptblock throws.
    /// Errors are surfaced to the model as tool results (not raised as cmdlet
    /// errors) so the model can self-correct.
    /// </summary>
    internal static (string Result, string? Error) RunTool(ToolDescriptor tool, System.Collections.Hashtable arguments)
    {
        try
        {
            var output = tool.Run.InvokeReturnAsIs(arguments);
            return (StringifyToolOutput(output), null);
        }
        catch (Exception ex)
        {
            return (string.Empty, ex.Message);
        }
    }

    private static string StringifyToolOutput(object? output)
    {
        if (output == null) return string.Empty;
        if (output is string s) return s;
        if (output is System.Management.Automation.PSObject pso) return pso.ToString();
        try
        {
            return JsonSerializer.Serialize(output);
        }
        catch
        {
            return output.ToString() ?? string.Empty;
        }
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
