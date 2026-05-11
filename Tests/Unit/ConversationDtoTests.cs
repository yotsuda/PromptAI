using System.Text.Json;
using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class ConversationDtoTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new AIResponse(
            text: "answer",
            model: "claude-sonnet-4-20250514",
            provider: "Anthropic",
            inputTokens: 12,
            outputTokens: 34,
            estimatedCostUSD: 0.000567m,
            duration: TimeSpan.FromMilliseconds(1234),
            turns: new[]
            {
                new ConversationTurn("user", "question"),
                new ConversationTurn("assistant", "answer"),
            },
            systemPrompt: "be terse");

        var dto = ConversationDto.From(original);
        var json = JsonSerializer.Serialize(dto);
        var roundTripDto = JsonSerializer.Deserialize<ConversationDto>(json);
        Assert.NotNull(roundTripDto);

        var restored = roundTripDto!.ToResponse();
        Assert.Equal("answer", restored.Text);
        Assert.Equal("claude-sonnet-4-20250514", restored.Model);
        Assert.Equal("Anthropic", restored.Provider);
        Assert.Equal(12, restored.InputTokens);
        Assert.Equal(34, restored.OutputTokens);
        Assert.Equal(0.000567m, restored.EstimatedCostUSD);
        Assert.Equal(1234, restored.Duration.TotalMilliseconds);
        Assert.Equal("be terse", restored.SystemPrompt);
        Assert.Equal(2, restored.Turns.Count);
        Assert.Equal("user",      restored.Turns[0].Role);
        Assert.Equal("question",  restored.Turns[0].Content);
        Assert.Equal("assistant", restored.Turns[1].Role);
        Assert.Equal("answer",    restored.Turns[1].Content);
    }

    [Fact]
    public void RoundTrip_NullableFieldsHandled()
    {
        var original = new AIResponse(text: "x", model: "m", provider: "p");  // all optionals null/0
        var json = JsonSerializer.Serialize(ConversationDto.From(original));
        var restored = JsonSerializer.Deserialize<ConversationDto>(json)!.ToResponse();

        Assert.Equal("x", restored.Text);
        Assert.Null(restored.SystemPrompt);
        Assert.Null(restored.EstimatedCostUSD);
        Assert.Empty(restored.Turns);
    }
}
