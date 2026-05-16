using System.Collections;
using System.Text.Json;
using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class JsonHelpersTests
{
    [Fact]
    public void JsonObjectToHashtable_FlatObject()
    {
        var ht = JsonHelpers.JsonObjectToHashtable("""{"city":"Tokyo","count":5,"hot":true}""");
        Assert.Equal("Tokyo", ht["city"]);
        Assert.Equal(5L, ht["count"]);     // Int64 by default for non-decimal numbers
        Assert.Equal(true, ht["hot"]);
    }

    [Fact]
    public void JsonObjectToHashtable_NestedObjectBecomesNestedHashtable()
    {
        var ht = JsonHelpers.JsonObjectToHashtable("""{"a":{"b":{"c":"deep"}}}""");
        var a = Assert.IsType<Hashtable>(ht["a"]);
        var b = Assert.IsType<Hashtable>(a["b"]);
        Assert.Equal("deep", b["c"]);
    }

    [Fact]
    public void JsonObjectToHashtable_ArrayBecomesObjectArray()
    {
        var ht = JsonHelpers.JsonObjectToHashtable("""{"tags":["a","b","c"]}""");
        var arr = Assert.IsType<object?[]>(ht["tags"]);
        Assert.Equal(new object?[] { "a", "b", "c" }, arr);
    }

    [Fact]
    public void JsonObjectToHashtable_EmptyOrWhitespaceTreatedAsEmptyObject()
    {
        // Models occasionally emit no arguments for no-arg tools — we should
        // not throw in that case.
        Assert.Empty(JsonHelpers.JsonObjectToHashtable(""));
        Assert.Empty(JsonHelpers.JsonObjectToHashtable("   "));
        Assert.Empty(JsonHelpers.JsonObjectToHashtable("{}"));
    }

    [Fact]
    public void JsonObjectToHashtable_DoubleBecomesDouble()
    {
        var ht = JsonHelpers.JsonObjectToHashtable("""{"temp":22.5}""");
        Assert.Equal(22.5, ht["temp"]);
    }

    [Fact]
    public void JsonObjectToHashtable_NullValueRetained()
    {
        var ht = JsonHelpers.JsonObjectToHashtable("""{"opt":null}""");
        Assert.True(ht.ContainsKey("opt"));
        Assert.Null(ht["opt"]);
    }

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
