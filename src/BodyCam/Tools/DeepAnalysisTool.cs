using System.ComponentModel;
using BodyCam.Agents;

namespace BodyCam.Tools;

public class DeepAnalysisArgs
{
    [Description("The question or topic to analyze in depth")]
    public string Query { get; set; } = "";

    [Description("Additional context to include in the analysis")]
    public string? Context { get; set; }
}

public class DeepAnalysisTool : ToolBase<DeepAnalysisArgs>
{
    private readonly ConversationAgent _conversation;

    public override string Name => "deep_analysis";
    public override string Description =>
        "Perform deep analysis using a reasoning model. " +
        "Use for complex questions that need careful thought, multi-step reasoning, " +
        "or when the user explicitly asks for detailed analysis.";

    public DeepAnalysisTool(ConversationAgent conversation)
    {
        _conversation = conversation;
    }

    protected override async Task<ToolResult> ExecuteAsync(
        DeepAnalysisArgs args, ToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Query))
            return ToolResult.Fail("Query is required for deep analysis.");

        var result = await _conversation.AnalyzeAsync(args.Query, args.Context);
        return ToolResult.Success(new { analysis = result });
    }
}
