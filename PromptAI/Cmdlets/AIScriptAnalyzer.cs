using System.Management.Automation.Language;

namespace PromptAI.Cmdlets;

/// <summary>
/// Classification of a user-supplied (well, AI-supplied) PowerShell script.
/// Used by the exec_powershell tool to decide whether to auto-execute (pure
/// read-only) or require human approval (anything that might mutate state).
/// </summary>
public record ScriptAnalysis(
    bool IsReadOnly,
    bool HasWhatIfIncompatibleOps,
    IReadOnlyList<string> Risks);

/// <summary>
/// Static analysis of an AI-proposed PowerShell script. Conservative by
/// default: anything unrecognized is treated as potentially state-modifying.
/// `-WhatIf` only covers SupportsShouldProcess cmdlets, so we additionally
/// flag native executables, .NET method invocations, dynamic-eval cmdlets,
/// non-GET REST calls, and write-redirections that bypass -WhatIf.
/// </summary>
public static class AIScriptAnalyzer
{
    // PowerShell verbs that are inherently read-only — their side effects are
    // limited to producing output objects, not changing system state. If every
    // CommandAst in the script uses one of these verbs (and there are no
    // native exes, no .NET invokes, no Invoke-Expression, no write redirections,
    // and no Invoke-RestMethod with a write method), the script auto-executes.
    private static readonly HashSet<string> ReadOnlyVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get", "Select", "Where", "Sort", "Group", "Compare", "Measure",
        "ForEach", "Find", "Search", "Test", "Resolve", "Read",
        "Format", "Out", "ConvertTo", "ConvertFrom", "Show", "Trace",
    };

    // Cmdlets that despite their verb shape are dynamic-execution / dangerous
    // and must always require approval.
    private static readonly HashSet<string> AlwaysReviewCmdlets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Invoke-Expression", "iex",
        "Invoke-Command",                       // remote / scriptblock execution
        "Add-Type",                             // can compile arbitrary C#
        "Start-Process",                        // launches anything
        "Out-File", "Set-Content", "Add-Content", "Tee-Object",
        "Export-Csv", "Export-Clixml", "Export-PSSession",
    };

    public static ScriptAnalysis Analyze(string script)
    {
        var risks = new List<string>();

        Token[]? tokens;
        ParseError[]? errors;
        var ast = Parser.ParseInput(script, out tokens, out errors);

        if (errors != null && errors.Length > 0)
        {
            // A script that doesn't even parse is suspicious — return as
            // not-read-only so the user sees what's wrong before running.
            foreach (var e in errors)
                risks.Add($"Parse error: {e.Message} at line {e.Extent.StartLineNumber}");
            return new ScriptAnalysis(IsReadOnly: false, HasWhatIfIncompatibleOps: false, risks);
        }

        bool readOnly = true;
        bool whatIfIncompatible = false;

        // Native commands (external .exe) — never honor -WhatIf.
        foreach (var cmd in ast.FindAll(a => a is CommandAst, true).Cast<CommandAst>())
        {
            var nameElement = cmd.CommandElements.Count > 0 ? cmd.CommandElements[0] : null;
            var name = (nameElement as StringConstantExpressionAst)?.Value
                     ?? nameElement?.Extent.Text
                     ?? "(unknown)";

            if (cmd.InvocationOperator == TokenKind.Ampersand && IsLikelyNative(name))
            {
                readOnly = false;
                whatIfIncompatible = true;
                risks.Add($"External executable invocation: '{name}' — -WhatIf will not stop it");
                continue;
            }

            if (AlwaysReviewCmdlets.Contains(name))
            {
                readOnly = false;
                if (string.Equals(name, "Invoke-Expression", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "iex", StringComparison.OrdinalIgnoreCase))
                {
                    whatIfIncompatible = true;
                    risks.Add($"Dynamic evaluation: '{name}' — content is opaque to static analysis");
                }
                else
                {
                    risks.Add($"State-changing cmdlet: '{name}'");
                }
                continue;
            }

            // Verb-Noun read-only?
            var dashIdx = name.IndexOf('-');
            if (dashIdx > 0)
            {
                // Invoke-RestMethod / Invoke-WebRequest are GET-by-default and harmless
                // when used to fetch data, but become writes when a non-GET method is
                // supplied. The verb "Invoke" is not in the generic read-only allow-list
                // (Invoke-Expression and Invoke-Command must remain flagged), so we
                // handle these two cmdlets explicitly before the verb-based check.
                if (string.Equals(name, "Invoke-RestMethod", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Invoke-WebRequest", StringComparison.OrdinalIgnoreCase))
                {
                    if (HasWriteHttpMethod(cmd))
                    {
                        readOnly = false;
                        whatIfIncompatible = true;
                        risks.Add($"HTTP write request: '{name}' with non-GET method — -WhatIf does not apply");
                    }
                    continue;
                }

                var verb = name.Substring(0, dashIdx);
                if (!ReadOnlyVerbs.Contains(verb))
                {
                    readOnly = false;
                    risks.Add($"Cmdlet with non-read verb: '{name}'");
                }
            }
            else if (!string.IsNullOrEmpty(name) && !name.StartsWith('$') && !name.StartsWith('@'))
            {
                // A bare token like "git" or "rm" is almost certainly native.
                readOnly = false;
                whatIfIncompatible = true;
                risks.Add($"External executable invocation: '{name}' — -WhatIf will not stop it");
            }
        }

        // .NET method invocations like [IO.File]::Delete(...) — opaque, can mutate.
        foreach (var inv in ast.FindAll(a => a is InvokeMemberExpressionAst, true).Cast<InvokeMemberExpressionAst>())
        {
            readOnly = false;
            var name = inv.Member is StringConstantExpressionAst sce ? sce.Value : inv.Member.Extent.Text;
            risks.Add($".NET method invocation: '{inv.Expression.Extent.Text}::{name}()' — -WhatIf does not apply");
            whatIfIncompatible = true;
        }

        // Output redirections > >> 2> etc. — file writes that bypass -WhatIf.
        foreach (var redir in ast.FindAll(a => a is FileRedirectionAst, true).Cast<FileRedirectionAst>())
        {
            readOnly = false;
            whatIfIncompatible = true;
            risks.Add($"File redirection: '{redir.Extent.Text}' — bypasses -WhatIf");
        }

        // Variable assignments to system streams (rare but possible) are not flagged here;
        // ordinary `$x = 1` assignments are common in read-only scripts.

        return new ScriptAnalysis(readOnly, whatIfIncompatible, risks);
    }

    // Heuristic: if the name has no dash and looks like a path or filename, it is a
    // native command. We deliberately err on the side of "review needed".
    private static bool IsLikelyNative(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Contains('/') || name.Contains('\\')) return true;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return true;
        // git, kubectl, az, gcloud, aws, npm, dotnet, docker, ... no dash, not a PS verb.
        return name.IndexOf('-') < 0;
    }

    private static bool HasWriteHttpMethod(CommandAst cmd)
    {
        // Look for -Method <value> where value is anything other than GET.
        for (var i = 1; i < cmd.CommandElements.Count - 1; i++)
        {
            if (cmd.CommandElements[i] is CommandParameterAst p &&
                string.Equals(p.ParameterName, "Method", StringComparison.OrdinalIgnoreCase))
            {
                var value = cmd.CommandElements[i + 1];
                var method = (value as StringConstantExpressionAst)?.Value ?? value.Extent.Text;
                method = method.Trim('"', '\'');
                return !string.Equals(method, "Get", StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }
}
