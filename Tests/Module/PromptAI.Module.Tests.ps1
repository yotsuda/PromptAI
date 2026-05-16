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
            'Get-DeepSeekBalance',
            'Get-AIProvider',
            'Measure-AITokens',
            'Export-AIConversation',
            'Import-AIConversation'
        )

        It "exports <_>" -ForEach $expected {
            $cmd = Get-Command -Module PromptAI -Name $_ -ErrorAction SilentlyContinue
            $cmd | Should -Not -BeNullOrEmpty
            $cmd.CommandType | Should -Be 'Cmdlet'
        }

        It 'exports exactly 11 cmdlets (no surprises)' {
            (Get-Command -Module PromptAI).Count | Should -Be 11
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
            'Get-DeepSeekBalance',
            'Get-AIProvider',
            'Measure-AITokens',
            'Export-AIConversation',
            'Import-AIConversation'
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

    Context '-AIScriptPolicy on every Invoke-X' {
        # All 5 Invoke-X cmdlets must expose -AIScriptPolicy with the same
        # ValidateSet and default. If someone removes the parameter from one
        # cmdlet by mistake, this test catches it before release.
        $invokers = 'Invoke-Claude','Invoke-GPT','Invoke-Gemini','Invoke-Llama','Invoke-DeepSeek'

        It '<_> exposes -AIScriptPolicy' -ForEach $invokers {
            $param = (Get-Command $_).Parameters['AIScriptPolicy']
            $param | Should -Not -BeNullOrEmpty
            $param.ParameterType | Should -Be ([string])
        }

        It '<_> -AIScriptPolicy ValidateSet covers all 4 modes' -ForEach $invokers {
            $vs = (Get-Command $_).Parameters['AIScriptPolicy'].Attributes |
                  Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] }
            $vs | Should -Not -BeNullOrEmpty
            $vs.ValidValues | Should -Be @('Prompt','AlwaysApprove','AlwaysWhatIf','Off')
        }

        It '<_> rejects an unknown AIScriptPolicy value' -ForEach $invokers {
            $cmd = $_
            { & $cmd -Prompt 'x' -AIScriptPolicy 'Bogus' -ErrorAction Stop } |
                Should -Throw -ErrorId "ParameterArgumentValidationError,PromptAI.Cmdlets.$($cmd.Replace('-',''))Cmdlet"
        }

        It '<_> has AIScriptPolicy help describing all four modes' -ForEach $invokers {
            $help = Get-Help $_ -Parameter AIScriptPolicy -ErrorAction SilentlyContinue
            $help | Should -Not -BeNullOrEmpty
            $text = ($help.description | Out-String)
            foreach ($mode in 'Prompt','AlwaysApprove','AlwaysWhatIf','Off') {
                $text | Should -Match $mode
            }
        }
    }

    Context 'AIScriptContext type is exported' {
        It 'exposes AIScriptContext for advanced callers' {
            # External callers that want to bypass the cmdlet-level policy setup
            # need to be able to instantiate AIScriptContext themselves.
            $t = 'PromptAI.Cmdlets.AIScriptContext' -as [type]
            $t | Should -Not -BeNullOrEmpty
        }
    }
}
