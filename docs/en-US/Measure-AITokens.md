---
document type: cmdlet
external help file: PromptAI.dll-Help.xml
HelpUri: https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Measure-AITokens.md
Locale: en-US
Module Name: PromptAI
ms.date: 05/11/2026
PlatyPS schema version: 2024-05-01
title: Measure-AITokens
---

# Measure-AITokens

## SYNOPSIS

Estimates the token count of a piece of text using a fast local heuristic.

## SYNTAX

### __AllParameterSets

```
Measure-AITokens [-Text] <string> [-Detailed] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

No network calls. Heuristic: ASCII chars / 4 (BPE-on-English baseline), CJK chars / 2 (kanji is denser per character because each character is 1–2 tokens in real tokenizers), other chars (accented Latin, emoji, symbols) / 3 as a middle bucket. Result is rounded up.

**This is an approximation.** Real tokenizers will disagree by 10–50% depending on content (especially code, emojis, and CJK). Use it for "will this prompt fit in the context window?" pre-flight checks, not for billing — for exact counts call the API and read `AIResponse.InputTokens`.

A NuGet-backed real-tokenizer mode (`cl100k_base` / `o200k_base`) is on the v0.2+ roadmap.

## EXAMPLES

### Example 1: English text

```powershell
Measure-AITokens "Explain PowerShell pipelines in one paragraph."
```

```
EstimatedTokens CharCount Method
--------------- --------- ------
             12        47 heuristic
```

### Example 2: Japanese text

```powershell
Measure-AITokens "日本語のテキストはトークン密度が異なります。"
```

```
EstimatedTokens CharCount Method
--------------- --------- ------
             11        22 heuristic
```

### Example 3: Pipeline input

```powershell
Get-Content prompt.txt | Measure-AITokens
```

Multiple pipeline lines are joined with newlines and counted as a single body.

### Example 4: Detailed breakdown

```powershell
Measure-AITokens "Hello こんにちは 🌟" -Detailed
```

```
EstimatedTokens CharCount Method    AsciiChars CjkChars OtherChars
--------------- --------- ------    ---------- -------- ----------
              5        13 heuristic          7        5          1
```

### Example 5: Pre-flight context-window check

```powershell
$tokens = (Measure-AITokens $longPrompt).EstimatedTokens
if ($tokens -gt 100000) {
    throw "Prompt is ~$tokens tokens; exceeds the 100K context budget."
}
```

## PARAMETERS

### -Text

The text to measure. Accepts pipeline input. Multiple pipeline strings are joined with newlines.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Detailed

Emit a per-character-class breakdown (`AsciiChars`, `CjkChars`, `OtherChars`) alongside the total.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: 'False'
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### System.String

Pipeline input is concatenated with newlines and counted as a single body.

## OUTPUTS

### System.Management.Automation.PSCustomObject

One object per call with `EstimatedTokens`, `CharCount`, `Method`, and (when `-Detailed`) per-character-class counts.

## NOTES

- CJK detection covers Hiragana, Katakana, CJK Unified Ideographs (incl. Extension A), Hangul Syllables, and CJK Symbols & Punctuation.
- The heuristic is intentionally simple and ships zero dependencies; the alternative (bundling a real BPE tokenizer like `cl100k_base`) would add ~1.5 MB to the module and only produce exact counts for OpenAI — Anthropic / Gemini / Llama / DeepSeek use different tokenizers, so even a "real" tokenizer would be approximate for them.

## RELATED LINKS

- [Invoke-Claude](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Claude.md)
- [Invoke-GPT](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-GPT.md)
