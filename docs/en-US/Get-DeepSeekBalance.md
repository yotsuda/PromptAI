---
document type: cmdlet
external help file: PromptAI.dll-Help.xml
HelpUri: https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Get-DeepSeekBalance.md
Locale: en-US
Module Name: PromptAI
ms.date: 05/10/2026
PlatyPS schema version: 2024-05-01
title: Get-DeepSeekBalance
---

# Get-DeepSeekBalance

## SYNOPSIS

Returns the current DeepSeek API key balance.

## SYNTAX

### __AllParameterSets

```
Get-DeepSeekBalance [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Calls `GET https://api.deepseek.com/user/balance` and emits one PSCustomObject per currency. Useful for keeping an eye on your top-up before running a long batch.

DeepSeek is currently the only provider in this module that exposes a public balance endpoint to standard API keys. Anthropic and OpenAI require admin / dashboard keys; Gemini bills via GCP; Groq / Meta / Together expose billing only through their consoles. There is no `Get-AIBalance` umbrella cmdlet by design.

Requires the `DEEPSEEK_API_KEY` environment variable.

## EXAMPLES

### Example 1: Quick balance check

```powershell
Get-DeepSeekBalance
```

```
Currency TotalBalance GrantedBalance ToppedUpBalance IsAvailable
-------- ------------ -------------- --------------- -----------
USD              1.97           0.00            1.97        True
```

### Example 2: Pre-flight in a script

```powershell
$bal = Get-DeepSeekBalance | Where-Object Currency -eq 'USD'
if ($bal.TotalBalance -lt 0.50) {
    throw "DeepSeek balance is $($bal.TotalBalance) USD — top up before running the batch."
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

One object per currency, with properties:
- `Currency` — currency code (e.g. `USD`, `CNY`)
- `TotalBalance` — sum of granted + topped-up minus usage
- `GrantedBalance` — promotional / free credits remaining
- `ToppedUpBalance` — paid credits remaining
- `IsAvailable` — whether the account is currently allowed to make API calls

## NOTES

- The balance does not refresh in real time during a long-running call; it reflects the last completed billing reconciliation on DeepSeek's side.
- See https://api-docs.deepseek.com/api/get-user-balance for the underlying API.

## RELATED LINKS

- [Invoke-DeepSeek](Invoke-DeepSeek.md)
