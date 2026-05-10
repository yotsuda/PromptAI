@{

RootModule = 'PromptAI.dll'

ModuleVersion = '0.1.2'

CompatiblePSEditions = @('Core')

GUID = 'a7e3f1b2-4c5d-6e8f-9a0b-1c2d3e4f5a6b'

Author = 'Yoshifumi Tsuda'

CompanyName = 'Yoshifumi Tsuda'

Copyright = '(c) Yoshifumi Tsuda. All rights reserved.'

Description = 'Call AI models from PowerShell with real-time streaming. Supports Anthropic Claude, OpenAI GPT, Google Gemini, Meta Llama (via Groq/Meta/Together), and DeepSeek. Works with PowerShell.MCP for AI-to-AI communication.'

PowerShellVersion = '7.4'

FormatsToProcess = @('PromptAI.Format.ps1xml')

CmdletsToExport = @(
'Invoke-Claude',
'Invoke-GPT',
'Invoke-Gemini',
'Invoke-Llama',
'Invoke-DeepSeek'
)

FunctionsToExport = @()

VariablesToExport = @()

AliasesToExport = @()

PrivateData = @{

    PSData = @{

        Tags = 'AI','Claude','GPT','Gemini','Llama','DeepSeek','Anthropic','OpenAI','Google','Meta','Groq','Together','LLM','Streaming','Prompt'

        LicenseUri = 'https://github.com/yotsuda/PromptAI/blob/master/LICENSE'

        ProjectUri = 'https://github.com/yotsuda/PromptAI'

        ReleaseNotes = 'PromptAI - PowerShell cmdlets for AI APIs

Stream prompts to Anthropic Claude, OpenAI GPT, Google Gemini, Meta Llama (via Groq/Meta/Together), and DeepSeek from PowerShell, with real-time token-by-token output and a unified AIResponse object. Designed to compose with PowerShell.MCP for AI-to-AI workflows.

Per-version release notes: https://github.com/yotsuda/PromptAI/releases'

    }

}

}
