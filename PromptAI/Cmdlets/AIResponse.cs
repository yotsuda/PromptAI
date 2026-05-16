namespace PromptAI.Cmdlets;

/// <summary>
/// Wraps an AI response text plus per-call metadata. Has a custom format
/// (empty display) so that WriteObject does not duplicate the streaming output
/// shown via Host.UI.Write. Behaves like a string in most contexts via implicit
/// conversion and ToString(). Pass to a subsequent Invoke-X via -History to
/// continue the conversation.
/// </summary>
public class AIResponse
{
    public string Text { get; }
    public string Model { get; }
    public string Provider { get; }

    /// <summary>Tokens in the request (prompt + history + system + images).</summary>
    public int InputTokens { get; }

    /// <summary>Tokens in the assistant's response.</summary>
    public int OutputTokens { get; }

    /// <summary>
    /// Best-effort cost estimate in USD based on a hard-coded pricing table.
    /// Null when the model is unknown to the table or usage was not reported.
    /// </summary>
    public decimal? EstimatedCostUSD { get; }

    /// <summary>Wall-clock duration of the API call.</summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Full conversation including this turn. Pass an AIResponse as -History
    /// to a subsequent Invoke-X and the new request will include all prior turns.
    /// </summary>
    public IReadOnlyList<ConversationTurn> Turns { get; }

    /// <summary>
    /// System prompt that was in effect for this turn. When the AIResponse is
    /// passed as -History to a subsequent Invoke-X call, the system prompt is
    /// inherited unless the new call explicitly overrides it with -SystemPrompt.
    /// </summary>
    public string? SystemPrompt { get; }

    /// <summary>
    /// Tool calls executed during this turn, in the order the model issued them.
    /// Empty when -Tool was not supplied or the model did not invoke any tool.
    /// Each record carries the tool name, the arguments the model produced
    /// (as JSON), and either the scriptblock's stringified result or the
    /// error message that was returned to the model.
    /// </summary>
    public IReadOnlyList<ToolCallRecord> ToolCalls { get; }

    public AIResponse(
        string text,
        string model,
        string provider,
        int inputTokens = 0,
        int outputTokens = 0,
        decimal? estimatedCostUSD = null,
        TimeSpan duration = default,
        IReadOnlyList<ConversationTurn>? turns = null,
        string? systemPrompt = null,
        IReadOnlyList<ToolCallRecord>? toolCalls = null)
    {
        Text = text;
        Model = model;
        Provider = provider;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        EstimatedCostUSD = estimatedCostUSD;
        Duration = duration;
        Turns = turns ?? Array.Empty<ConversationTurn>();
        SystemPrompt = systemPrompt;
        ToolCalls = toolCalls ?? Array.Empty<ToolCallRecord>();
    }

    public override string ToString() => Text;

    public static implicit operator string(AIResponse r) => r.Text;

    public int Length => Text.Length;
}

/// <summary>
/// One message in a conversation. Role is "user" or "assistant".
/// ImagePaths is non-null only for user turns that included image input.
/// </summary>
public record ConversationTurn(string Role, string Content, string[]? ImagePaths = null);

/// <summary>
/// Records one tool invocation. Arguments is the JSON the model produced for
/// the tool call. Result is the scriptblock's stringified return value when
/// Error is null, or empty when the tool threw — in that case Error contains
/// the message that was surfaced to the model as the tool result.
/// </summary>
public record ToolCallRecord(string Name, string Arguments, string Result, string? Error = null);
