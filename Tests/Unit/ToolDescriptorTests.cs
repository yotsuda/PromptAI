using System.Collections;
using System.Management.Automation;
using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class ToolDescriptorTests
{
    private static Hashtable ValidTool() => new()
    {
        ["Name"]        = "calc",
        ["Description"] = "do math",
        ["Parameters"]  = new Hashtable { ["type"] = "object" },
        ["Run"]         = ScriptBlock.Create("'42'"),
    };

    [Fact]
    public void ParseTools_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(AIStreamingCmdletBase.ParseTools(null));
        Assert.Null(AIStreamingCmdletBase.ParseTools(System.Array.Empty<Hashtable>()));
    }

    [Fact]
    public void ParseTools_ValidHashtable_ProducesDescriptor()
    {
        var result = AIStreamingCmdletBase.ParseTools(new[] { ValidTool() });
        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal("calc",    result[0].Name);
        Assert.Equal("do math", result[0].Description);
    }

    [Fact]
    public void ParseTools_MissingName_Throws()
    {
        var t = ValidTool();
        t.Remove("Name");
        var ex = Assert.Throws<PSArgumentException>(() => AIStreamingCmdletBase.ParseTools(new[] { t }));
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void ParseTools_MissingDescription_Throws()
    {
        var t = ValidTool();
        t.Remove("Description");
        var ex = Assert.Throws<PSArgumentException>(() => AIStreamingCmdletBase.ParseTools(new[] { t }));
        Assert.Contains("Description", ex.Message);
    }

    [Fact]
    public void ParseTools_MissingParameters_Throws()
    {
        var t = ValidTool();
        t.Remove("Parameters");
        var ex = Assert.Throws<PSArgumentException>(() => AIStreamingCmdletBase.ParseTools(new[] { t }));
        Assert.Contains("Parameters", ex.Message);
    }

    [Fact]
    public void ParseTools_MissingRun_Throws()
    {
        var t = ValidTool();
        t.Remove("Run");
        var ex = Assert.Throws<PSArgumentException>(() => AIStreamingCmdletBase.ParseTools(new[] { t }));
        Assert.Contains("Run", ex.Message);
    }

    [Fact]
    public void ParseTools_RunMustBeScriptBlock()
    {
        var t = ValidTool();
        t["Run"] = "not a scriptblock";
        var ex = Assert.Throws<PSArgumentException>(() => AIStreamingCmdletBase.ParseTools(new[] { t }));
        Assert.Contains("ScriptBlock", ex.Message);
    }

    [Fact]
    public void ParseTools_IndexInErrorMessage_PointsToOffender()
    {
        var good = ValidTool();
        var bad  = ValidTool();
        bad.Remove("Name");
        var ex = Assert.Throws<PSArgumentException>(() => AIStreamingCmdletBase.ParseTools(new[] { good, bad }));
        Assert.Contains("[1]", ex.Message);
    }

    // RunTool scriptblock execution requires a PowerShell Runspace that xUnit
    // does not provide by default. Coverage for execution + error capture is
    // in the manual smoke tests (scratch/smoke-tool-*.ps1).
}
