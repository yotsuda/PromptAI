---
document type: cmdlet
external help file: PromptAI.dll-Help.xml
HelpUri: https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Compare-AI.md
Locale: en-US
Module Name: PromptAI
ms.date: 05/10/2026
PlatyPS schema version: 2024-05-01
title: Compare-AI
---

# Compare-AI

## SYNOPSIS

Sends the same prompt to multiple AI providers in parallel and returns one AIResponse per provider.

## SYNTAX

### __AllParameterSets

```
Compare-AI [-Prompt] <string> [[-SystemPrompt] <string>] [-Provider <string[]>] [-MaxTokens <int>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Dispatches the same prompt to every requested provider in parallel (`Task.WhenAll`) and emits one `AIResponse` per provider. Default-Provider behavior: if `-Provider` is omitted, every provider whose API key environment variable is currently set is queried. Each provider uses its own default model.

A failed call is surfaced as an `AIResponse` with `.Text` set to `"[error: ...]"` and `.Model` set to `"(none)"` so the result table stays uniform.

## EXAMPLES

### Example 1: All configured providers, side by side

```powershell
Compare-AI "What is 2+2? Reply with just the digit." |
    Format-Table Provider, Model, Text, InputTokens, OutputTokens, EstimatedCostUSD,
                 @{Name='Sec'; Expression={[Math]::Round($_.Duration.TotalSeconds, 2)}} -AutoSize
```

Sends the prompt to every configured provider; pipes the resulting `AIResponse[]` into a comparison table.

### Example 2: Pick specific providers

```powershell
Compare-AI "Summarize quantum entanglement in one sentence." -Provider Claude, GPT, Gemini |
    Format-Table Provider, Text -Wrap
```

### Example 3: Persona system prompt across providers

```powershell
Compare-AI "Explain monads" -SystemPrompt "You are a Haskell tutor. Be concise." |
    Format-Table Provider, Model, Text -Wrap
```

### Example 4: Cheapest by total cost

```powershell
Compare-AI "Write a haiku about PowerShell." |
    Sort-Object EstimatedCostUSD |
    Format-Table Provider, EstimatedCostUSD, Text -Wrap
```

## PARAMETERS

### -MaxTokens

Maximum number of tokens for each provider's response. Default 4096. Applied uniformly across providers.

```yaml
Type: System.Int32
DefaultValue: '4096'
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

### -Prompt

The prompt sent to every selected provider. Accepts pipeline input (multiple lines joined with newlines).

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

### -Provider

Subset of providers to query. Defaults to every provider whose API key env var is set. For Llama, the sub-provider is fixed to Groq inside Compare-AI.

```yaml
Type: System.String[]
DefaultValue: ''
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
AcceptedValues:
- Claude
- GPT
- Gemini
- Llama
- DeepSeek
HelpMessage: ''
```

### -SystemPrompt

Optional system prompt applied uniformly to every provider.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 1
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

Prompt text. Multiple strings from the pipeline are joined with newlines.

## OUTPUTS

### PromptAI.Cmdlets.AIResponse

One `AIResponse` per provider queried. Failed calls surface as an `AIResponse` with `.Text` starting with `"[error: ..."`.

## NOTES

- Token streaming is suppressed in Compare-AI to avoid interleaved output from multiple providers. Tokens for each provider are accumulated and emitted together at the end.
- Each provider uses its own default model and its own pricing entry; cross-provider cost comparisons are best-effort.
- For Llama, Compare-AI fixes the sub-provider to Groq. Use `Invoke-Llama -Provider Meta` (etc.) directly if you need Meta or Together.

## RELATED LINKS

- [Invoke-Claude](Invoke-Claude.md)
- [Invoke-GPT](Invoke-GPT.md)
- [Invoke-Gemini](Invoke-Gemini.md)
- [Invoke-Llama](Invoke-Llama.md)
- [Invoke-DeepSeek](Invoke-DeepSeek.md)
