using System.Diagnostics;
using System.Net.Http;
using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Services.AiProviders;

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

public sealed class LookCommandPrompts
{
    public CommandPromptDefinition Summary { get; init; } = new(
        nameof(LookDetailLevel.Summary),
        "Summary",
        "Look. Give a short summary.",
        """
        You are helping a blind or visually impaired user understand a camera frame.
        Safety-relevant observations come first.
        Say when the image appears dark, blurry, blocked, too close, too far away, or ambiguous.
        Do not infer hidden facts or identities.

        Detail level: Summary
        Give the shortest useful answer in one or two sentences.
        Lead with the main thing or direct answer.
        If there is any immediate hazard, mention it first.
        """);

    public CommandPromptDefinition Overview { get; init; } = new(
        nameof(LookDetailLevel.Overview),
        "Look",
        "Look. Give an overview.",
        """
        You are helping a blind or visually impaired user understand a camera frame.
        Safety-relevant observations come first.
        Say when the image appears dark, blurry, blocked, too close, too far away, or ambiguous.
        Do not infer hidden facts or identities.

        Detail level: Overview
        Give an orientation-first overview that is easy to listen to while moving.
        Include people, obstacles, entrances, exits, signs, and major objects.
        Use spatial language such as left, right, ahead, above, below, near, and far.
        """);

    public CommandPromptDefinition Detailed { get; init; } = new(
        nameof(LookDetailLevel.Detailed),
        "Detail",
        "Look at the image in detail.",
        """
        You are helping a blind or visually impaired user understand a camera frame.
        Safety-relevant observations come first.
        Say when the image appears dark, blurry, blocked, too close, too far away, or ambiguous.
        Do not infer hidden facts or identities.

        Detail level: Detailed
        Give a structured scene description.
        Include important objects, relationships, visible text snippets, confidence, uncertainty, and possible next actions.
        Use consistent spatial language such as left, right, ahead, above, below, near, and far.
        """);

    public CommandPromptDefinition Full { get; init; } = new(
        nameof(LookDetailLevel.Full),
        "Full",
        "Look at the image as fully as possible.",
        """
        You are helping a blind or visually impaired user understand a camera frame.
        Safety-relevant observations come first.
        Say when the image appears dark, blurry, blocked, too close, too far away, or ambiguous.
        Do not infer hidden facts or identities.

        Detail level: Full
        Give the most complete reasonable description of the frame.
        Include visible text, layout, colors, object details, hazards, and uncertainty.
        Avoid claims that are not supported by the image.
        """);

    public IReadOnlyList<CommandPromptDefinition> All =>
        [Summary, Overview, Detailed, Full];

    public CommandPromptDefinition Get(LookDetailLevel detail) => detail switch
    {
        LookDetailLevel.Overview => Overview,
        LookDetailLevel.Detailed => Detailed,
        LookDetailLevel.Full => Full,
        _ => Summary,
    };
}

public sealed class LookCommand : CameraCommandBase<LookCommandOptions>, ICommandPromptProvider
{
    private readonly VisionAgent _vision;
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly IAnalyticsService _analytics;
    private static readonly LookCommandPrompts DefaultPrompts = new();

    public LookCommand(VisionAgent vision)
        : this(vision, AiProviderRegistry.Default, new NullAnalyticsService())
    {
    }

    public LookCommand(
        VisionAgent vision,
        IAiProviderRegistry providerRegistry,
        IAnalyticsService analytics)
    {
        _vision = vision;
        _providerRegistry = providerRegistry;
        _analytics = analytics;
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
        new(nameof(LookCommandOptions.DetailLevel), typeof(LookDetailLevel), LookDetailLevel.Overview, true),
        new(nameof(LookCommandOptions.Focus), typeof(string), null, false),
        new(nameof(LookCommandOptions.Question), typeof(string), null, false),
    ];

    public LookCommandPrompts Prompts { get; } = new();

    public IReadOnlyList<CommandPromptDefinition> PromptDefinitions => Prompts.All;

