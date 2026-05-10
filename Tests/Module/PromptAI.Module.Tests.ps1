<#
    Pester tests for the PromptAI module surface.
    Loads the module from the assembled module path. Set $env:PROMPTAI_MODULE_PATH
    in CI to point at the staging dir; otherwise the tests assume PromptAI is
    importable by name (e.g., installed under $env:PSModulePath).
#>

BeforeAll {
    $modulePath = if ($env:PROMPTAI_MODULE_PATH) { $env:PROMPTAI_MODULE_PATH } else { 'PromptAI' }
    Import-Module $modulePath -Force -ErrorAction Stop
    $script:Module = Get-Module PromptAI
}

AfterAll {
    Remove-Module PromptAI -ErrorAction SilentlyContinue
}

Describe 'PromptAI module surface' {

    It 'loads' {
        $script:Module | Should -Not -BeNullOrEmpty
        $script:Module.Name | Should -Be 'PromptAI'
    }

    It 'reports a 3-part SemVer-style version' {
        $script:Module.Version.ToString() | Should -Match '^\d+\.\d+\.\d+$'
    }

    Context 'cmdlet exports' {
        $expected = @(
            'Invoke-Claude',
            'Invoke-GPT',
            'Invoke-Gemini',
            'Invoke-Llama',
            'Invoke-DeepSeek',
            'Compare-AI',
            'Get-DeepSeekBalance'
        )

        It "exports <_>" -ForEach $expected {
            $cmd = Get-Command -Module PromptAI -Name $_ -ErrorAction SilentlyContinue
            $cmd | Should -Not -BeNullOrEmpty
            $cmd.CommandType | Should -Be 'Cmdlet'
        }

        It 'exports exactly 7 cmdlets (no surprises)' {
            (Get-Command -Module PromptAI).Count | Should -Be 7
        }
    }

    Context 'Get-Help backed by MAML' {
        $cmdlets = @(
            'Invoke-Claude',
            'Invoke-GPT',
            'Invoke-Gemini',
            'Invoke-Llama',
            'Invoke-DeepSeek',
            'Compare-AI',
            'Get-DeepSeekBalance'
        )

        It '<_> has a non-empty Synopsis' -ForEach $cmdlets {
            $h = Get-Help $_
            $h.Synopsis | Should -Not -BeNullOrEmpty
            # Anti-regression: a cmdlet without a real MAML synopsis falls back to
            # the auto-generated syntax line, which always contains "[<CommonParameters>]".
            $h.Synopsis | Should -Not -Match '\[<CommonParameters>\]'
        }

        It '<_> has at least one Online link in MAML' -ForEach $cmdlets {
            $h = Get-Help $_
            ($h.relatedLinks.navigationLink | Where-Object linkText -eq 'Online Version').Count |
                Should -BeGreaterThan 0
        }
    }

    Context 'parameter validation' {
        # Compiled cmdlets surface FullyQualifiedErrorId as
        # "ParameterArgumentValidationError,<CSharpClassName>" rather than
        # "...,<verb-noun>". Match on the prefix.

        It 'Compare-AI -Provider rejects an unknown provider' {
            { Compare-AI -Prompt 'x' -Provider 'Bogus' -ErrorAction Stop } |
                Should -Throw -ErrorId 'ParameterArgumentValidationError,PromptAI.Cmdlets.CompareAICmdlet'
        }

        It 'Invoke-Llama -Provider rejects an unknown sub-provider' {
            { Invoke-Llama -Prompt 'x' -Provider 'Bogus' -ErrorAction Stop } |
                Should -Throw -ErrorId 'ParameterArgumentValidationError,PromptAI.Cmdlets.InvokeLlamaCmdlet'
        }

        It 'Invoke-Claude -MaxTokens rejects an out-of-range value' {
            { Invoke-Claude -Prompt 'x' -MaxTokens 500000 -ErrorAction Stop } |
                Should -Throw -ErrorId 'ParameterArgumentValidationError,PromptAI.Cmdlets.InvokeClaudeCmdlet'
        }
    }

    Context 'AIResponse type' {
        It 'is the documented OutputType for Invoke-Claude' {
            (Get-Command Invoke-Claude).OutputType.Type.FullName |
                Should -Contain 'PromptAI.Cmdlets.AIResponse'
        }
    }
}
