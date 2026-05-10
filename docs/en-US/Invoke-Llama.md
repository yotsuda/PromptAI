---
document type: cmdlet
external help file: PromptAI.dll-Help.xml
HelpUri: https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Llama.md
Locale: en-US
Module Name: PromptAI
ms.date: 05/10/2026
PlatyPS schema version: 2024-05-01
title: Invoke-Llama
---

# Invoke-Llama

## SYNOPSIS

Sends a prompt to a Meta Llama model via Groq, Meta's official Llama API, or Together AI with real-time streaming.

## SYNTAX

### __AllParameterSets

```
Invoke-Llama [-Prompt] <string> [[-SystemPrompt] <string>] [-Provider <string>] [-Model <string>]
 [-MaxTokens <int>] [-History <AIResponse>] [-Image <string[]>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Sends a prompt to a Llama model using SSE (Server-Sent Events) streaming. Tokens are displayed on the console in real time as they arrive. The full response is returned as an AIResponse object, which behaves like a string in most contexts.

Llama is open-weight, so the same model family is served by multiple hosts. `-Provider` selects which one:

| Provider   | Endpoint                                            | API key env       | Default model                                    |
|------------|-----------------------------------------------------|-------------------|--------------------------------------------------|
| `Groq` (default) | `https://api.groq.com/openai/v1/chat/completions`   | `GROQ_API_KEY`    | `llama-3.3-70b-versatile`                        |
| `Meta`     | `https://api.llama.com/v1/chat/completions`         | `LLAMA_API_KEY`   | `Llama-4-Maverick-17B-128E-Instruct-FP8`         |
| `Together` | `https://api.together.xyz/v1/chat/completions`      | `TOGETHER_API_KEY`| `meta-llama/Llama-3.3-70B-Instruct-Turbo`        |

All three are OpenAI-compatible chat completion endpoints, so the request/response shape is identical.

## EXAMPLES

### Example 1: Basic prompt (Groq, default)

```powershell
Invoke-Llama "Explain PowerShell pipelines in one paragraph."
```

Sends the prompt to Llama 3.3 70B via Groq (default provider) and streams the response.

### Example 2: Capture result in a variable

```powershell
$result = Invoke-Llama "What is 2+2?"
$result.Text    # "4"
"$result"       # "4" (implicit string conversion)
```

The AIResponse object supports `.Text` property access and implicit string conversion.

### Example 3: Switch to Meta's official API

```powershell
Invoke-Llama -Provider Meta "Summarize this article: $text"
```

Routes through `api.llama.com` using `LLAMA_API_KEY`.

### Example 4: Use Together AI for a specific model

```powershell
Invoke-Llama -Provider Together -Model "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo" "Translate to Japanese: Hello"
```

Together exposes turbo variants. Tab completion suggests provider-appropriate IDs.

### Example 5: Pipeline input

```powershell
Get-Content error.log | Invoke-Llama "Summarize the errors in this log."
```

Content piped into Invoke-Llama is joined with newlines and prepended to the prompt.

### Example 6: System prompt

```powershell
Invoke-Llama "List running services" -SystemPrompt "You are a Windows system administrator. Be concise."
```

The system prompt guides the AI's behavior and response style.

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

Llama model ID. The accepted values depend on `-Provider`. Tab completion suggests provider-appropriate IDs. If omitted, a per-provider default is used.

```yaml
Type: System.String
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

Prior conversation. Pass an AIResponse from an earlier `Invoke-X` call to continue the conversation.

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

### -Image

One or more image inputs. Each entry is a local file path or HTTPS URL. Vision is supported only by specific Llama models — Groq's `llama-3.2-90b-vision-preview` and Meta's Llama-4-* multimodal models. Calling with `-Image` against a text-only model fails at the API.

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
AcceptedValues: []
HelpMessage: ''
```

### -Provider

Hosting provider for the Llama model. One of `Groq`, `Meta`, `Together`. Default is `Groq` (free tier, fastest inference).

```yaml
Type: System.String
DefaultValue: Groq
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
- Groq
- Meta
- Together
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

Carries `.Text`, `.Model`, `.Provider`, `.InputTokens`, `.OutputTokens`, `.EstimatedCostUSD` (best-effort; Groq's free-tier models return null), `.Duration`, and `.Turns` (full conversation including this exchange — pass back as `-History` to continue). Supports implicit conversion to string via `ToString()`.

## NOTES

- Requires the API key environment variable corresponding to the chosen `-Provider`.
- All three providers expose OpenAI-compatible chat completions, so non-Llama models offered by Groq/Together (Mixtral, Gemma, Qwen, DeepSeek) are not supported through this cmdlet by design — use a model-family-specific cmdlet instead.
- Streaming tokens are displayed via Host.UI.Write, which does not interfere with pipeline output.

## RELATED LINKS

- [Invoke-Claude](Invoke-Claude.md)
- [Invoke-GPT](Invoke-GPT.md)
- [Invoke-Gemini](Invoke-Gemini.md)
- [Invoke-DeepSeek](Invoke-DeepSeek.md)
- [Compare-AI](Compare-AI.md)
- [Groq API Documentation](https://console.groq.com/docs/api-reference)
- [Llama API Documentation](https://llama.developer.meta.com/docs)
- [Together AI Documentation](https://docs.together.ai/reference/chat-completions)
