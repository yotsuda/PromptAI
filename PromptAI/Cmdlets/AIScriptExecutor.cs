using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Text;

namespace PromptAI.Cmdlets;

/// <summary>
/// Decision returned by the approval UI.
/// </summary>
public enum ApprovalDecision
{
    Approve,
    ApproveEdited,
    Reject,
    Quit,
}

/// <summary>
/// Drives the exec_powershell tool's full lifecycle: analyze → run with -WhatIf
/// (if state-modifying) → prompt user → on approve, run for real (or run the
/// edited version) → return the real output to the model. Read-only scripts
/// skip the entire approval dance and execute immediately.
/// </summary>
public static class AIScriptExecutor
{
    /// <summary>
    /// The single public entry point. Returns the string that will be sent
    /// back to the AI as the tool_result for this exec_powershell invocation.
    /// May throw a PipelineStoppedException if the user picks Quit.
    /// </summary>
    public static string ExecuteWithPolicy(
        string script,
        string purpose,
        string policy,
        PSHost host,
        Runspace runspace)
    {
        var analysis = AIScriptAnalyzer.Analyze(script);

        // Off mode is handled at the cmdlet layer (the tool isn't even exposed).
        // Here we still validate policy enum so misuse fails loud.
        bool alwaysApprove = string.Equals(policy, "AlwaysApprove", StringComparison.OrdinalIgnoreCase);
        bool alwaysWhatIf  = string.Equals(policy, "AlwaysWhatIf",  StringComparison.OrdinalIgnoreCase);
        bool prompt        = string.Equals(policy, "Prompt",        StringComparison.OrdinalIgnoreCase);

        if (!alwaysApprove && !alwaysWhatIf && !prompt)
        {
            return $"ERROR: invalid AIScriptPolicy '{policy}'";
        }

        // Fast path: read-only and policy is Prompt or AlwaysApprove → just run.
        if (analysis.IsReadOnly && !alwaysWhatIf)
        {
            return RunScript(script, whatIf: false, runspace);
        }

        // AlwaysApprove non-read-only: run real (no approval) but log a warning.
        if (alwaysApprove)
        {
            host.UI.WriteWarningLine("[exec_powershell] AlwaysApprove policy — running state-modifying script without prompt.");
            return RunScript(script, whatIf: false, runspace);
        }

        // AlwaysWhatIf: run with WhatIf only, never the real thing. Some
        // cmdlets emit WhatIf messages via the host (not a captured stream)
        // so we additionally surface the detected operations from the AST
        // analyzer — that gives the model something concrete to reason about
        // even when the underlying cmdlet's preview output is host-only.
        if (alwaysWhatIf)
        {
            var whatIfOutput = RunScript(script, whatIf: true, runspace);
            var sb = new StringBuilder();
            sb.AppendLine("[WhatIf preview only — real execution skipped per AIScriptPolicy=AlwaysWhatIf]");
            sb.AppendLine("[script]");
            sb.AppendLine(script);
            if (analysis.Risks.Count > 0)
            {
                sb.AppendLine("[detected operations]");
                foreach (var r in analysis.Risks) sb.AppendLine("  • " + r);
            }
            sb.AppendLine("[WhatIf output captured]");
            sb.Append(whatIfOutput);
            return sb.ToString();
        }

        // Prompt mode: WhatIf → show user → approval UI → on approve, real run.
        string whatIfPreview;
        try
        {
            whatIfPreview = RunScript(script, whatIf: true, runspace);
        }
        catch (Exception ex)
        {
            whatIfPreview = $"(WhatIf preview itself errored: {ex.Message})";
        }

        RenderProposal(host, script, purpose, whatIfPreview, analysis);

        var decision = PromptDecision(host, analysis);
        switch (decision)
        {
            case ApprovalDecision.Approve:
                return RunScript(script, whatIf: false, runspace);

            case ApprovalDecision.ApproveEdited:
                var edited = OpenEditor(host, script);
                if (string.IsNullOrWhiteSpace(edited))
                {
                    return "User cancelled the edit. No script was executed.";
                }
                var editedResult = RunScript(edited, whatIf: false, runspace);
                return "Note: user edited the script before approving.\n[script]\n" + edited + "\n[result]\n" + editedResult;

            case ApprovalDecision.Reject:
                return "User rejected this script. Try a different approach or ask the user for clarification.";

            case ApprovalDecision.Quit:
                throw new PipelineStoppedException();
        }

        return "ERROR: unreachable approval decision branch";
    }

