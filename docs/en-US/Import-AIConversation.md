---
document type: cmdlet
external help file: PromptAI.dll-Help.xml
HelpUri: https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Import-AIConversation.md
Locale: en-US
Module Name: PromptAI
ms.date: 05/11/2026
PlatyPS schema version: 2024-05-01
title: Import-AIConversation
---

# Import-AIConversation

## SYNOPSIS

Reads a conversation JSON file written by Export-AIConversation and emits an AIResponse.

## SYNTAX

### __AllParameterSets

```
Import-AIConversation [-Path] <string> [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Pairs with `Export-AIConversation`. Loads the JSON file and reconstructs an `AIResponse` carrying the same `Text`, `Model`, `Provider`, `InputTokens`, `OutputTokens`, `EstimatedCostUSD`, `Duration`, `SystemPrompt`, and full `Turns` history. Pass the result as `-History` to a subsequent `Invoke-X` call to resume the conversation.

## EXAMPLES

### Example 1: Resume a saved conversation

```powershell
$h = Import-AIConversation -Path .\monads.json
Invoke-Claude "Sum up what we covered." -History $h
```

### Example 2: Pipeline from Get-ChildItem

```powershell
Get-ChildItem .\saved-chats\*.json | Import-AIConversation | ForEach-Object {
    "$($_.Provider) ($($_.Turns.Count) turns): $($_.Turns[0].Content.Substring(0, 40))..."
}
```

`-Path` accepts pipeline input by property name (`FullName`), so `Get-ChildItem` output flows in directly.

## PARAMETERS

### -Path

Source JSON file path. Accepts pipeline input by property name `FullName` so `Get-ChildItem` results pipe through cleanly.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases:
- FullName
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: true
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

A path. Pipeline input by property name `FullName`.

## OUTPUTS

### PromptAI.Cmdlets.AIResponse

The reconstructed AIResponse. Pass to `-History` on a subsequent `Invoke-X` call.

## NOTES

- Throws `FileNotFoundException` if the file does not exist.
- Throws on malformed JSON or schema mismatch.

## RELATED LINKS

- [Export-AIConversation](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Export-AIConversation.md)
- [Invoke-Claude](https://github.com/yotsuda/PromptAI/blob/master/docs/en-US/Invoke-Claude.md)
