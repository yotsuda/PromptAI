using System.Management.Automation;

namespace PromptAI.Cmdlets;

/// <summary>
/// Lists every provider this module knows about, whether the API key environment
/// variable is set, and the default model the corresponding Invoke-X cmdlet uses.
/// Strictly local — no network calls. Useful as a pre-flight check before running
/// Compare-AI or before sharing a script that assumes certain keys are present.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AIProvider")]
[OutputType(typeof(PSObject))]
public class GetAIProviderCmdlet : PSCmdlet
{
    private static readonly (string Name, string EnvVar, string DefaultModel)[] s_providers =
    [
        ("Claude",   "ANTHROPIC_API_KEY", "claude-sonnet-4-20250514"),
        ("GPT",      "OPENAI_API_KEY",    "gpt-4o"),
        ("Gemini",   "GEMINI_API_KEY",    "gemini-2.5-flash"),
        ("Llama",    "GROQ_API_KEY",      "llama-3.3-70b-versatile (Groq default; Meta/Together available via -Provider)"),
        ("DeepSeek", "DEEPSEEK_API_KEY",  "deepseek-v4-flash"),
    ];

    protected override void EndProcessing()
    {
        foreach (var (name, envVar, defaultModel) in s_providers)
        {
            var key = Environment.GetEnvironmentVariable(envVar);
            var configured = !string.IsNullOrEmpty(key);

            var obj = new PSObject();
            obj.TypeNames.Insert(0, "PromptAI.Cmdlets.AIProvider");
            obj.Properties.Add(new PSNoteProperty("Name",         name));
            obj.Properties.Add(new PSNoteProperty("EnvVar",       envVar));
            obj.Properties.Add(new PSNoteProperty("IsConfigured", configured));
            obj.Properties.Add(new PSNoteProperty("DefaultModel", defaultModel));
            obj.Properties.Add(new PSNoteProperty("KeyPrefix",    configured ? key!.Substring(0, Math.Min(4, key.Length)) : null));
            WriteObject(obj);
        }
    }
}
