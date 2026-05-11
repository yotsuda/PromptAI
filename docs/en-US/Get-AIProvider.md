---
document type: cmdlet
external help file: PromptAI.dll-Help.xml
HelpUri: https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Get-AIProvider.md
Locale: en-US
Module Name: PromptAI
ms.date: 05/11/2026
PlatyPS schema version: 2024-05-01
title: Get-AIProvider
---

# Get-AIProvider

## SYNOPSIS

Lists every AI provider this module supports, with its API key env var, configuration status, and default model.

## SYNTAX

### __AllParameterSets

```
Get-AIProvider [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Strictly local — no network calls. For each of Claude / GPT / Gemini / Llama / DeepSeek, reports whether the corresponding API key environment variable is set and what model the related `Invoke-X` cmdlet uses by default. Useful as a pre-flight check before sharing a script that assumes certain keys are present, or before running `Compare-AI` (which queries every configured provider).

## EXAMPLES

### Example 1: Quick survey

```powershell
Get-AIProvider
```

```
Name     EnvVar             IsConfigured DefaultModel
----     ------             ------------ ------------
Claude   ANTHROPIC_API_KEY          True claude-sonnet-4-20250514
GPT      OPENAI_API_KEY             True gpt-4o
Gemini   GEMINI_API_KEY             True gemini-2.5-flash
Llama    GROQ_API_KEY               True llama-3.3-70b-versatile (Groq default; …)
DeepSeek DEEPSEEK_API_KEY           True deepseek-v4-flash
```

### Example 2: Filter to only configured providers

```powershell
Get-AIProvider | Where-Object IsConfigured
```

### Example 3: Pre-flight gate in a script

```powershell
$missing = Get-AIProvider | Where-Object { -not $_.IsConfigured } | Select-Object -ExpandProperty EnvVar
if ($missing) {
    throw "Missing API keys: $($missing -join ', '). Set them and retry."
}
```

## PARAMETERS

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

This cmdlet does not accept pipeline input.

## OUTPUTS

### System.Management.Automation.PSCustomObject

One object per supported provider, with properties:
- `Name` — friendly name (e.g. `Claude`, `GPT`)
- `EnvVar` — environment variable name expected by the corresponding cmdlet
- `IsConfigured` — `True` when the env var is set and non-empty
- `DefaultModel` — the model the corresponding `Invoke-X` cmdlet uses if `-Model` is omitted
- `KeyPrefix` — first 4 characters of the API key (e.g. `gsk_`, `sk-`), or `null` when unconfigured

## NOTES

- Inspects the **process** environment. Setting a User-scope env var via `[Environment]::SetEnvironmentVariable(..., 'User')` does not appear here until the process is restarted.

## RELATED LINKS

- [Invoke-Claude](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Claude.md)
- [Invoke-GPT](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-GPT.md)
- [Invoke-Gemini](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Gemini.md)
- [Invoke-Llama](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Llama.md)
- [Invoke-DeepSeek](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-DeepSeek.md)
- [Compare-AI](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Compare-AI.md)
