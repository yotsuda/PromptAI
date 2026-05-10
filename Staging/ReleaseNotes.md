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

# Version: 0.1.1

## Help system

- Added MAML-based help for `Invoke-Claude`, `Invoke-GPT`, and `Invoke-Gemini`.
- `Get-Help -Online` is supported via populated `HelpUri`.

# Version: 0.1.0

Initial release. `Invoke-Claude`, `Invoke-GPT`, `Invoke-Gemini` with SSE streaming.
