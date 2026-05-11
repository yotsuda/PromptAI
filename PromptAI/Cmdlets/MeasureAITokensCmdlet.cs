using System.Management.Automation;

namespace PromptAI.Cmdlets;

/// <summary>
/// Estimates the number of tokens in a piece of text using a fast local heuristic.
/// No network calls. Useful as a pre-flight check ("will this prompt fit in the
/// model's context window?") rather than as an exact cost calculator.
///
/// Heuristic: ASCII chars / 4 (BPE-on-English baseline), CJK chars / 2 (denser
/// per character because each kanji is 1-2 tokens in most tokenizers), other
/// (accented Latin, emoji, symbols) chars / 3 as a middle bucket. Result is
/// rounded up. Real tokenizers will disagree by 10-50%; do not rely on this for
/// billing.
/// </summary>
[Cmdlet(VerbsDiagnostic.Measure, "AITokens")]
[OutputType(typeof(PSObject))]
public class MeasureAITokensCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string Text { get; set; } = null!;

    /// <summary>When set, emits a per-character-class breakdown alongside the total.</summary>
    [Parameter]
    public SwitchParameter Detailed { get; set; }

    private readonly List<string> _lines = [];

    protected override void ProcessRecord() => _lines.Add(Text);

    protected override void EndProcessing()
    {
        var combined = string.Join("\n", _lines);
        int ascii = 0, cjk = 0, other = 0;

        foreach (var rune in combined.EnumerateRunes())
        {
            int v = rune.Value;
            if (v <= 0x7F)
            {
                ascii++;
            }
            else if (IsCjk(v))
            {
                cjk++;
            }
            else
            {
                other++;
            }
        }

        var raw = ascii / 4.0 + cjk / 2.0 + other / 3.0;
        var estimated = (int)Math.Ceiling(raw);

        var obj = new PSObject();
        obj.TypeNames.Insert(0, "PromptAI.Cmdlets.TokenEstimate");
        obj.Properties.Add(new PSNoteProperty("EstimatedTokens", estimated));
        obj.Properties.Add(new PSNoteProperty("CharCount",       ascii + cjk + other));
        obj.Properties.Add(new PSNoteProperty("Method",          "heuristic"));
        if (Detailed.IsPresent)
        {
            obj.Properties.Add(new PSNoteProperty("AsciiChars",  ascii));
            obj.Properties.Add(new PSNoteProperty("CjkChars",    cjk));
            obj.Properties.Add(new PSNoteProperty("OtherChars",  other));
        }
        WriteObject(obj);
    }

    /// <summary>
    /// Lightweight CJK detection: Hiragana / Katakana / CJK Unified Ideographs
    /// (incl. Extension A) / Hangul Syllables / CJK Symbols & Punctuation.
    /// Covers the Japanese / Chinese / Korean text that real tokenizers split
    /// roughly twice as densely as ASCII.
    /// </summary>
    internal static bool IsCjk(int codepoint)
        =>  (codepoint >= 0x3000 && codepoint <= 0x9FFF)   // CJK symbols + Hiragana + Katakana + CJK Unified
         || (codepoint >= 0xAC00 && codepoint <= 0xD7AF)   // Hangul Syllables
         || (codepoint >= 0x3400 && codepoint <= 0x4DBF);  // CJK Extension A
}
