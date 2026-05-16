# Version: 0.1.4

This release adds tool / function calling and AI-authored script execution across all five providers, on top of the new cmdlets and generation-control parameters.

## AI-authored scripts — `exec_powershell` + `-AIScriptPolicy`

Every `Invoke-X` cmdlet now exposes an implicit `exec_powershell` tool that lets the model write and execute PowerShell ad hoc, on the caller's session, with a built-in safety gate. The model decides what to run; the cmdlet decides whether to require human approval.

```powershell
# Read-only — auto-runs, no prompt:
Invoke-Claude "How many .log files are larger than 1 MB in C:\Logs?"

# State-modifying — WhatIf preview + Y/N/E/Q approval (default Prompt mode):
Invoke-GPT "Clean up .tmp files older than 7 days in $env:TEMP"

# CI / unattended — run everything, no prompts:
Invoke-Claude $prompt -AIScriptPolicy AlwaysApprove

# Strict — only tools passed via -Tool, never let AI write ad-hoc code:
Invoke-Claude $prompt -Tool $myTools -AIScriptPolicy Off
```

### How it works

1. The model emits a `exec_powershell { script, purpose }` tool call.
2. **AST static analysis** classifies the script. Read-only shapes (`Get-*`, `Select-*`, `Measure-*`, GET-only `Invoke-RestMethod`, etc.) auto-execute. Anything else — `Remove-*`, native executables, `.NET` method invocations, redirections, `Invoke-Expression`, non-GET HTTP — is flagged as state-modifying.
3. **State-modifying scripts** run first with `$WhatIfPreference = $true` to generate a preview, then the user sees the proposed script + purpose + detected risks + WhatIf output and chooses **Y**es / **N**o / **E**dit / **Q**uit.
4. On approve, the script re-runs without `-WhatIf`; the real output is returned to the model. On reject, the model receives a rejection notice and can try a different approach. On edit, the user's edited script runs.

### Policy modes

| `-AIScriptPolicy` | Behavior |
|---|---|
| `Prompt` (default) | Read-only auto-runs; state-modifying → WhatIf preview + Y/N/E/Q approval |
| `AlwaysApprove` | Execute everything without prompting (CI / trusted scripts) |
| `AlwaysWhatIf` | Never execute for real; preview only — exploration / dry-run mode |
| `Off` | Do not expose `exec_powershell`; the AI is restricted to `-Tool`-declared tools |

`$PSDefaultParameterValues['Invoke-*:AIScriptPolicy'] = 'Off'` in `$PROFILE` to opt out globally.

### Safety notes

- `exec_powershell` is mutually exclusive with `-Schema` (tool use already structures the output).
- The non-interactive prompt detection is lazy — read-only scripts auto-execute even in scripts/CI; only the actual approval gate (state-modifying script in `Prompt` mode) requires an interactive host. Set the policy explicitly in non-interactive contexts.
- Tool execution happens in the caller's session and inherits all imported modules, PSDrives, credentials, and authority. Treat it as if the AI had a console window in front of you.
- Static analysis is conservative — anything unrecognized is treated as state-modifying. A few cmdlets that bypass `-WhatIf` (file redirections, native exes, `.NET` invokes, `Invoke-Expression`) are explicitly flagged so the user is aware before approving.

### New types

- `AIScriptContext { Policy, Host, Runspace }` — runtime bundle threaded into each provider's tool loop.
- `ScriptAnalysis { IsReadOnly, HasWhatIfIncompatibleOps, Risks }` — result of `AIScriptAnalyzer.Analyze`.
- `ApprovalDecision { Approve, ApproveEdited, Reject, Quit }` — outcome of the approval UI.

## Tool / function calling — `-Tool`

Every `Invoke-X` cmdlet (Claude, GPT, Gemini, Llama, DeepSeek) now accepts `-Tool <Hashtable[]>`. Each tool declares `Name`, `Description`, `Parameters` (JSON Schema), and `Run` (a PowerShell scriptblock). When the model decides to call a tool, the scriptblock runs, the stringified result is fed back, and the model decides whether to call more tools or produce final text. Multi-tool turns, multi-round tool chains, and tool errors (the scriptblock throws → error is surfaced to the model, which is given the chance to self-correct) all work.

