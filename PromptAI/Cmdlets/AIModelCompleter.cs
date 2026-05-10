using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Net.Http;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Base class for AI model argument completers.
/// Fetches available models from the API on first use and caches in memory.
/// Falls back to friendly aliases if the API key is not set or the call fails.
/// </summary>
public abstract class AIModelCompleter : IArgumentCompleter
{
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private string[]? _cachedModels;

    protected abstract string? GetApiKey();
    protected abstract string[] GetFallbackModels();
    protected abstract string[] FetchModelsFromAPI(string apiKey);

    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters)
    {
        var models = GetModels();
        var pattern = WildcardPattern.Get(
            string.IsNullOrEmpty(wordToComplete) ? "*" : $"{wordToComplete}*",
            WildcardOptions.IgnoreCase);

        foreach (var model in models)
        {
            if (pattern.IsMatch(model))
                yield return new CompletionResult(model);
        }
    }

    private string[] GetModels()
    {
        if (_cachedModels != null)
            return _cachedModels;

        var apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            _cachedModels = GetFallbackModels();
            return _cachedModels;
        }

        try
        {
            _cachedModels = FetchModelsFromAPI(apiKey);
        }
        catch
        {
            _cachedModels = GetFallbackModels();
        }

        return _cachedModels;
    }

    /// <summary>
    /// Helper: sends GET request and returns the response body.
    /// </summary>
    protected static string HttpGet(string url, Action<HttpRequestMessage> configureRequest)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        configureRequest(request);
        var response = s_httpClient.Send(request, HttpCompletionOption.ResponseContentRead);
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Completer for Invoke-Claude -Model parameter.
/// Fetches models from Anthropic GET /v1/models API.
/// </summary>
public class ClaudeModelCompleter : AIModelCompleter
{
    private static readonly string[] s_fallback =
        ["claude-opus-4-0-20250514", "claude-sonnet-4-20250514", "claude-haiku-4-5-20251001"];

    protected override string? GetApiKey()
        => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    protected override string[] GetFallbackModels() => s_fallback;

    protected override string[] FetchModelsFromAPI(string apiKey)
    {
        var body = HttpGet("https://api.anthropic.com/v1/models?limit=100", req =>
        {
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
        });

        using var doc = JsonDocument.Parse(body);
        var models = new List<string>();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    var modelId = id.GetString();
                    if (modelId != null)
                        models.Add(modelId);
                }
            }
        }

        return models.Count > 0 ? models.ToArray() : s_fallback;
    }
}

/// <summary>
/// Completer for Invoke-GPT -Model parameter.
/// Fetches models from OpenAI GET /v1/models API.
/// </summary>
public class GPTModelCompleter : AIModelCompleter
{
    private static readonly string[] s_fallback =
        ["gpt-4o", "gpt-4.1", "o3", "o4-mini"];

    protected override string? GetApiKey()
        => Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    protected override string[] GetFallbackModels() => s_fallback;

    protected override string[] FetchModelsFromAPI(string apiKey)
    {
        var body = HttpGet("https://api.openai.com/v1/models", req =>
        {
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
        });

        using var doc = JsonDocument.Parse(body);
        var models = new List<string>();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    var modelId = id.GetString();
                    if (modelId != null)
                        models.Add(modelId);
                }
            }
        }

        return models.Count > 0 ? models.ToArray() : s_fallback;
    }
}

/// <summary>
/// Completer for Invoke-Llama -Model parameter.
/// Provider-aware: reads -Provider from bound parameters and switches API/env accordingly.
/// Defaults to Groq when no -Provider is supplied.
/// </summary>
public class LlamaModelCompleter : IArgumentCompleter
{
    private static readonly Dictionary<string, string[]> s_fallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Groq"]     = ["llama-3.3-70b-versatile", "llama-3.1-8b-instant", "llama-3.2-90b-vision-preview"],
        ["Meta"]     = ["Llama-4-Maverick-17B-128E-Instruct-FP8", "Llama-4-Scout-17B-16E-Instruct-FP8", "Llama-3.3-70B-Instruct"],
        ["Together"] = ["meta-llama/Llama-3.3-70B-Instruct-Turbo", "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo"],
    };

    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters)
    {
        var provider = fakeBoundParameters["Provider"] as string ?? "Groq";
        if (!s_fallback.TryGetValue(provider, out var models))
            models = s_fallback["Groq"];

        var pattern = WildcardPattern.Get(
            string.IsNullOrEmpty(wordToComplete) ? "*" : $"{wordToComplete}*",
            WildcardOptions.IgnoreCase);

        foreach (var model in models)
        {
            if (pattern.IsMatch(model))
                yield return new CompletionResult(model);
        }
    }
}

/// <summary>
/// Completer for Invoke-DeepSeek -Model parameter.
/// Fetches models from DeepSeek GET /v1/models (OpenAI-compatible).
/// </summary>
public class DeepSeekModelCompleter : AIModelCompleter
{
    private static readonly string[] s_fallback = ["deepseek-v4-flash", "deepseek-v4-pro"];

    protected override string? GetApiKey()
        => Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");

    protected override string[] GetFallbackModels() => s_fallback;

    protected override string[] FetchModelsFromAPI(string apiKey)
    {
        var body = HttpGet("https://api.deepseek.com/models", req =>
        {
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
        });

        using var doc = JsonDocument.Parse(body);
        var models = new List<string>();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    var modelId = id.GetString();
                    if (modelId != null)
                        models.Add(modelId);
                }
            }
        }

        return models.Count > 0 ? models.ToArray() : s_fallback;
    }
}

/// <summary>
/// Completer for Invoke-Gemini -Model parameter.
/// Fetches models from Google GET /v1beta/models API.
/// </summary>
public class GeminiModelCompleter : AIModelCompleter
{
    private static readonly string[] s_fallback =
        ["gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash"];

    protected override string? GetApiKey()
        => Environment.GetEnvironmentVariable("GEMINI_API_KEY");

    protected override string[] GetFallbackModels() => s_fallback;

    protected override string[] FetchModelsFromAPI(string apiKey)
    {
        var body = HttpGet($"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}", req => { });

        using var doc = JsonDocument.Parse(body);
        var models = new List<string>();

        if (doc.RootElement.TryGetProperty("models", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name))
                {
                    // API returns "models/gemini-2.5-flash", strip the prefix
                    var modelId = name.GetString()?.Replace("models/", "");
                    if (modelId != null)
                        models.Add(modelId);
                }
            }
        }

        return models.Count > 0 ? models.ToArray() : s_fallback;
    }
}
