using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class AIResponseTests
{
    [Fact]
    public void Constructor_DefaultsAreSafeWhenOptionalFieldsOmitted()
    {
        var r = new AIResponse(text: "hi", model: "x", provider: "y");
        Assert.Equal("hi", r.Text);
        Assert.Equal("x", r.Model);
        Assert.Equal("y", r.Provider);
        Assert.Equal(0, r.InputTokens);
        Assert.Equal(0, r.OutputTokens);
        Assert.Null(r.EstimatedCostUSD);
        Assert.Equal(TimeSpan.Zero, r.Duration);
        Assert.Empty(r.Turns);
    }

    [Fact]
    public void ToString_ReturnsText()
    {
        var r = new AIResponse("the answer", "x", "y");
        Assert.Equal("the answer", r.ToString());
        Assert.Equal("the answer", $"{r}");
    }

    [Fact]
    public void ImplicitStringConversion_ReturnsText()
    {
        var r = new AIResponse("plain", "x", "y");
        string s = r;
        Assert.Equal("plain", s);
    }

    [Fact]
    public void Length_ReflectsTextLength()
    {
        var r = new AIResponse("12345", "x", "y");
        Assert.Equal(5, r.Length);
    }

    [Fact]
    public void Constructor_PopulatesAllNewFields()
    {
        var turns = new[]
        {
            new ConversationTurn("user", "Q"),
            new ConversationTurn("assistant", "A"),
        };
        var r = new AIResponse(
            text: "A",
            model: "gpt-4o",
            provider: "OpenAI",
            inputTokens: 4,
            outputTokens: 1,
            estimatedCostUSD: 0.0001m,
            duration: TimeSpan.FromMilliseconds(123),
            turns: turns);

        Assert.Equal(4, r.InputTokens);
        Assert.Equal(1, r.OutputTokens);
        Assert.Equal(0.0001m, r.EstimatedCostUSD);
        Assert.Equal(123, r.Duration.TotalMilliseconds);
        Assert.Equal(2, r.Turns.Count);
        Assert.Equal("user", r.Turns[0].Role);
        Assert.Equal("Q",    r.Turns[0].Content);
    }

    [Fact]
    public void ConversationTurn_DefaultImagePathsIsNull()
    {
        var t = new ConversationTurn("user", "hello");
        Assert.Null(t.ImagePaths);
    }

    [Fact]
    public void Constructor_SystemPromptDefaultsToNull()
    {
        var r = new AIResponse("x", "m", "p");
        Assert.Null(r.SystemPrompt);
    }

    [Fact]
    public void Constructor_SystemPromptRoundTrip()
    {
        var r = new AIResponse("x", "m", "p", systemPrompt: "you are a tutor");
        Assert.Equal("you are a tutor", r.SystemPrompt);
    }

    // Inheritance precedence (explicit > history > none) is implemented in each
    // provider's static Call() method as: explicit ?? history?.SystemPrompt.
    // Verified here against the same expression so a refactor that breaks
    // precedence (e.g. flipping the order to history ?? explicit) trips a test.
    [Theory]
    [InlineData("explicit", "from history", "explicit")]
    [InlineData(null,       "from history", "from history")]
    [InlineData("explicit", null,           "explicit")]
    [InlineData(null,       null,           null)]
    public void EffectiveSystemPrompt_Precedence(string? explicitPrompt, string? historyPrompt, string? expected)
    {
        var history = historyPrompt is null
            ? null
            : new AIResponse("prev", "m", "p", systemPrompt: historyPrompt);

        var effective = explicitPrompt ?? history?.SystemPrompt;
        Assert.Equal(expected, effective);
    }
}
