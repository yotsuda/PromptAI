using System.Management.Automation;
using System.Text.Json;

namespace PromptAI.Cmdlets;

/// <summary>
/// Reads a conversation JSON file written by Export-AIConversation and emits an
/// AIResponse so the caller can pass it as -History to a subsequent Invoke-X.
/// </summary>
[Cmdlet(VerbsData.Import, "AIConversation")]
[OutputType(typeof(AIResponse))]
public class ImportAIConversationCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    public string Path { get; set; } = null!;

    protected override void ProcessRecord()
    {
        var resolved = GetUnresolvedProviderPathFromPSPath(Path);
        if (!System.IO.File.Exists(resolved))
            throw new System.IO.FileNotFoundException($"Conversation file not found: {resolved}", resolved);

        var json = System.IO.File.ReadAllText(resolved);
        var dto  = JsonSerializer.Deserialize<ConversationDto>(json)
            ?? throw new PSInvalidOperationException($"Failed to parse conversation file: {resolved}");

        WriteObject(dto.ToResponse());
    }
}