```powershell
$weather = @{
    Name        = 'get_weather'
    Description = 'Returns the current weather for a city.'
    Parameters  = @{
        type       = 'object'
        properties = @{ city = @{ type = 'string' } }
        required   = @('city')
    }
    Run = { param($a) Invoke-RestMethod "https://wttr.in/$($a.city)?format=j1" }
}

$r = Invoke-Claude "What's the weather in Tokyo?" -Tool $weather
$r.ToolCalls      # The invocations the model made this turn
$r.Text           # The model's final synthesis
```

- `-MaxToolIterations <int>` caps the tool loop (default 10) so a runaway agent stops.
- `-Tool` and `-Schema` are mutually exclusive — tool use already structures the output.
- New `AIResponse.ToolCalls` property: a list of `ToolCallRecord { Name, Arguments, Result, Error }`.

Per-provider wire mapping:

- **Anthropic**: `tools` field with `input_schema`; reads `tool_use` content blocks from the stream; sends `tool_result` back.
- **OpenAI / Groq / Meta / Together / DeepSeek**: `tools` with `{type:function, function:{name, description, parameters}}`; reads `delta.tool_calls`; sends `{role:tool, tool_call_id, content}` back. DeepSeek reasoner models emit `reasoning_content` (chain-of-thought) which the cmdlet captures and replays on subsequent iterations so the API does not reject the message.
- **Google Gemini**: `tools[].functionDeclarations`; reads `parts[].functionCall`; sends `parts[].functionResponse` back.

## New cmdlets

- **`Get-AIProvider`** — lists every provider this module knows about, whether the API key env var is set, and the default model. No network calls. Useful as a pre-flight check before sharing a script.
- **`Measure-AITokens`** — fast local token-count approximation (no network, no NuGet dep). ASCII chars / 4 + CJK chars / 2 + other / 3, rounded up. Documented as approximate; real tokenizers will disagree by 10–50%. `-Detailed` shows per-character-class breakdown.
- **`Export-AIConversation`** / **`Import-AIConversation`** — round-trip an `AIResponse` (which carries the full conversation in `.Turns`) to a JSON file, so a chat can resume across PowerShell sessions.

## New parameters on every Invoke-X

- **`-Temperature`**, **`-TopP`**, **`-StopSequence`** — generation control. Mapped to provider-specific fields (Anthropic `top_p` / `stop_sequences`, OpenAI `top_p` / `stop`, Gemini `topP` / `stopSequences`).
- **`-Json`** — request a JSON-only response. OpenAI/Llama/DeepSeek use `response_format=json_object`; Gemini uses `responseMimeType=application/json`; Claude uses an assistant-message prefill of `{`.
- **`-Schema <Hashtable>`** — pass a JSON Schema (as a PowerShell hashtable). OpenAI/Llama use `response_format=json_schema` strict mode; Gemini uses `responseSchema`. Claude/DeepSeek fall back to schema-in-system-prompt + JSON mode (best-effort, not strict).

## Custom output views

- `Get-AIProvider` and `Get-DeepSeekBalance` (the latter shipped in 0.1.3 but the view is the same shape) emit `PSObject` instances with custom TypeNames so a dedicated TableControl in `PromptAI.Format.ps1xml` formats them with auto-width columns.

## Quality gate

- Test suites run in `release.yml` before signing and PSGallery publish — a tagged release will not ship if any test fails.
- **xUnit: 138 tests** (up from the 0.1.3 baseline) — adds coverage for tool-call serialization/parsing, `AIResponse.ToolCalls`, `JsonHelpers.JsonObjectToHashtable`, `ParseTools` validation, `AIScriptAnalyzer` (read-only vs state-modifying classification, `-WhatIf`-incompatible op detection, `Invoke-RestMethod` method-sensitivity), and the `exec_powershell` schema + argument parsing.
- **Pester: 61 tests** — module surface, MAML-backed Get-Help, parameter validation, and `-AIScriptPolicy` presence/ValidateSet/help across all five `Invoke-X` cmdlets.

## Verifying the signed DLL

`PromptAI.dll` is Authenticode-signed by yotsuda's self-signed certificate.

```powershell
Get-AuthenticodeSignature (Join-Path (Get-Module PromptAI -ListAvailable).ModuleBase 'PromptAI.dll') |
    Format-List Status, SignerCertificate
```

Expected SHA-1 thumbprint: `74E5208228DFB12A067747D536BF497B6E98C73C`. See https://github.com/yotsuda/code-signing for trust setup.

# Version: 0.1.3

## New cmdlets

