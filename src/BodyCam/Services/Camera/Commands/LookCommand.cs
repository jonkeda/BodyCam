using BodyCam.Agents;

namespace BodyCam.Services.Camera.Commands;

public sealed record LookCommandOptions(
    LookDetailLevel? DetailLevel,
    string? Focus,
    string? Question);

public enum LookDetailLevel
{
    Summary,
    Overview,
    Detailed,
    Full,
}

public sealed class LookCommand : CameraCommandBase<LookCommandOptions>
{
    private readonly VisionAgent _vision;

    public LookCommand(VisionAgent vision)
    {
        _vision = vision;
    }

    public override string Id => "look";
    public override string DisplayName => "Look";
    public override string? ToolName => "look";

    public override CameraCommandCapabilities Capabilities { get; } = new(
        SupportsFullAuto: true,
        SupportsManualAim: true,
        RequiresStillFrame: true,
        CanUseFrameStream: false,
        RequiresConfirmationForExternalActions: false);

    public override IReadOnlyList<CommandOptionDefinition> Options { get; } =
    [
        new(nameof(LookCommandOptions.DetailLevel), typeof(LookDetailLevel), LookDetailLevel.Summary, true),
        new(nameof(LookCommandOptions.Focus), typeof(string), null, false),
        new(nameof(LookCommandOptions.Question), typeof(string), null, false),
    ];

    public override async Task<CameraCommandResult> ExecuteAsync(
        CameraCommandContext context,
        CancellationToken ct)
    {
        var options = ResolveOptions(context);
        var frame = await CaptureFrameForModeAsync(context, ct).ConfigureAwait(false);
        if (frame is null)
            return CameraUnavailable(Id);

        var prompt = BuildPrompt(options);
        var description = await _vision.DescribeFrameAsync(frame, prompt, ct).ConfigureAwait(false);

        var data = BaseData(context, options);
        data["detail_level"] = options.DetailLevel.ToString();
        data["description"] = description;

        return new CameraCommandResult(
            Id,
            Success: true,
            TranscriptText: description,
            Data: data,
            Error: null);
    }

    public LookCommandOptions ResolveOptions(CameraCommandContext context)
    {
        var supplied = TryReadOptions(context.Request);
        return new LookCommandOptions(
            supplied?.DetailLevel ?? context.Settings.DefaultLookDetailLevel,
            Normalize(supplied?.Focus),
            Normalize(supplied?.Question ?? context.Request.Query));
    }

    public static string BuildPrompt(LookCommandOptions options)
    {
        var detail = options.DetailLevel ?? LookDetailLevel.Summary;
        var detailInstructions = detail switch
        {
            LookDetailLevel.Summary => """
                Give the shortest useful answer in one or two sentences.
                Lead with the main thing or direct answer.
                If there is any immediate hazard, mention it first.
                """,
            LookDetailLevel.Overview => """
                Give an orientation-first overview that is easy to listen to while moving.
                Include people, obstacles, entrances, exits, signs, and major objects.
                Use spatial language such as left, right, ahead, above, below, near, and far.
                """,
            LookDetailLevel.Detailed => """
                Give a structured scene description.
                Include important objects, relationships, visible text snippets, confidence, uncertainty, and possible next actions.
                Use consistent spatial language such as left, right, ahead, above, below, near, and far.
                """,
            LookDetailLevel.Full => """
                Give the most complete reasonable description of the frame.
                Include visible text, layout, colors, object details, hazards, and uncertainty.
                Avoid claims that are not supported by the image.
                """,
            _ => "Describe the scene concisely.",
        };

        var prompt = $"""
            You are helping a blind or visually impaired user understand a camera frame.
            Safety-relevant observations come first.
            Say when the image appears dark, blurry, blocked, too close, too far away, or ambiguous.
            Do not infer hidden facts or identities.

            Detail level: {detail}
            {detailInstructions}
            """;

        if (!string.IsNullOrWhiteSpace(options.Focus))
            prompt += $"{Environment.NewLine}Pay particular attention to: {options.Focus}.";

        if (!string.IsNullOrWhiteSpace(options.Question))
            prompt += $"{Environment.NewLine}Answer this question first if the image supports it: {options.Question}";

        return prompt;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
