namespace PromptAI.Cmdlets;

/// <summary>
/// Best-effort static pricing lookup. Prices are USD per 1M tokens, current as of
/// the v0.1.3 release. Pricing changes — treat output as approximate. Unknown models
/// (and Groq's free tier) return null so AIResponse.EstimatedCostUSD is null rather
/// than misleadingly zero.
/// </summary>
internal static class Pricing
{
    /// <summary>(inputPer1M, outputPer1M) in USD. Match by exact ID first, then prefix.</summary>
    private static readonly (string idOrPrefix, decimal inPer1M, decimal outPer1M)[] s_table =
    [
        // Anthropic Claude — https://www.anthropic.com/pricing
        ("claude-opus-4-",     15.00m, 75.00m),
        ("claude-sonnet-4-",    3.00m, 15.00m),
        ("claude-haiku-4-",     1.00m,  5.00m),

        // OpenAI — https://openai.com/api/pricing
        ("gpt-4o-mini",         0.15m,  0.60m),
        ("gpt-4o",              2.50m, 10.00m),
        ("gpt-4.1-mini",        0.40m,  1.60m),
        ("gpt-4.1",             2.00m,  8.00m),
        ("o4-mini",             1.10m,  4.40m),
        ("o3-mini",             1.10m,  4.40m),
        ("o3",                  2.00m,  8.00m),

        // Google Gemini — https://ai.google.dev/pricing
        ("gemini-2.5-flash",    0.30m,  2.50m),
        ("gemini-2.5-pro",      1.25m, 10.00m),
        ("gemini-2.0-flash",    0.10m,  0.40m),

        // DeepSeek — https://api-docs.deepseek.com/quick_start/pricing
        ("deepseek-v4-flash",   0.14m,  0.28m),
        ("deepseek-v4-pro",     0.55m,  2.19m),
        ("deepseek-chat",       0.27m,  1.10m),
        ("deepseek-reasoner",   0.55m,  2.19m),

        // Meta Llama API — https://llama.developer.meta.com/docs
        // (preview pricing; update when GA)
        ("Llama-4-Maverick",    0.50m,  1.50m),
        ("Llama-4-Scout",       0.20m,  0.60m),
        ("Llama-3.3-70B",       0.50m,  1.50m),

        // Together AI — https://www.together.ai/pricing
        ("meta-llama/Llama-3.3-70B-Instruct-Turbo",         0.88m,  0.88m),
        ("meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo",     0.18m,  0.18m),

        // Groq — free tier; paid pricing exists but most usage is free
        // Returning null (no entry) is correct here.
    ];

    /// <summary>
    /// Returns USD cost for the given input/output token usage on the named model,
    /// or null if the model isn't in the pricing table.
    /// </summary>
    public static decimal? Compute(string model, int inputTokens, int outputTokens)
    {
        if (string.IsNullOrEmpty(model)) return null;

        foreach (var (idOrPrefix, inPer1M, outPer1M) in s_table)
        {
            if (model.Equals(idOrPrefix, StringComparison.OrdinalIgnoreCase) ||
                model.StartsWith(idOrPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return (inputTokens * inPer1M / 1_000_000m) + (outputTokens * outPer1M / 1_000_000m);
            }
        }

        return null;
    }
}