- **`Compare-AI`** — sends the same prompt to multiple providers in parallel (`Task.WhenAll`) and emits one `AIResponse` per provider. Default-Provider behavior queries every provider whose API key env var is set. Pipe to `Format-Table Provider, Model, Text, EstimatedCostUSD, Duration` for a side-by-side view.
- **`Get-DeepSeekBalance`** — returns the current DeepSeek API key balance via `GET /user/balance`. DeepSeek is currently the only provider with a public balance endpoint accessible to standard API keys.

## Existing cmdlets — new parameters

- **`-History <AIResponse>`** on every `Invoke-X`: pass an AIResponse from a prior call to continue the conversation. The returned AIResponse carries the full updated history in `.Turns`. The system prompt is inherited automatically — pass `-SystemPrompt` again only to override.
- **`-Image <string[]>`** on `Invoke-Claude`, `Invoke-GPT`, `Invoke-Gemini`, `Invoke-Llama`: attach one or more images (local file path or HTTPS URL) to the current turn. URL passthrough for Anthropic/OpenAI/Llama; lazy download + base64 inline for Gemini. DeepSeek's current models do not support image input.

## Enriched `AIResponse`

Every Invoke-X return value now includes:

| Property | Meaning |
|---|---|
| `InputTokens` / `OutputTokens` | Exact token usage extracted from the SSE stream. |
| `EstimatedCostUSD` | Best-effort cost from a hard-coded pricing table; null when the model isn't recognized. |
| `Duration` | Wall-clock duration of the API call. |
| `Turns` | Full conversation, including the new exchange. Pass back as `-History`. |
| `SystemPrompt` | The system prompt in effect for this turn — preserved across `-History` chaining unless overridden. |

Existing `.Text`, `.Model`, `.Provider`, and string-conversion behavior are unchanged — non-breaking addition.

## Robustness

- All SSE parsers now guard against unexpected JSON shapes (e.g., OpenAI's final `"usage": null` chunk that previously broke chunk parsing for GPT and DeepSeek).
- The SSE reader skips malformed chunks instead of aborting the whole stream.

## Quality gate

- New `Tests/` directory with xUnit unit tests (`Pricing`, `OpenAICompat`, `Claude` / `Gemini` parsers, `ImageLoader`, `AIResponse`) and Pester module-surface tests (cmdlet exports, `Get-Help` MAML, parameter validation).
- `release.yml` now runs both test suites before signing and PSGallery publish — a tagged release will not ship if any test fails.

## Verifying the signed DLL

`PromptAI.dll` is Authenticode-signed by yotsuda's self-signed certificate.

```powershell
Get-AuthenticodeSignature (Join-Path (Get-Module PromptAI -ListAvailable).ModuleBase 'PromptAI.dll') |
    Format-List Status, SignerCertificate
```

Expected SHA-1 thumbprint: `74E5208228DFB12A067747D536BF497B6E98C73C`. See https://github.com/yotsuda/code-signing for trust setup.

# Version: 0.1.2

## New cmdlets

- **`Invoke-Llama`** — sends prompts to Meta Llama models with `-Provider Groq|Meta|Together` switching between three OpenAI-compatible hosts. Default Groq (free tier). Tab completion for `-Model` is provider-aware.
- **`Invoke-DeepSeek`** — sends prompts to DeepSeek's official API (`api.deepseek.com`, OpenAI-compatible). Default model `deepseek-v4-flash`; `deepseek-v4-pro` also supported.

## Help system overhaul

- Help sources moved from `PromptAI/PlatyPS/en-US/` to top-level `docs/en-US/` for shorter, GitHub-friendly online URLs.
- Added the previously-missing `Invoke-Gemini` documentation (cmdlet shipped in 0.1.0 without help).
- Frontmatter `HelpUri` is now populated on every cmdlet, so `Get-Help <cmdlet> -Online` opens the GitHub-hosted page.
- Cross-links between every cmdlet are complete in `RELATED LINKS`.

## Build pipeline

- `Build.ps1` now invokes `Microsoft.PowerShell.PlatyPS` to compile MAML (`PromptAI.dll-Help.xml`) from `docs/<locale>/*.md` and deploys it next to the DLL under each locale folder.
- New GitHub Actions workflow at `.github/workflows/release.yml` triggers on `v*.*.*` tag push and handles version-consistency check, build, MAML compilation, Authenticode signing of `PromptAI.dll`, PSGallery publish, and GitHub Release creation from this file.

# Version: 0.1.0

Initial release. `Invoke-Claude`, `Invoke-GPT`, `Invoke-Gemini` with SSE streaming.
