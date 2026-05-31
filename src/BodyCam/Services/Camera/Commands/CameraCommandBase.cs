using System.Text.Json;
using BodyCam.Services;

namespace BodyCam.Services.Camera.Commands;

public abstract class CameraCommandBase<TOptions> : ICameraCommand
    where TOptions : class
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public virtual string? ToolName => Id;
    public abstract CameraCommandCapabilities Capabilities { get; }
    public virtual IReadOnlyList<CommandOptionDefinition> Options => [];

    public abstract Task<CameraCommandResult> ExecuteAsync(
        CameraCommandContext context,
        CancellationToken ct);

    public virtual CameraCommandMode ResolveMode(
        CameraCommandRequest request,
        CameraCommandContext context)
    {
        if (request.Mode is { } explicitMode)
            return explicitMode;

        return request.Origin switch
        {
            CommandTriggerOrigin.LlmToolCall => CameraCommandMode.FullAuto,
            CommandTriggerOrigin.PhysicalButton => CameraCommandMode.FullAuto,
            CommandTriggerOrigin.WakeWord => CameraCommandMode.FullAuto,
            CommandTriggerOrigin.KeyboardShortcut => CameraCommandMode.FullAuto,
            CommandTriggerOrigin.ExplicitManual => CameraCommandMode.ManualAim,
            CommandTriggerOrigin.ActionsDrawer => context.Settings.DefaultTouchCommandMode,
            CommandTriggerOrigin.Automation => throw new InvalidOperationException(
                "Automation camera command requests must specify a mode."),
            _ => CameraCommandMode.FullAuto,
        };
    }

    protected static async Task<byte[]?> CaptureFrameForModeAsync(
        CameraCommandContext context,
        CancellationToken ct)
    {
        return context.ResolvedMode == CameraCommandMode.ManualAim
            ? await context.WaitForManualCapture(ct).ConfigureAwait(false)
            : await context.CaptureFrame(ct).ConfigureAwait(false);
    }

    protected static TOptions? TryReadOptions(CameraCommandRequest request)
    {
        if (request.Options is null)
            return null;

        if (request.Options is TOptions options)
            return options;

        if (request.Options is JsonElement json)
        {
            return json.Deserialize<TOptions>(JsonOptions);
        }

        var serialized = JsonSerializer.Serialize(request.Options, JsonOptions);
        return JsonSerializer.Deserialize<TOptions>(serialized, JsonOptions);
    }

    protected static Dictionary<string, object?> BaseData(
        CameraCommandContext context,
        object? options)
    {
        return new Dictionary<string, object?>
        {
            ["command"] = context.Request.CommandId,
            ["mode"] = context.ResolvedMode.ToString(),
            ["origin"] = context.Request.Origin.ToString(),
            ["options"] = options,
        };
    }

    protected static CameraCommandResult CameraUnavailable(string commandId) =>
        new(
            commandId,
            Success: false,
            TranscriptText: "Camera not available or no frame captured.",
            Data: new { error = "Camera not available or no frame captured." },
            Error: "Camera not available or no frame captured.");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
