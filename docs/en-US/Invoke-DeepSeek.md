---
document type: cmdlet
external help file: PromptAI.dll-Help.xml
HelpUri: https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-DeepSeek.md
Locale: en-US
Module Name: PromptAI
ms.date: 05/10/2026
PlatyPS schema version: 2024-05-01
title: Invoke-DeepSeek
---

# Invoke-DeepSeek

## SYNOPSIS

Sends a prompt to the DeepSeek API and returns the response with real-time streaming.

## SYNTAX

### __AllParameterSets

```
Invoke-DeepSeek [-Prompt] <string> [[-SystemPrompt] <string>] [-Model <string>] [-MaxTokens <int>]
 [-History <AIResponse>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Sends a prompt to the DeepSeek API using SSE (Server-Sent Events) streaming. Tokens are displayed on the console in real time as they arrive. The full response is returned as an AIResponse object, which behaves like a string in most contexts.

Requires the `DEEPSEEK_API_KEY` environment variable to be set with a valid DeepSeek API key.

DeepSeek's API is OpenAI-compatible. Current models:
- **`deepseek-v4-flash`** (default) — fast, cheap general-purpose chat
- **`deepseek-v4-pro`** — more capable, supports chain-of-thought reasoning

Note: `deepseek-chat` and `deepseek-reasoner` are also accepted but scheduled for deprecation on 2026-07-24.

## EXAMPLES

### Example 1: Basic prompt

```powershell
Invoke-DeepSeek "Explain PowerShell pipelines in one paragraph."
```

Sends the prompt to deepseek-v4-flash (default) and streams the response.

### Example 2: Capture result in a variable

```powershell
$result = Invoke-DeepSeek "What is 2+2?"
$result.Text    # "4"
"$result"       # "4" (implicit string conversion)
```

The AIResponse object supports `.Text` property access and implicit string conversion.

### Example 3: Use the more capable model

```powershell
Invoke-DeepSeek -Model deepseek-v4-pro "Prove that the square root of 2 is irrational."
```

`deepseek-v4-pro` is more capable than the default `-flash` variant and supports chain-of-thought reasoning. Note: only the final answer is streamed; reasoning content (`delta.reasoning_content`) is not surfaced by this cmdlet today.

### Example 4: Pipeline input

```powershell
Get-Content error.log | Invoke-DeepSeek "Summarize the errors in this log."
```

Content piped into Invoke-DeepSeek is joined with newlines and prepended to the prompt.

### Example 5: System prompt

```powershell
Invoke-DeepSeek "List running services" -SystemPrompt "You are a Windows system administrator. Be concise."
```

The system prompt guides the AI's behavior and response style.

### Example 6: Compare across providers

```powershell
$claude   = Invoke-Claude   "What is the best sorting algorithm?"
$gpt      = Invoke-GPT      "What is the best sorting algorithm?"
$deepseek = Invoke-DeepSeek "What is the best sorting algorithm?"
"Claude says: $claude"
"GPT says: $gpt"
"DeepSeek says: $deepseek"
```

### Example 7: Multi-turn conversation

```powershell
$h1 = Invoke-DeepSeek "My name is Yoshi."
$h2 = Invoke-DeepSeek "What's my name?" -History $h1
# DeepSeek replies "Yoshi" because $h1's full conversation is replayed.
```

Pass an AIResponse as `-History` to continue the conversation. The returned AIResponse carries the full updated history in `.Turns`. Image input is not supported by DeepSeek's current models — `Invoke-DeepSeek` does not expose `-Image`.

## PARAMETERS

### -MaxTokens

Maximum number of tokens in the AI response. Default is 4096.

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

### -Model

DeepSeek model name. Accepts `deepseek-v4-flash` (default) or `deepseek-v4-pro`. Legacy `deepseek-chat` / `deepseek-reasoner` also work until their 2026-07-24 deprecation. Tab completion fetches the live list from `/models` when the API key is set.

```yaml
Type: System.String
DefaultValue: 'deepseek-v4-flash'
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

The user prompt to send to the AI. Accepts pipeline input. Multiple pipeline strings are joined with newlines.

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

### -History

Prior conversation. Pass an AIResponse from an earlier `Invoke-X` call to continue the conversation. `-SystemPrompt` is inherited from the prior turn unless you pass it again to override.

```yaml
Type: PromptAI.Cmdlets.AIResponse
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
AcceptedValues: []
HelpMessage: ''
```

### -SystemPrompt

Optional system prompt to guide the AI's behavior and response style.

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

Carries `.Text`, `.Model`, `.Provider`, `.InputTokens`, `.OutputTokens`, `.EstimatedCostUSD` (best-effort), `.Duration`, and `.Turns` (full conversation including this exchange — pass back as `-History` to continue). Supports implicit conversion to string via `ToString()`.

## NOTES

- Requires `DEEPSEEK_API_KEY` environment variable.
- Uses DeepSeek Chat Completions API (https://api.deepseek.com/chat/completions) with SSE streaming.
- API is OpenAI-compatible.
- Streaming tokens are displayed via Host.UI.Write, which does not interfere with pipeline output.

## RELATED LINKS

- [Invoke-Claude](Invoke-Claude.md)
- [Invoke-GPT](Invoke-GPT.md)
- [Invoke-Gemini](Invoke-Gemini.md)
- [Invoke-Llama](Invoke-Llama.md)
- [Compare-AI](Compare-AI.md)
- [Get-DeepSeekBalance](Get-DeepSeekBalance.md)
- [DeepSeek API Documentation](https://api-docs.deepseek.com/)
