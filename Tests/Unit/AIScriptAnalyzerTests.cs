using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class AIScriptAnalyzerTests
{
    // --- Read-only signals ---

    [Theory]
    [InlineData("Get-ChildItem C:\\foo")]
    [InlineData("Get-Process | Where-Object Name -eq 'svchost'")]
    [InlineData("Measure-Object -Sum")]
    [InlineData("Select-Object -First 10")]
    [InlineData("Test-Path C:\\foo")]
    [InlineData("Resolve-Path C:\\foo")]
    [InlineData("Format-Table | Out-Default")]
    [InlineData("(Get-ChildItem).Count")]
    [InlineData("ConvertTo-Json @{ a = 1 }")]
    public void Analyze_ReadOnlyCmdlets_MarkedReadOnly(string script)
    {
        var result = AIScriptAnalyzer.Analyze(script);
        Assert.True(result.IsReadOnly, $"Expected read-only, but got risks: {string.Join("; ", result.Risks)}");
        Assert.False(result.HasWhatIfIncompatibleOps);
    }

    [Fact]
    public void Analyze_PureVariableExpression_IsReadOnly()
    {
        var result = AIScriptAnalyzer.Analyze("$x = 5; $x + 3");
        Assert.True(result.IsReadOnly);
    }

    // --- State-modifying cmdlets caught ---

    [Theory]
    [InlineData("Remove-Item C:\\foo")]
    [InlineData("Set-Location C:\\")]
    [InlineData("New-Item -ItemType File -Path foo.txt")]
    [InlineData("Move-Item a b")]
    [InlineData("Copy-Item a b")]
    [InlineData("Stop-Process -Name notepad")]
    [InlineData("Start-Service spooler")]
    public void Analyze_StateModifyingCmdlets_FlaggedNotReadOnly(string script)
    {
        var result = AIScriptAnalyzer.Analyze(script);
        Assert.False(result.IsReadOnly);
        Assert.NotEmpty(result.Risks);
    }

    // --- WhatIf-incompatible operations flagged ---

    [Theory]
    [InlineData("Invoke-Expression 'whatever'")]
    [InlineData("iex 'whatever'")]
    public void Analyze_InvokeExpression_IsWhatIfIncompatible(string script)
    {
        var result = AIScriptAnalyzer.Analyze(script);
        Assert.False(result.IsReadOnly);
        Assert.True(result.HasWhatIfIncompatibleOps);
        Assert.Contains(result.Risks, r => r.Contains("Dynamic evaluation"));
    }

    [Theory]
    [InlineData("git push --force origin main")]
    [InlineData("kubectl delete pod foo")]
    [InlineData("aws s3 rm s3://bucket/key")]
    public void Analyze_NativeExecutables_AreFlaggedAsIncompatible(string script)
    {
        var result = AIScriptAnalyzer.Analyze(script);
        Assert.False(result.IsReadOnly);
        Assert.True(result.HasWhatIfIncompatibleOps);
        Assert.Contains(result.Risks, r => r.Contains("External executable"));
    }

    [Fact]
    public void Analyze_DotNetMethodInvoke_IsWhatIfIncompatible()
    {
        var result = AIScriptAnalyzer.Analyze("[System.IO.File]::Delete('C:\\foo.txt')");
        Assert.False(result.IsReadOnly);
        Assert.True(result.HasWhatIfIncompatibleOps);
        Assert.Contains(result.Risks, r => r.Contains(".NET method invocation"));
    }

    [Fact]
    public void Analyze_FileRedirection_IsWhatIfIncompatible()
    {
        var result = AIScriptAnalyzer.Analyze("Get-Process > C:\\out.txt");
        Assert.False(result.IsReadOnly);
        Assert.True(result.HasWhatIfIncompatibleOps);
        Assert.Contains(result.Risks, r => r.Contains("redirection"));
    }

    // --- Invoke-RestMethod / Invoke-WebRequest method-sensitive ---

    [Fact]
    public void Analyze_RestMethod_Get_IsReadOnly()
    {
        var result = AIScriptAnalyzer.Analyze("Invoke-RestMethod https://example.com/api");
        Assert.True(result.IsReadOnly);
    }

    [Fact]
    public void Analyze_RestMethod_Post_FlaggedAsIncompatible()
    {
        var result = AIScriptAnalyzer.Analyze("Invoke-RestMethod https://example.com/api -Method POST -Body '{}'");
        Assert.False(result.IsReadOnly);
        Assert.True(result.HasWhatIfIncompatibleOps);
        Assert.Contains(result.Risks, r => r.Contains("HTTP write request"));
    }

    [Fact]
    public void Analyze_WebRequest_Delete_FlaggedAsIncompatible()
    {
        var result = AIScriptAnalyzer.Analyze("Invoke-WebRequest https://example.com/x -Method Delete");
        Assert.False(result.IsReadOnly);
        Assert.True(result.HasWhatIfIncompatibleOps);
    }

    // --- File write cmdlets that bypass -WhatIf via host output ---

    [Theory]
    [InlineData("Out-File C:\\foo.txt")]
    [InlineData("Set-Content C:\\foo.txt 'x'")]
    [InlineData("Add-Content C:\\foo.txt 'y'")]
    [InlineData("Tee-Object C:\\foo.txt")]
    public void Analyze_AlwaysReviewCmdlets_FlaggedNotReadOnly(string script)
    {
        var result = AIScriptAnalyzer.Analyze(script);
        Assert.False(result.IsReadOnly);
    }

    // --- Parse errors ---

    [Fact]
    public void Analyze_ParseError_TreatedAsNotReadOnly()
    {
        var result = AIScriptAnalyzer.Analyze("Get-ChildItem -Path 'unterminated");
        Assert.False(result.IsReadOnly);
        Assert.Contains(result.Risks, r => r.Contains("Parse error"));
    }

    // --- Combinations ---

    [Fact]
    public void Analyze_ReadOnlyPipelineWithMultipleStages_StillReadOnly()
    {
        var result = AIScriptAnalyzer.Analyze(
            "Get-ChildItem C:\\logs -Recurse | Where-Object Length -gt 1MB | Sort-Object LastWriteTime | Select-Object -First 10");
        Assert.True(result.IsReadOnly);
    }

    [Fact]
    public void Analyze_MixedReadAndWrite_FlaggedNotReadOnly()
    {
        var result = AIScriptAnalyzer.Analyze("Get-ChildItem C:\\logs | Remove-Item");
        Assert.False(result.IsReadOnly);
    }

    [Fact]
    public void Analyze_AddType_AlwaysFlagged()
    {
        // Compile-arbitrary-C# is always dangerous.
        var result = AIScriptAnalyzer.Analyze("Add-Type -TypeDefinition 'public class X {}'");
        Assert.False(result.IsReadOnly);
    }
}