    /// <summary>
    /// Runs the supplied script in the caller's current session, optionally
    /// with $WhatIfPreference forced to $true. We MUST use ScriptBlock.Invoke
    /// (not a fresh PowerShell.Create() bound to the same runspace) because
    /// the calling cmdlet still occupies that runspace — re-entering it
    /// deadlocks. ScriptBlock.Invoke runs synchronously in the current
    /// session state and observes the same modules, PSDrives, and variables.
    /// All streams are merged via `*>&1` so warnings, errors, and WhatIf
    /// messages reach the model alongside the success output.
    /// </summary>
    private static string RunScript(string script, bool whatIf, Runspace runspace)
    {
        // `& { ... }` opens a child scope so the $WhatIfPreference assignment
        // does not leak to the caller's session. `*>&1` merges every stream
        // (success, error, warning, verbose, information) so a single
        // Out-String round-trip can stringify the lot.
        var wrapped = whatIf
            ? $"& {{ $WhatIfPreference = $true\n{script} *>&1 }} | Out-String"
            : $"& {{ {script} *>&1 }} | Out-String";

        try
        {
            var sb = ScriptBlock.Create(wrapped);
            var results = sb.Invoke();

            var output = new StringBuilder();
            foreach (var r in results)
            {
                if (r == null) continue;
                output.Append(r.ToString());
            }
            var text = output.ToString().TrimEnd();
            return string.IsNullOrEmpty(text) ? "(no output)" : text;
        }
        catch (Exception ex)
        {
            return $"[exception]\n{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static void RenderProposal(PSHost host, string script, string purpose, string whatIfPreview, ScriptAnalysis analysis)
    {
        host.UI.WriteLine();
        host.UI.WriteLine(ConsoleColor.Cyan, ConsoleColor.Black,
            "╭─ AI proposes script " + new string('─', Math.Max(0, 50)) + "╮");
        host.UI.WriteLine($"  Purpose: {purpose}");
        host.UI.WriteLine();
        foreach (var line in script.Split('\n'))
        {
            host.UI.WriteLine("    " + line.TrimEnd('\r'));
        }
        host.UI.WriteLine(ConsoleColor.Cyan, ConsoleColor.Black,
            "╰" + new string('─', 72) + "╯");

        if (analysis.Risks.Count > 0)
        {
            host.UI.WriteLine();
            host.UI.WriteLine(ConsoleColor.Yellow, ConsoleColor.Black, "[risks detected]");
            foreach (var r in analysis.Risks)
            {
                host.UI.WriteLine(ConsoleColor.Yellow, ConsoleColor.Black, "  • " + r);
            }
            if (analysis.HasWhatIfIncompatibleOps)
            {
                host.UI.WriteLine(ConsoleColor.Yellow, ConsoleColor.Black,
                    "  ⚠ Some operations bypass -WhatIf — the preview below may NOT reflect what real execution does.");
            }
        }

        host.UI.WriteLine();
        host.UI.WriteLine(ConsoleColor.DarkGray, ConsoleColor.Black, "[WhatIf preview]");
        foreach (var line in whatIfPreview.Split('\n'))
        {
            host.UI.WriteLine("  " + line.TrimEnd('\r'));
        }
        host.UI.WriteLine();
    }

    private static ApprovalDecision PromptDecision(PSHost host, ScriptAnalysis analysis)
    {
        // PromptForChoice gives a proper one-key prompt. Order matters: we
        // make Reject the default (first) so a stray Enter does not approve.
        var choices = new Collection<ChoiceDescription>
        {
            new("&No",      "Reject — return rejection to the AI so it can try a different approach"),
            new("&Yes",     "Approve and execute the script for real"),
            new("&Edit",    "Open the script in an editor, then approve the edited version"),
            new("&Quit",    "Abort the entire AI call"),
        };

        try
        {
            var idx = host.UI.PromptForChoice(
                caption: "Approve script execution?",
                message: "Review the proposal above. The script will run in your current session.",
                choices: choices,
                defaultChoice: 0);

            return idx switch
            {
                1 => ApprovalDecision.Approve,
                2 => ApprovalDecision.ApproveEdited,
                3 => ApprovalDecision.Quit,
                _ => ApprovalDecision.Reject,
            };
        }
        catch (Exception ex) when (
            ex is PSInvalidOperationException ||
            ex is NotImplementedException ||
            ex is System.Management.Automation.Host.HostException)
        {
            // The host cannot display an interactive prompt (background job,
            // non-interactive remoting, redirected stdin, etc.). Default to
            // Reject so the AI receives a clear rejection rather than the
            // call hanging or crashing.
            throw new PSInvalidOperationException(
                "AIScriptPolicy=Prompt requires an interactive host, but the current host cannot display prompts. " +
                "Set -AIScriptPolicy to AlwaysApprove, AlwaysWhatIf, or Off for non-interactive sessions. " +
                $"(Original error: {ex.Message})");
        }
    }

    private static string OpenEditor(PSHost host, string initial)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"promptai-edit-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(tmp, initial);

        var editor = Environment.GetEnvironmentVariable("EDITOR");
        if (string.IsNullOrWhiteSpace(editor))
        {
            editor = OperatingSystem.IsWindows() ? "notepad" : "vi";
        }

        try
        {
            host.UI.WriteLine($"Opening editor '{editor}' on {tmp} — save and close to continue.");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName        = editor,
                Arguments       = $"\"{tmp}\"",
                UseShellExecute = false,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit();
            return File.ReadAllText(tmp);
        }
        catch (Exception ex)
        {
            host.UI.WriteWarningLine($"Editor invocation failed: {ex.Message}");
            return string.Empty;
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* best-effort cleanup */ }
        }
    }
}
