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
 [-History <AIResponse>] [-Temperature <double>] [-TopP <double>] [-StopSequence <string[]>]
 [-Json] [-Schema <hashtable>] [-Tool <hashtable[]>] [-MaxToolIterations <int>]
 [-AIScriptPolicy <string>] [<CommonParameters>]
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

### -Json

Request a JSON-only response. Sends `response_format: {"type": "json_object"}`.

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

JSON Schema (PowerShell hashtable) the response must conform to. Implies `-Json`. DeepSeek's strict `json_schema` support varies by model, so this falls back to schema-in-system-prompt + `json_object` mode (best-effort, not guaranteed strict).

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

Sampling temperature (DeepSeek valid range 0–2). Lower = more deterministic, higher = more creative.

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

One or more tool declarations the model may invoke. Each hashtable must contain `Name` (string), `Description` (string), `Parameters` (Hashtable — a JSON Schema describing the tool arguments), and `Run` (ScriptBlock — receives the parsed arguments as a Hashtable in `$args[0]` and returns the result, which is stringified and sent back to the model). When the model issues a tool call, the scriptblock runs and its result is fed back; the model may chain multiple tool calls. Mutually exclusive with `-Schema`. The full sequence of tool invocations is returned on the `AIResponse.ToolCalls` property. Maps to OpenAI-compatible `tool_calls`; DeepSeek reasoner models emit chain-of-thought which the cmdlet captures and replays across iterations automatically.

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

Carries `.Text`, `.Model`, `.Provider`, `.InputTokens`, `.OutputTokens`, `.EstimatedCostUSD` (best-effort), `.Duration`, `.Turns` (full conversation including this exchange — pass back as `-History` to continue), and `.ToolCalls` (each invocation the model made during this turn: `Name`, `Arguments` JSON, `Result`, optional `Error`). Supports implicit conversion to string via `ToString()`.

## NOTES

- Requires `DEEPSEEK_API_KEY` environment variable.
- Uses DeepSeek Chat Completions API (https://api.deepseek.com/chat/completions) with SSE streaming.
- API is OpenAI-compatible.
- Streaming tokens are displayed via Host.UI.Write, which does not interfere with pipeline output.

## RELATED LINKS

- [Invoke-Claude](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Claude.md)
- [Invoke-GPT](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-GPT.md)
- [Invoke-Gemini](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Gemini.md)
- [Invoke-Llama](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Llama.md)
- [Compare-AI](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Compare-AI.md)
- [Get-DeepSeekBalance](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Get-DeepSeekBalance.md)
- [DeepSeek API Documentation](https://api-docs.deepseek.com/)
