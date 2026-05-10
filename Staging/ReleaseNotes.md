# Version: 0.1.3

## New cmdlets

- **`Compare-AI`** — sends the same prompt to multiple providers in parallel (`Task.WhenAll`) and emits one `AIResponse` per provider. Default-Provider behavior queries every provider whose API key env var is set. Pipe to `Format-Table Provider, Model, Text, EstimatedCostUSD, Duration` for a side-by-side view.
- **`Get-DeepSeekBalance`** — returns the current DeepSeek API key balance via `GET /user/balance`. DeepSeek is currently the only provider with a public balance endpoint accessible to standard API keys.

## Existing cmdlets — new parameters

- **`-History <AIResponse>`** on every `Invoke-X`: pass an AIResponse from a prior call to continue the conversation. The returned AIResponse carries the full updated history in `.Turns`.
- **`-Image <string[]>`** on `Invoke-Claude`, `Invoke-GPT`, `Invoke-Gemini`, `Invoke-Llama`: attach one or more images (local file path or HTTPS URL) to the current turn. URL passthrough for Anthropic/OpenAI/Llama; lazy download + base64 inline for Gemini. DeepSeek's current models do not support image input.

## Enriched `AIResponse`

Every Invoke-X return value now includes:

| Property | Meaning |
|---|---|
| `InputTokens` / `OutputTokens` | Exact token usage extracted from the SSE stream. |
| `EstimatedCostUSD` | Best-effort cost from a hard-coded pricing table; null when the model isn't recognized. |
| `Duration` | Wall-clock duration of the API call. |
| `Turns` | Full conversation, including the new exchange. Pass back as `-History`. |

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
