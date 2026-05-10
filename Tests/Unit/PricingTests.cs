using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class PricingTests
{
    [Fact]
    public void Compute_ReturnsNullForUnknownModel()
    {
        Assert.Null(Pricing.Compute("totally-not-a-real-model", 100, 100));
    }

    [Fact]
    public void Compute_ReturnsNullForEmptyModel()
    {
        Assert.Null(Pricing.Compute("", 100, 100));
        Assert.Null(Pricing.Compute(null!, 100, 100));
    }

    [Fact]
    public void Compute_ExactMatchOnGptModel()
    {
        // gpt-4o = $2.50/$10.00 per 1M
        var cost = Pricing.Compute("gpt-4o", 1_000_000, 1_000_000);
        Assert.Equal(12.50m, cost);
    }

    [Fact]
    public void Compute_PrefixMatchOnClaudeModel()
    {
        // claude-sonnet-4- prefix matches → $3.00/$15.00 per 1M
        var cost = Pricing.Compute("claude-sonnet-4-20250514", 1_000_000, 1_000_000);
        Assert.Equal(18.00m, cost);
    }

    [Fact]
    public void Compute_GptOrderingMattersForPrefix()
    {
        // Tests that gpt-4o-mini (specific) wins over gpt-4o (generic prefix)
        // because the specific entry is listed first in s_table.
        var miniCost = Pricing.Compute("gpt-4o-mini", 1_000_000, 1_000_000);
        var bigCost  = Pricing.Compute("gpt-4o",      1_000_000, 1_000_000);
        Assert.True(miniCost < bigCost, $"expected gpt-4o-mini ({miniCost}) cheaper than gpt-4o ({bigCost})");
    }

    [Fact]
    public void Compute_ZeroTokensReturnsZero()
    {
        var cost = Pricing.Compute("gpt-4o", 0, 0);
        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Compute_SubMillionTokensProducesFractionalCost()
    {
        // 1000 in, 1000 out on gpt-4o = (1000 * 2.50 + 1000 * 10.00) / 1M = 0.0125
        var cost = Pricing.Compute("gpt-4o", 1000, 1000);
        Assert.Equal(0.0125m, cost);
    }

    [Fact]
    public void Compute_CaseInsensitiveModelMatching()
    {
        var lower = Pricing.Compute("gpt-4o",  1000, 1000);
        var upper = Pricing.Compute("GPT-4O",  1000, 1000);
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void Compute_DeepSeekV4Flash()
    {
        // deepseek-v4-flash = $0.14/$0.28 per 1M
        var cost = Pricing.Compute("deepseek-v4-flash", 1_000_000, 1_000_000);
        Assert.Equal(0.42m, cost);
    }
}
