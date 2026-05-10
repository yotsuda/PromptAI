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

    public AIResponse(
        string text,
        string model,
        string provider,
        int inputTokens = 0,
        int outputTokens = 0,
        decimal? estimatedCostUSD = null,
        TimeSpan duration = default,
        IReadOnlyList<ConversationTurn>? turns = null)
    {
        Text = text;
        Model = model;
        Provider = provider;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        EstimatedCostUSD = estimatedCostUSD;
        Duration = duration;
        Turns = turns ?? Array.Empty<ConversationTurn>();
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
