using System.Collections;
using System.Text.Json;
using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class JsonHelpersTests
{
    [Fact]
    public void SerializeHashtable_FlatPrimitives()
    {
        var h = new Hashtable
        {
            ["name"]  = "x",
            ["count"] = 3,
            ["flag"]  = true,
        };
        var json = JsonHelpers.SerializeHashtable(h);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("x", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(3,   doc.RootElement.GetProperty("count").GetInt32());
        Assert.True(      doc.RootElement.GetProperty("flag").GetBoolean());
    }

    [Fact]
    public void SerializeHashtable_NestedObject()
    {
        var h = new Hashtable
        {
            ["type"]       = "object",
            ["properties"] = new Hashtable
            {
                ["name"] = new Hashtable { ["type"] = "string" },
            },
        };
        var json = JsonHelpers.SerializeHashtable(h);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("string",
            doc.RootElement
               .GetProperty("properties")
               .GetProperty("name")
               .GetProperty("type")
               .GetString());
    }

    [Fact]
    public void SerializeHashtable_ArrayValues()
    {
        var h = new Hashtable
        {
            ["required"] = new[] { "a", "b" },
            ["enum"]     = new object[] { 1, 2, 3 },
        };
        var json = JsonHelpers.SerializeHashtable(h);
        using var doc = JsonDocument.Parse(json);
        var req = doc.RootElement.GetProperty("required");
        Assert.Equal(2, req.GetArrayLength());
        Assert.Equal("a", req[0].GetString());
        Assert.Equal("b", req[1].GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("enum")[2].GetInt32());
    }

    [Fact]
    public void SerializeHashtable_NullValue()
    {
        var h = new Hashtable { ["x"] = null };
        var json = JsonHelpers.SerializeHashtable(h);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("x").ValueKind);
    }
}
