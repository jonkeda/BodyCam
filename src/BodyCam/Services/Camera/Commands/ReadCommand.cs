using System.Diagnostics;
using System.Net.Http;
using BodyCam.Agents;
using BodyCam.Services;
using BodyCam.Services.AiProviders;

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

public sealed class ReadCommandPrompts
{
    public CommandPromptDefinition Summary { get; init; } = new(
        nameof(ReadDetailLevel.Summary),
        "Summary",
        "Read. Summarize the visible text.",
        """
        Extract visible text from this image for a blind or visually impaired user.
        If no readable text is visible, respond with exactly: NO_TEXT.
        If the frame is blurry, dark, blocked, or confidence is low, say that before the text.

        Detail level: Summary
        Summarize what the visible text says. Keep it short and useful.
        """);

    public CommandPromptDefinition Overview { get; init; } = new(
        nameof(ReadDetailLevel.Overview),
        "Overview",
        "Read. Give an overview.",
        """
        Extract visible text from this image for a blind or visually impaired user.
        If no readable text is visible, respond with exactly: NO_TEXT.
        If the frame is blurry, dark, blocked, or confidence is low, say that before the text.

        Detail level: Overview
        Explain the document, sign, label, menu, or screen type and the main visible sections.
        """);

    public CommandPromptDefinition Full { get; init; } = new(
        nameof(ReadDetailLevel.Full),
        "Full",
        "Read all visible text.",
        """
        Extract visible text from this image for a blind or visually impaired user.
        If no readable text is visible, respond with exactly: NO_TEXT.
        If the frame is blurry, dark, blocked, or confidence is low, say that before the text.

        Detail level: Full
        Read the visible text as completely and exactly as possible. Preserve wording and line breaks when useful.
        """);

    public IReadOnlyList<CommandPromptDefinition> All =>
        [Summary, Overview, Full];

    public CommandPromptDefinition Get(ReadDetailLevel detail) => detail switch
    {
        ReadDetailLevel.Summary => Summary,
        ReadDetailLevel.Overview => Overview,
        _ => Full,
    };
}

public sealed class ReadCommand : CameraCommandBase<ReadCommandOptions>, ICommandPromptProvider
{
    private readonly VisionAgent _vision;
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly IAnalyticsService _analytics;
    private static readonly ReadCommandPrompts DefaultPrompts = new();

    public ReadCommand(VisionAgent vision)
        : this(vision, AiProviderRegistry.Default, new NullAnalyticsService())
    {
    }

    public ReadCommand(
        VisionAgent vision,
        IAiProviderRegistry providerRegistry,
        IAnalyticsService analytics)
    {
        _vision = vision;
        _providerRegistry = providerRegistry;
        _analytics = analytics;
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

    public ReadCommandPrompts Prompts { get; } = new();

    public IReadOnlyList<CommandPromptDefinition> PromptDefinitions => Prompts.All;

    public override async Task<CameraCommandResult> ExecuteAsync(
        CameraCommandContext context,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var options = ResolveOptions(context);
        var promptDefinition = Prompts.Get(options.DetailLevel ?? ReadDetailLevel.Full);
        var data = BaseData(context, options);
        data["detail_level"] = options.DetailLevel?.ToString();
        data["provider_id"] = AiProviderIds.Normalize(context.Settings.ProviderId);
        data["capability_path"] = "vision";

        var provider = _providerRegistry.TryGet(context.Settings.ProviderId);
        if (!SupportsVisionInput(provider))
        {
            sw.Stop();
            var providerId = AiProviderIds.Normalize(context.Settings.ProviderId);
            var message = provider is null
                ? $"Active provider '{providerId}' is not registered."
                : $"Active provider '{provider.DisplayName}' does not support image input.";
            data["provider_id"] = provider?.Id ?? providerId;
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
        data["provider_id"] = activeProvider.Id;
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

        string text;
        try
        {
            var prompt = BuildPrompt(options, promptDefinition);
            text = await _vision.DescribeFrameAsync(frame, prompt, ct).ConfigureAwait(false);
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
            var message = $"Read error: {ex.Message}";
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

        var transcript = IsNoText(text) ? "No text detected." : text;
        data["text"] = transcript;

        return new CameraCommandResult(
            Id,
            Success: true,
            TranscriptText: transcript,
            Data: data,
            Error: null,
            TranscriptInput: transcriptInput);
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
        return BuildPrompt(options, ResolvePromptDefinition(options));
    }

    private static string BuildPrompt(
        ReadCommandOptions options,
        CommandPromptDefinition promptDefinition)
    {
        var prompt = promptDefinition.Prompt;

        if (!string.IsNullOrWhiteSpace(options.Focus))
            prompt += $"{Environment.NewLine}Focus on the {options.Focus}.";

        return prompt;
    }

    public static CommandPromptDefinition ResolvePromptDefinition(ReadCommandOptions options) =>
        DefaultPrompts.Get(options.DetailLevel ?? ReadDetailLevel.Full);

    private static bool IsNoText(string text) =>
        string.IsNullOrWhiteSpace(text)
        || text.Contains("NO_TEXT", StringComparison.OrdinalIgnoreCase);

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
