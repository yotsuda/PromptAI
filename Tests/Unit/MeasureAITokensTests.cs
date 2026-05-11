using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class MeasureAITokensTests
{
    [Theory]
    [InlineData(0x3042, true)]   // Hiragana あ
    [InlineData(0x30A4, true)]   // Katakana イ
    [InlineData(0x4E00, true)]   // CJK unified 一
    [InlineData(0x9FFF, true)]   // top of CJK unified
    [InlineData(0xAC00, true)]   // Hangul 가
    [InlineData(0x3400, true)]   // CJK Ext A
    [InlineData(0x0041, false)]  // Latin A
    [InlineData(0x00E9, false)]  // Latin é
    [InlineData(0x1F31F, false)] // 🌟 emoji
    public void IsCjk_Boundaries(int codepoint, bool expected)
    {
        Assert.Equal(expected, MeasureAITokensCmdlet.IsCjk(codepoint));
    }
}
