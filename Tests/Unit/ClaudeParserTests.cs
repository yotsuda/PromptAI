using System.Text.Json;
using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class ClaudeParserTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ParseDelta_ExtractsTextFromContentBlockDelta()
    {
        var json = """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}""";
        Assert.Equal("Hello", InvokeClaudeCmdlet.ParseDelta(Parse(json)));
    }

    [Fact]
    public void ParseDelta_ReturnsNullForOtherEventTypes()
    {
        foreach (var type in new[] { "message_start", "message_delta", "message_stop", "content_block_start", "content_block_stop", "ping" })
        {
            var json = $$"""{"type":"{{type}}"}""";
            Assert.Null(InvokeClaudeCmdlet.ParseDelta(Parse(json)));
        }
    }

    [Fact]
    public void ParseDelta_ReturnsNullForMissingType()
    {
        var json = """{"foo":"bar"}""";
        Assert.Null(InvokeClaudeCmdlet.ParseDelta(Parse(json)));
    }

    [Fact]
    public void ParseDelta_ReturnsNullForJsonNullRoot()
    {
        Assert.Null(InvokeClaudeCmdlet.ParseDelta(Parse("null")));
    }

    [Fact]
    public void ParseUsage_ExtractsInputTokensFromMessageStart()
    {
        var json = """{"type":"message_start","message":{"id":"x","usage":{"input_tokens":42,"output_tokens":1}}}""";
        var (input, output) = InvokeClaudeCmdlet.ParseUsage(Parse(json));
        Assert.Equal(42, input);
        Assert.Equal(1, output);
    }

    [Fact]
    public void ParseUsage_ExtractsCumulativeOutputFromMessageDelta()
    {
        var json = """{"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":150}}""";
        var (input, output) = InvokeClaudeCmdlet.ParseUsage(Parse(json));
        Assert.Null(input);
        Assert.Equal(150, output);
    }

    [Fact]
    public void ParseUsage_ReturnsNullsForUnrelatedEvent()
    {
        var json = """{"type":"content_block_delta","delta":{"type":"text_delta","text":"x"}}""";
        var (input, output) = InvokeClaudeCmdlet.ParseUsage(Parse(json));
        Assert.Null(input);
        Assert.Null(output);
    }

    [Fact]
    public void ParseUsage_HandlesMissingMessageOrUsageGracefully()
    {
        // Defensive: type=message_start but no message field
        var json = """{"type":"message_start"}""";
        var (input, output) = InvokeClaudeCmdlet.ParseUsage(Parse(json));
        Assert.Null(input);
        Assert.Null(output);
    }
}