    public override async Task<CameraCommandResult> ExecuteAsync(
        CameraCommandContext context,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var options = ResolveOptions(context);
        var promptDefinition = Prompts.Get(options.DetailLevel ?? LookDetailLevel.Overview);
        var data = BaseData(context, options);
        data["detail_level"] = options.DetailLevel?.ToString();
        data["prompt_key"] = promptDefinition.Key;
        data["prompt_text"] = promptDefinition.Text;

        var providerId = AiProviderIds.Normalize(context.Settings.ProviderId);
        var provider = _providerRegistry.TryGet(providerId);
        data["provider_id"] = provider?.Id ?? providerId;
        data["capability_path"] = "vision";

        if (!SupportsVisionInput(provider))
        {
            sw.Stop();
            var message = provider is null
                ? $"Active provider '{providerId}' is not registered."
                : $"Active provider '{provider.DisplayName}' does not support image input.";
            data["error"] = message;
            TrackCommand(provider?.Id ?? providerId, context, "error", sw.Elapsed, "unsupported_capability");

            return new CameraCommandResult(
                Id,
                Success: false,
                TranscriptText: message,
                Data: data,
                Error: message);
        }

        var activeProvider = provider!;
        var frame = await CaptureFrameForModeAsync(context, ct).ConfigureAwait(false);
        if (frame is null)
        {
            sw.Stop();
            TrackCommand(activeProvider.Id, context, "error", sw.Elapsed, "camera_unavailable");
            return CameraUnavailable(Id);
        }

        var transcriptInput = new CameraCommandTranscriptInput(
            promptDefinition.Text,
            frame,
            "Captured frame");

        string description;
        try
        {
            var prompt = BuildPrompt(options, promptDefinition);
            description = await _vision.DescribeFrameAsync(frame, prompt, ct).ConfigureAwait(false);
            sw.Stop();
            TrackCommand(activeProvider.Id, context, "success", sw.Elapsed, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var message = $"Look error: {ex.Message}";
            data["error"] = ex.Message;
            TrackCommand(activeProvider.Id, context, "error", sw.Elapsed, Categorize(ex));

            return new CameraCommandResult(
                Id,
                Success: false,
                TranscriptText: message,
                Data: data,
                Error: ex.Message,
                TranscriptInput: transcriptInput);
        }

        data["description"] = description;

        return new CameraCommandResult(
            Id,
            Success: true,
            TranscriptText: description,
            Data: data,
            Error: null,
            TranscriptInput: transcriptInput);
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
        return BuildPrompt(options, ResolvePromptDefinition(options));
    }

    private static string BuildPrompt(
        LookCommandOptions options,
        CommandPromptDefinition promptDefinition)
    {
        var prompt = promptDefinition.Prompt;

        if (!string.IsNullOrWhiteSpace(options.Focus))
            prompt += $"{Environment.NewLine}Pay particular attention to: {options.Focus}.";

        if (!string.IsNullOrWhiteSpace(options.Question))
            prompt += $"{Environment.NewLine}Answer this question first if the image supports it: {options.Question}";

        return prompt;
    }

    public static CommandPromptDefinition ResolvePromptDefinition(LookCommandOptions options) =>
        DefaultPrompts.Get(options.DetailLevel ?? LookDetailLevel.Overview);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool SupportsVisionInput(AiProviderDefinition? provider) =>
        provider is not null
        && (provider.Supports(AiProviderCapability.Vision)
            || provider.Supports(AiProviderCapability.ImageInput));

    private void TrackCommand(
        string providerId,
        CameraCommandContext context,
        string result,
        TimeSpan latency,
        string? errorCategory)
    {
        if (!_analytics.IsEnabled)
            return;

        var properties = new Dictionary<string, string>
        {
            ["provider.id"] = providerId,
            ["capability.path"] = "vision",
            ["command"] = Id,
            ["mode"] = context.ResolvedMode.ToString(),
            ["origin"] = context.Request.Origin.ToString(),
            ["result"] = result,
            ["fallback.path"] = "none",
            ["latency.ms"] = ((int)latency.TotalMilliseconds).ToString(),
        };

        if (errorCategory is not null)
            properties["error.category"] = errorCategory;

        _analytics.TrackEvent("ai.command.capability", properties);
        _analytics.TrackMetric("ai.command.latency_ms", latency.TotalMilliseconds, properties);
    }

    private static string Categorize(Exception ex)
    {
        if (ex is HttpRequestException)
            return "network";
        if (ex.Message.Contains("401", StringComparison.Ordinal)
            || ex.Message.Contains("403", StringComparison.Ordinal))
        {
            return "auth";
        }
        if (ex.Message.Contains("429", StringComparison.Ordinal))
            return "rate_limit";
        if (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "timeout";

        return "provider_error";
    }
}
