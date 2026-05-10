---
document type: cmdlet
external help file: PromptAI.dll-Help.xml
HelpUri: https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Gemini.md
Locale: en-US
Module Name: PromptAI
ms.date: 05/10/2026
PlatyPS schema version: 2024-05-01
title: Invoke-Gemini
---

# Invoke-Gemini

## SYNOPSIS

Sends a prompt to the Google Gemini API and returns the response with real-time streaming.

## SYNTAX

### __AllParameterSets

```
Invoke-Gemini [-Prompt] <string> [[-SystemPrompt] <string>] [-Model <string>] [-MaxTokens <int>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Sends a prompt to the Google Gemini API using SSE (Server-Sent Events) streaming via the `streamGenerateContent` endpoint. Tokens are displayed on the console in real time as they arrive. The full response is returned as an AIResponse object, which behaves like a string in most contexts.

Requires the `GEMINI_API_KEY` environment variable to be set with a valid Google AI Studio API key.

Tab completion suggests available models from the v1beta `models` API; falls back to gemini-2.5-flash, gemini-2.5-pro, gemini-2.0-flash when the API key is unset or unreachable. Default model is gemini-2.5-flash.

## EXAMPLES

### Example 1: Basic prompt

```powershell
Invoke-Gemini "Explain PowerShell pipelines in one paragraph."
```

Sends the prompt to gemini-2.5-flash (default model) and streams the response to the console.

### Example 2: Capture result in a variable

```powershell
$result = Invoke-Gemini "What is 2+2?"
$result.Text    # "4"
"$result"       # "4" (implicit string conversion)
```

The AIResponse object supports `.Text` property access and implicit string conversion.

### Example 3: Use a specific model

```powershell
Invoke-Gemini -Model gemini-2.5-pro "Write a detailed analysis of this error: $errorMsg"
Invoke-Gemini -Model gemini-2.0-flash "Translate to Japanese: Hello"
```

Use `-Model` with a full Gemini model ID. Tab completion suggests available models.

### Example 4: Pipeline input

```powershell
Get-Content error.log | Invoke-Gemini "Summarize the errors in this log."
```

Content piped into Invoke-Gemini is joined with newlines and prepended to the prompt.

### Example 5: System prompt

```powershell
Invoke-Gemini "List running services" -SystemPrompt "You are a Windows system administrator. Be concise."
```

The system prompt is sent as `systemInstruction` and guides the AI's behavior and response style.

### Example 6: Compare across providers

```powershell
$claude = Invoke-Claude "What is the best sorting algorithm?"
$gpt    = Invoke-GPT    "What is the best sorting algorithm?"
$gemini = Invoke-Gemini "What is the best sorting algorithm?"
"Claude says: $claude"
"GPT says: $gpt"
"Gemini says: $gemini"
```

Use Invoke-Claude, Invoke-GPT, and Invoke-Gemini to compare responses from different AI providers.

## PARAMETERS

### -MaxTokens

Maximum number of output tokens in the AI response. Default is 4096. Sent as `generationConfig.maxOutputTokens` only when set to a non-default value.

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

Gemini model name. Full model IDs are accepted (e.g., gemini-2.5-flash, gemini-2.5-pro, gemini-2.0-flash). Default is gemini-2.5-flash.

```yaml
Type: System.String
DefaultValue: 'gemini-2.5-flash'
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

### -SystemPrompt

Optional system prompt sent as `systemInstruction` to guide the AI's behavior and response style.

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

An object containing the AI response text. Has a `.Text` property and supports implicit conversion to string via `ToString()`.

## NOTES

- Requires `GEMINI_API_KEY` environment variable.
- Uses Google Generative Language API streamGenerateContent (https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse) with SSE streaming.
- Streaming tokens are displayed via Host.UI.Write, which does not interfere with pipeline output.

## RELATED LINKS

- [Invoke-Claude](Invoke-Claude.md)
- [Invoke-GPT](Invoke-GPT.md)
- [Invoke-Llama](Invoke-Llama.md)
- [Invoke-DeepSeek](Invoke-DeepSeek.md)
- [Gemini API Documentation](https://ai.google.dev/gemini-api/docs)
