using System.Management.Automation;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PromptAI.Cmdlets;

/// <summary>
/// Serializes a single AIResponse (which carries the full conversation history
/// in .Turns) to a JSON file. Reverse of Import-AIConversation. Useful for
/// resuming a conversation across PowerShell sessions.
/// </summary>
[Cmdlet(VerbsData.Export, "AIConversation")]
[OutputType(typeof(void))]
public class ExportAIConversationCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public AIResponse InputObject { get; set; } = null!;

    [Parameter(Mandatory = true, Position = 1)]
    public string Path { get; set; } = null!;

    [Parameter]
    public SwitchParameter Force { get; set; }

    private AIResponse? _toExport;

    protected override void ProcessRecord()
    {
        if (_toExport != null)
            throw new PSInvalidOperationException(
                "Export-AIConversation accepts a single AIResponse. Pass the latest one — it already carries the full conversation in .Turns.");
        _toExport = InputObject;
    }

    protected override void EndProcessing()
    {
        if (_toExport == null) return;

        var resolved = GetUnresolvedProviderPathFromPSPath(Path);
        if (System.IO.File.Exists(resolved) && !Force.IsPresent)
            throw new PSInvalidOperationException($"File exists: {resolved}. Use -Force to overwrite.");

        var dto = ConversationDto.From(_toExport);
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var json = JsonSerializer.Serialize(dto, opts);
        System.IO.File.WriteAllText(resolved, json);
    }
}

internal record ConversationDto(
    string Text,
    string Model,
    string Provider,
    int InputTokens,
    int OutputTokens,
    decimal? EstimatedCostUSD,
    double DurationSeconds,
    string? SystemPrompt,
    List<ConversationTurn> Turns,
    string SchemaVersion = "1")
{
    public static ConversationDto From(AIResponse r) => new(
        Text: r.Text,
        Model: r.Model,
        Provider: r.Provider,
        InputTokens: r.InputTokens,
        OutputTokens: r.OutputTokens,
        EstimatedCostUSD: r.EstimatedCostUSD,
        DurationSeconds: r.Duration.TotalSeconds,
        SystemPrompt: r.SystemPrompt,
        Turns: r.Turns.ToList());

    public AIResponse ToResponse() => new(
        text: Text,
        model: Model,
        provider: Provider,
        inputTokens: InputTokens,
        outputTokens: OutputTokens,
        estimatedCostUSD: EstimatedCostUSD,
        duration: TimeSpan.FromSeconds(DurationSeconds),
        turns: Turns,
        systemPrompt: SystemPrompt);
}
