using System.Text.Json;
using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class OpenAICompatTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ParseDelta_ExtractsContentFromTypicalChunk()
    {
        var json = """{"choices":[{"index":0,"delta":{"content":"Hello"}}]}""";
        Assert.Equal("Hello", OpenAICompat.ParseDelta(Parse(json)));
    }

    [Fact]
    public void ParseDelta_ReturnsNullForEmptyChoicesArray()
    {
        // Final usage chunk has empty choices.
        var json = """{"choices":[],"usage":{"prompt_tokens":4,"completion_tokens":2}}""";
        Assert.Null(OpenAICompat.ParseDelta(Parse(json)));
    }

    [Fact]
    public void ParseDelta_ReturnsNullForRoleOnlyDelta()
    {
        // First chunk often has only role, no content.
        var json = """{"choices":[{"index":0,"delta":{"role":"assistant"}}]}""";
        Assert.Null(OpenAICompat.ParseDelta(Parse(json)));
    }

    [Fact]
    public void ParseDelta_ReturnsNullForEmptyDelta()
    {
        // Final delta on stop is sometimes empty.
        var json = """{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""";
        Assert.Null(OpenAICompat.ParseDelta(Parse(json)));
    }

    [Fact]
    public void ParseDelta_ReturnsNullForJsonNullRoot()
    {
        Assert.Null(OpenAICompat.ParseDelta(Parse("null")));
    }

    [Fact]
    public void ParseDelta_ReturnsNullForArrayRoot()
    {
        Assert.Null(OpenAICompat.ParseDelta(Parse("[]")));
    }

    [Fact]
    public void ParseDelta_ReturnsNullWhenContentIsJsonNull()
    {
        // Some providers send content: null on the closing chunk.
        var json = """{"choices":[{"index":0,"delta":{"content":null}}]}""";
        Assert.Null(OpenAICompat.ParseDelta(Parse(json)));
    }

    [Fact]
    public void ParseUsage_ExtractsTokensFromFinalChunk()
    {
        var json = """{"choices":[],"usage":{"prompt_tokens":42,"completion_tokens":7,"total_tokens":49}}""";
        var (input, output) = OpenAICompat.ParseUsage(Parse(json));
        Assert.Equal(42, input);
        Assert.Equal(7, output);
    }

    [Fact]
    public void ParseUsage_ReturnsNullWhenUsageIsJsonNull()
    {
        // The bug fix that motivated the parser hardening: previously this threw
        // InvalidOperationException because TryGetProperty was called on a Null element.
        var json = """{"choices":[{"delta":{"content":"x"}}],"usage":null}""";
        var (input, output) = OpenAICompat.ParseUsage(Parse(json));
        Assert.Null(input);
        Assert.Null(output);
    }

    [Fact]
    public void ParseUsage_ReturnsNullWhenUsageMissing()
    {
        var json = """{"choices":[{"delta":{"content":"x"}}]}""";
        var (input, output) = OpenAICompat.ParseUsage(Parse(json));
        Assert.Null(input);
        Assert.Null(output);
    }

    [Fact]
    public void ParseUsage_HandlesPartialUsage()
    {
        // Hypothetical: only one of the fields is present.
        var json = """{"usage":{"prompt_tokens":10}}""";
        var (input, output) = OpenAICompat.ParseUsage(Parse(json));
        Assert.Equal(10, input);
        Assert.Null(output);
    }

    [Fact]
    public void ParseUsage_ReturnsNullForJsonNullRoot()
    {
        var (input, output) = OpenAICompat.ParseUsage(Parse("null"));
        Assert.Null(input);
        Assert.Null(output);
    }
}
