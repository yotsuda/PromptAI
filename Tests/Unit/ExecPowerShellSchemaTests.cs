using System.Collections;
using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

/// <summary>
/// The synthetic exec_powershell tool's JSON schema is what the LLM actually
/// sees in the tools list. If its shape regresses (missing required fields,
/// wrong types, missing descriptions), the model will fail to call it
/// correctly. These tests pin the shape down.
/// </summary>
public class ExecPowerShellSchemaTests
{
    [Fact]
    public void Schema_IsObjectType()
    {
        var schema = AIStreamingCmdletBase.BuildExecPowerShellSchema();
        Assert.Equal("object", schema["type"]);
    }

    [Fact]
    public void Schema_HasScriptProperty_AsString()
    {
        var schema = AIStreamingCmdletBase.BuildExecPowerShellSchema();
        var properties = Assert.IsType<Hashtable>(schema["properties"]);
        var script = Assert.IsType<Hashtable>(properties["script"]);
        Assert.Equal("string", script["type"]);
        Assert.False(string.IsNullOrWhiteSpace(script["description"] as string),
            "script must carry a description so the LLM knows what to put there");
    }

    [Fact]
    public void Schema_HasPurposeProperty_AsString()
    {
        var schema = AIStreamingCmdletBase.BuildExecPowerShellSchema();
        var properties = Assert.IsType<Hashtable>(schema["properties"]);
        var purpose = Assert.IsType<Hashtable>(properties["purpose"]);
        Assert.Equal("string", purpose["type"]);
        Assert.False(string.IsNullOrWhiteSpace(purpose["description"] as string));
    }

    [Fact]
    public void Schema_BothFieldsMarkedRequired()
    {
        var schema = AIStreamingCmdletBase.BuildExecPowerShellSchema();
        var required = schema["required"] as string[];
        Assert.NotNull(required);
        Assert.Contains("script",  required);
        Assert.Contains("purpose", required);
    }

    [Fact]
    public void Schema_RoundTripsThroughJsonHelpers()
    {
        // Every provider serializes this schema into the request body via
        // JsonHelpers.WriteHashtable — confirm it produces valid JSON.
        var schema = AIStreamingCmdletBase.BuildExecPowerShellSchema();
        var json = JsonHelpers.SerializeHashtable(schema);
        Assert.Contains("\"type\":\"object\"", json);
        Assert.Contains("\"script\"",  json);
        Assert.Contains("\"purpose\"", json);
        Assert.Contains("\"required\":[", json);
    }

    [Fact]
    public void ExecPowerShellName_IsStableConstant()
    {
        // Providers serialize this name into request bodies and use it as the
        // dispatch key for routing tool_use → AIScriptExecutor. Changing it
        // is a wire-level breaking change.
        Assert.Equal("exec_powershell", AIStreamingCmdletBase.ExecPowerShellName);
    }

    [Fact]
    public void ExecPowerShellDescription_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AIStreamingCmdletBase.ExecPowerShellDescription));
        // The description guides the model on when to reach for the tool —
        // a smoke check that it mentions key behaviors.
        var d = AIStreamingCmdletBase.ExecPowerShellDescription;
        Assert.Contains("PowerShell", d);
    }

    // Argument-parsing failure paths exit BEFORE AIScriptExecutor needs a
    // real PSHost / Runspace, so we can exercise them with a stub context.
    // These ensure misbehaved tool calls from the model don't crash the loop —
    // they get surfaced as tool errors so the model can self-correct.
    private static AIScriptContext StubContext() => new("Off", null!, null!);

    [Fact]
    public void RunExecPowerShell_EmptyArgs_ReturnsMissingScriptError()
    {
        var (result, error) = AIStreamingCmdletBase.RunExecPowerShell("{}", StubContext());
        Assert.Equal(string.Empty, result);
        Assert.NotNull(error);
        Assert.Contains("script", error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunExecPowerShell_EmptyArgsJsonString_ReturnsMissingScriptError()
    {
        // Some models emit "" instead of "{}" when they think no args are needed.
        var (result, error) = AIStreamingCmdletBase.RunExecPowerShell("", StubContext());
        Assert.Equal(string.Empty, result);
        Assert.NotNull(error);
        Assert.Contains("script", error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunExecPowerShell_PurposeOnlyNoScript_ReturnsMissingScriptError()
    {
        var (result, error) = AIStreamingCmdletBase.RunExecPowerShell(
            """{"purpose":"do something"}""", StubContext());
        Assert.Equal(string.Empty, result);
        Assert.NotNull(error);
    }

    [Fact]
    public void RunExecPowerShell_MalformedJson_ReturnsError()
    {
        var (result, error) = AIStreamingCmdletBase.RunExecPowerShell(
            "{not valid json", StubContext());
        Assert.Equal(string.Empty, result);
        Assert.NotNull(error);
    }
}
