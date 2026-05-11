---
document type: cmdlet
external help file: PromptAI.dll-Help.xml
HelpUri: https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Export-AIConversation.md
Locale: en-US
Module Name: PromptAI
ms.date: 05/11/2026
PlatyPS schema version: 2024-05-01
title: Export-AIConversation
---

# Export-AIConversation

## SYNOPSIS

Serializes a single AIResponse (full conversation history) to a JSON file.

## SYNTAX

### __AllParameterSets

```
Export-AIConversation [-InputObject] <AIResponse> [-Path] <string> [-Force] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Pairs with `Import-AIConversation`. An `AIResponse` already carries the full conversation in `.Turns`, so exporting the most recent one captures the whole exchange. The JSON is human-readable (indented, schema-versioned). Resume in another PowerShell session by piping the loaded AIResponse back into `Invoke-X -History`.

Refuses to overwrite an existing file unless `-Force` is supplied.

## EXAMPLES

### Example 1: Save a conversation

```powershell
$h1 = Invoke-Claude "Explain monads briefly." -SystemPrompt "You are a Haskell tutor."
$h2 = Invoke-Claude "Now contrast with applicatives." -History $h1
$h2 | Export-AIConversation -Path .\monads.json
```

### Example 2: Overwrite an existing file

```powershell
$h2 | Export-AIConversation -Path .\monads.json -Force
```

### Example 3: Round-trip (export → import → continue)

```powershell
# Session A
$h | Export-AIConversation -Path .\chat.json

# Session B (later, even after closing the shell)
$resumed = Import-AIConversation -Path .\chat.json
Invoke-Claude "Where were we?" -History $resumed
```

## PARAMETERS

### -Force

Overwrite the file if it already exists.

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

### -InputObject

The AIResponse to serialize. Accepts pipeline input. Pass the most recent AIResponse — it already contains the full conversation. Pipelines yielding more than one AIResponse are rejected.

```yaml
Type: PromptAI.Cmdlets.AIResponse
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

### -Path

Destination JSON file path.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 1
  IsRequired: true
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

### PromptAI.Cmdlets.AIResponse

A single AIResponse. Multiple inputs from the pipeline are rejected.

## OUTPUTS

### None

This cmdlet writes a file; it does not emit pipeline output.

## NOTES

- The JSON format includes a `SchemaVersion` field (currently `"1"`) for future-proofing.

## RELATED LINKS

- [Import-AIConversation](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Import-AIConversation.md)
- [Invoke-Claude](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Claude.md)
