@{

RootModule = 'PromptAI.dll'

ModuleVersion = '0.1.0'

CompatiblePSEditions = @('Core')

GUID = 'a7e3f1b2-4c5d-6e8f-9a0b-1c2d3e4f5a6b'

Author = 'Yoshifumi Tsuda'

CompanyName = 'Yoshifumi Tsuda'

Copyright = '(c) Yoshifumi Tsuda. All rights reserved.'

Description = 'Call AI models from PowerShell with streaming. Supports Anthropic Claude, OpenAI GPT, and Google Gemini. One-liner syntax with real-time token display.'

PowerShellVersion = '7.4'

FormatsToProcess = @('PromptAI.Format.ps1xml')

CmdletsToExport = @(
'Invoke-Claude',
'Invoke-GPT',
'Invoke-Gemini'
)

FunctionsToExport = @()

VariablesToExport = @()

AliasesToExport = @()

PrivateData = @{

    PSData = @{

        Tags = 'AI','Claude','GPT','Gemini','Anthropic','OpenAI','Google','LLM','Streaming','Prompt'

        LicenseUri = 'https://github.com/yotsuda/PromptAI/blob/main/LICENSE'

        ProjectUri = 'https://github.com/yotsuda/PromptAI'

        ReleaseNotes = 'Initial release. Invoke-Claude, Invoke-GPT, Invoke-Gemini with SSE streaming.'

    }

}

}
