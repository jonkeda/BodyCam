using BodyCam.Agents;

namespace BodyCam.Services.Camera.Commands;

public sealed record ReadCommandOptions(
    ReadDetailLevel? DetailLevel,
    string? Focus);

public enum ReadDetailLevel
{
    Summary,
    Overview,
    Full,
}

public sealed class ReadCommand : CameraCommandBase<ReadCommandOptions>
{
    private readonly VisionAgent _vision;

    public ReadCommand(VisionAgent vision)
    {
        _vision = vision;
    }

    public override string Id => "read";
    public override string DisplayName => "Read";
    public override string? ToolName => "read_text";

    public override CameraCommandCapabilities Capabilities { get; } = new(
        SupportsFullAuto: true,
        SupportsManualAim: true,
        RequiresStillFrame: true,
        CanUseFrameStream: false,
        RequiresConfirmationForExternalActions: false);

    public override IReadOnlyList<CommandOptionDefinition> Options { get; } =
    [
        new(nameof(ReadCommandOptions.DetailLevel), typeof(ReadDetailLevel), ReadDetailLevel.Full, true),
        new(nameof(ReadCommandOptions.Focus), typeof(string), null, false),
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
        var text = await _vision.DescribeFrameAsync(frame, prompt, ct).ConfigureAwait(false);
        var transcript = IsNoText(text) ? "No text detected." : text;

        var data = BaseData(context, options);
        data["detail_level"] = options.DetailLevel.ToString();
        data["text"] = transcript;

        return new CameraCommandResult(
            Id,
            Success: true,
            TranscriptText: transcript,
            Data: data,
            Error: null);
    }

    public ReadCommandOptions ResolveOptions(CameraCommandContext context)
    {
        var supplied = TryReadOptions(context.Request);
        return new ReadCommandOptions(
            supplied?.DetailLevel ?? context.Settings.DefaultReadDetailLevel,
            Normalize(supplied?.Focus));
    }

    public static string BuildPrompt(ReadCommandOptions options)
    {
        var detail = options.DetailLevel ?? ReadDetailLevel.Full;
        var instruction = detail switch
        {
            ReadDetailLevel.Summary =>
                "Summarize what the visible text says. Keep it short and useful.",
            ReadDetailLevel.Overview =>
                "Explain the document, sign, label, menu, or screen type and the main visible sections.",
            ReadDetailLevel.Full =>
                "Read the visible text as completely and exactly as possible. Preserve wording and line breaks when useful.",
            _ =>
                "Read the visible text.",
        };

        var prompt = $"""
            Extract visible text from this image for a blind or visually impaired user.
            If no readable text is visible, respond with exactly: NO_TEXT.
            If the frame is blurry, dark, blocked, or confidence is low, say that before the text.

            Detail level: {detail}
            {instruction}
            """;

        if (!string.IsNullOrWhiteSpace(options.Focus))
            prompt += $"{Environment.NewLine}Focus on the {options.Focus}.";

        return prompt;
    }

    private static bool IsNoText(string text) =>
        string.IsNullOrWhiteSpace(text)
        || text.Contains("NO_TEXT", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
