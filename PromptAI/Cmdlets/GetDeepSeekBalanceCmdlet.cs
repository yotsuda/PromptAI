using System.Management.Automation;
using System.Net.Http;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Queries the DeepSeek user balance endpoint and returns one PSCustomObject per
/// currency. DeepSeek is currently the only provider in this module that exposes
/// a public balance API to standard API keys; other providers require admin keys
/// or expose billing only via their consoles.
/// </summary>
[Cmdlet(VerbsCommon.Get, "DeepSeekBalance")]
[OutputType(typeof(PSObject))]
public class GetDeepSeekBalanceCmdlet : PSCmdlet
{
    protected override void EndProcessing()
    {
        var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? throw new PSInvalidOperationException("DEEPSEEK_API_KEY environment variable is not set.");

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        var response = AIStreamingCmdletBase.s_httpClient.Send(request, HttpCompletionOption.ResponseContentRead);
        AIStreamingCmdletBase.EnsureSuccess(response, "Get-DeepSeekBalance");

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        bool isAvailable = root.TryGetProperty("is_available", out var avail) && avail.GetBoolean();

        if (!root.TryGetProperty("balance_infos", out var infos) || infos.GetArrayLength() == 0)
        {
            WriteWarning("DeepSeek returned no balance entries.");
            return;
        }

        foreach (var entry in infos.EnumerateArray())
        {
            var obj = new PSObject();
            // Custom TypeName so the format engine picks the dedicated TableControl
            // in PromptAI.Format.ps1xml. IsAvailable is excluded from the default
            // view (always True in practice); Format-List exposes it.
            obj.TypeNames.Insert(0, "PromptAI.Cmdlets.DeepSeekBalance");
            obj.Properties.Add(new PSNoteProperty("Currency",        entry.GetProperty("currency").GetString()));
            obj.Properties.Add(new PSNoteProperty("TotalBalance",    decimal.Parse(entry.GetProperty("total_balance").GetString()!)));
            obj.Properties.Add(new PSNoteProperty("GrantedBalance",  decimal.Parse(entry.GetProperty("granted_balance").GetString()!)));
            obj.Properties.Add(new PSNoteProperty("ToppedUpBalance", decimal.Parse(entry.GetProperty("topped_up_balance").GetString()!)));
            obj.Properties.Add(new PSNoteProperty("IsAvailable",     isAvailable));
            WriteObject(obj);
        }
    }
}
