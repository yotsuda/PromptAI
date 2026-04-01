# PromptAI

Call AI models from PowerShell with real-time streaming. Works with [PowerShell.MCP](https://github.com/yotsuda/PowerShell.MCP) for AI-to-AI communication.

```powershell
Invoke-Claude "Explain PowerShell pipelines in one paragraph."
Invoke-GPT "What is the best sorting algorithm?"
Invoke-Gemini "Translate to Japanese: Hello World"
```

Tokens stream to the console as they arrive. No waiting for the full response.

## Install

```powershell
Install-Module PromptAI
```

Requires PowerShell 7.4+.

## Setup

Set API keys for the providers you use. Restart PowerShell after running `setx`.

```powershell
setx ANTHROPIC_API_KEY "sk-ant-..."   # for Invoke-Claude
setx OPENAI_API_KEY "sk-..."          # for Invoke-GPT
setx GEMINI_API_KEY "..."             # for Invoke-Gemini
```

## Cmdlets

| Cmdlet | Provider | Default Model |
|---|---|---|
| `Invoke-Claude` | Anthropic | claude-sonnet-4-20250514 |
| `Invoke-GPT` | OpenAI | gpt-4o |
| `Invoke-Gemini` | Google | gemini-2.5-flash |

## Usage

### Basic

```powershell
Invoke-Claude "What is 2+2?"
```

### Specify a model

```powershell
Invoke-Claude -Model claude-opus-4-0-20250514 "Write a detailed analysis."
```

Tab completion is supported for `-Model` — available models are fetched from the API.

### System prompt

```powershell
Invoke-Claude "List running services" -SystemPrompt "You are a Windows admin. Be concise."
```

### Pipeline input

```powershell
Get-Content error.log | Invoke-Claude "Summarize the errors in this log."
```

### Capture result

```powershell
$result = Invoke-Claude "What is the capital of France?"
$result.Text    # "Paris"
"$result"       # "Paris" (implicit string conversion)
```

### Compare models

```powershell
$claude = Invoke-Claude "What is the best programming language?"
$gpt = Invoke-GPT "What is the best programming language?"
```

## Output

All cmdlets return an `AIResponse` object with:

- `.Text` — the response text
- `.Model` — the model used
- `.Provider` — the provider name
- Implicit string conversion via `ToString()`

## PowerShell.MCP Integration

When used with [PowerShell.MCP](https://github.com/yotsuda/PowerShell.MCP), AI agents can call these cmdlets through the MCP console. The response is automatically included in the MCP tool output.

```powershell
# Install both modules
Install-Module PowerShell.MCP
Install-Module PromptAI
```

This enables AI-to-AI communication — an AI agent running in Claude Code can call `Invoke-GPT` to get a second opinion from GPT, or `Invoke-Gemini` to cross-check with Gemini.

## License

[MIT](LICENSE)
