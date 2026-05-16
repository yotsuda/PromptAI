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
 [-MaxTokens <int>] [-History <AIResponse>] [-Image <string[]>] [-Temperature <double>]
 [-TopP <double>] [-StopSequence <string[]>] [-Json] [-Schema <hashtable>]
 [-Tool <hashtable[]>] [-MaxToolIterations <int>] [-AIScriptPolicy <string>]
 [<CommonParameters>]
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

### Example 7: Multi-turn conversation

```powershell
$h1 = Invoke-Llama "My name is Yoshi."
$h2 = Invoke-Llama "What's my name?" -History $h1
# Llama replies "Yoshi" because $h1's full conversation is replayed.
```

Pass an AIResponse as `-History` to continue the conversation. The returned AIResponse carries the full updated history in `.Turns`.

### Example 8: Image input (vision-capable models only)

```powershell
# Groq: only llama-3.2-90b-vision-preview supports vision
Invoke-Llama "Describe this" -Image .\screenshot.png -Model llama-3.2-90b-vision-preview

# Meta: Llama-4-* models are multimodal
Invoke-Llama "OCR this" -Image .\receipt.jpg -Provider Meta
```

`-Image` accepts local paths or HTTPS URLs. URL passthrough for OpenAI-compatible endpoints (no download needed); local files are base64-encoded and inlined. Calling with `-Image` against a text-only model fails at the API.

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

### -Json

Request a JSON-only response. Sends `response_format: {"type": "json_object"}`. Groq / Together / Meta all support this for current Llama models.

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

### -Schema

JSON Schema (PowerShell hashtable) the response must conform to. Implies `-Json`. Sends `response_format: {"type": "json_schema", ...}`. Schema-strict support varies by sub-provider and model — Groq supports it for newer Llama models; Meta and Together vary.

```yaml
Type: System.Collections.Hashtable
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

### -StopSequence

One or more strings that, if generated, halt the response. Sent as OpenAI-compatible `stop`.

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

### -Temperature

Sampling temperature. Range varies by sub-provider; OpenAI-compat default is 0–2.

```yaml
Type: System.Nullable`1[[System.Double]]
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

### -TopP

Nucleus sampling cutoff. Sent as OpenAI-compatible `top_p`.

```yaml
Type: System.Nullable`1[[System.Double]]
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

### -Tool

One or more tool declarations the model may invoke. Each hashtable must contain `Name` (string), `Description` (string), `Parameters` (Hashtable — a JSON Schema describing the tool arguments), and `Run` (ScriptBlock — receives the parsed arguments as a Hashtable in `$args[0]` and returns the result, which is stringified and sent back to the model). When the model issues a tool call, the scriptblock runs and its result is fed back; the model may chain multiple tool calls. Mutually exclusive with `-Schema`. The full sequence of tool invocations is returned on the `AIResponse.ToolCalls` property. Maps to OpenAI-compatible `tool_calls` (works on Groq, Meta, and Together; verify the specific model supports tools).

```yaml
Type: System.Collections.Hashtable[]
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

### -MaxToolIterations

Maximum number of tool-execution rounds per call. The model issues a batch of tool calls, the cmdlet executes them and feeds the results back, and the model decides whether to call more. This cap stops a runaway loop; raise it for genuinely long agent tasks. Default 10.

```yaml
Type: System.Int32
DefaultValue: '10'
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

### -AIScriptPolicy

Controls whether the AI may write and execute ad-hoc PowerShell via the implicit `exec_powershell` tool. Read-only scripts always auto-execute (no prompt). State-modifying scripts get a `-WhatIf` preview and require explicit approval (Yes/No/Edit/Quit) in the default `Prompt` mode. Detection uses static AST analysis: `Get-*`, `Select-*`, `Measure-*` style cmdlets are read-only; `Remove-*`, native exes, `.NET` method invocations, redirections, `Invoke-Expression`, and non-GET HTTP calls all trigger approval.

Modes: `Prompt` (default) — WhatIf preview + interactive approval; `AlwaysApprove` — execute everything (CI / trusted scripts); `AlwaysWhatIf` — never execute for real (exploration / dry-run); `Off` — do not expose `exec_powershell`; AI is restricted to tools passed via `-Tool`.

```yaml
Type: System.String
DefaultValue: 'Prompt'
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
- Prompt
- AlwaysApprove
- AlwaysWhatIf
- Off
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

Carries `.Text`, `.Model`, `.Provider`, `.InputTokens`, `.OutputTokens`, `.EstimatedCostUSD` (best-effort; Groq's free-tier models return null), `.Duration`, `.Turns` (full conversation including this exchange — pass back as `-History` to continue), and `.ToolCalls` (each invocation the model made during this turn: `Name`, `Arguments` JSON, `Result`, optional `Error`). Supports implicit conversion to string via `ToString()`.

## NOTES

- Requires the API key environment variable corresponding to the chosen `-Provider`.
- All three providers expose OpenAI-compatible chat completions, so non-Llama models offered by Groq/Together (Mixtral, Gemma, Qwen, DeepSeek) are not supported through this cmdlet by design — use a model-family-specific cmdlet instead.
- Streaming tokens are displayed via Host.UI.Write, which does not interfere with pipeline output.

## RELATED LINKS

- [Invoke-Claude](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Claude.md)
- [Invoke-GPT](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-GPT.md)
- [Invoke-Gemini](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Gemini.md)
- [Invoke-DeepSeek](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-DeepSeek.md)
- [Compare-AI](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Compare-AI.md)
- [Groq API Documentation](https://console.groq.com/docs/api-reference)
- [Llama API Documentation](https://llama.developer.meta.com/docs)
- [Together AI Documentation](https://docs.together.ai/reference/chat-completions)
