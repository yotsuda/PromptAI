using System.Text.Json;
using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class GeminiParserTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ParseDelta_ExtractsTextFromCandidatesContentParts()
    {
        var json = """{"candidates":[{"content":{"parts":[{"text":"Hello"}],"role":"model"}}]}""";
        Assert.Equal("Hello", InvokeGeminiCmdlet.ParseDelta(Parse(json)));
    }

    [Fact]
    public void ParseDelta_ReturnsNullForEmptyCandidates()
    {
        Assert.Null(InvokeGeminiCmdlet.ParseDelta(Parse("""{"candidates":[]}""")));
    }

    [Fact]
    public void ParseDelta_ReturnsNullWhenContentMissing()
    {
        // Some Gemini chunks signal finish without content (e.g., safety stop).
        var json = """{"candidates":[{"finishReason":"STOP"}]}""";
        Assert.Null(InvokeGeminiCmdlet.ParseDelta(Parse(json)));
    }

    [Fact]
    public void ParseDelta_ReturnsNullForJsonNullRoot()
    {
        Assert.Null(InvokeGeminiCmdlet.ParseDelta(Parse("null")));
    }

    [Fact]
    public void ParseDelta_ReturnsNullWhenPartsHasNoText()
    {
        // Hypothetical: parts has inline_data but no text (not a content delta).
        var json = """{"candidates":[{"content":{"parts":[{"inline_data":{"mime_type":"image/png","data":"xx"}}]}}]}""";
        Assert.Null(InvokeGeminiCmdlet.ParseDelta(Parse(json)));
    }

    [Fact]
    public void ParseUsage_ExtractsCumulativeTokenCounts()
    {
        var json = """{"usageMetadata":{"promptTokenCount":17,"candidatesTokenCount":35,"totalTokenCount":52}}""";
        var (input, output) = InvokeGeminiCmdlet.ParseUsage(Parse(json));
        Assert.Equal(17, input);
        Assert.Equal(35, output);
    }

    [Fact]
    public void ParseUsage_ReturnsNullWhenUsageMetadataMissing()
    {
        var json = """{"candidates":[{"content":{"parts":[{"text":"x"}]}}]}""";
        var (input, output) = InvokeGeminiCmdlet.ParseUsage(Parse(json));
        Assert.Null(input);
        Assert.Null(output);
    }

    [Fact]
    public void ParseUsage_ReturnsNullForJsonNullRoot()
    {
        var (input, output) = InvokeGeminiCmdlet.ParseUsage(Parse("null"));
        Assert.Null(input);
        Assert.Null(output);
    }
}
