using BodyCam.Services;

namespace BodyCam.Services.Camera.Commands;

public enum CameraCommandMode
{
    FullAuto,
    ManualAim,
}

public enum CommandTriggerOrigin
{
    ActionsDrawer,
    PhysicalButton,
    WakeWord,
    KeyboardShortcut,
    LlmToolCall,
    Automation,
    ExplicitManual,
}

public sealed record CameraCommandCapabilities(
    bool SupportsFullAuto,
    bool SupportsManualAim,
    bool RequiresStillFrame,
    bool CanUseFrameStream,
    bool RequiresConfirmationForExternalActions);

public sealed record CommandOptionDefinition(
    string Name,
    Type ValueType,
    object? DefaultValue,
    bool PersistLastSelectedValue);

public sealed record CommandPromptDefinition(
    string Key,
    string DisplayName,
    string Text,
    string Prompt);

public sealed record CameraActionVariantDefinition(
    string Key,
    string DisplayName,
    string Text,
    object? Options = null,
    string? Query = null,
    bool IsDefault = false);

public sealed record CameraCommandTranscriptInput(
    string Text,
    byte[]? ImageBytes,
    string? ImageCaption);

public sealed record CameraCommandRequest(
    string CommandId,
    CameraCommandMode? Mode,
    CommandTriggerOrigin Origin,
    object? Options,
    string? Query);

public sealed record CameraCommandContext(
    CameraCommandRequest Request,
    CameraCommandMode ResolvedMode,
    CameraManager Cameras,
    ISettingsService Settings,
    Func<CancellationToken, Task<byte[]?>> CaptureFrame,
    Func<CancellationToken, Task<byte[]?>> WaitForManualCapture);

public sealed record CameraCommandResult(
    string CommandId,
    bool Success,
    string TranscriptText,
    object? Data,
    string? Error,
    CameraCommandTranscriptInput? TranscriptInput = null);

public interface ICameraCommand
{
    string Id { get; }
    string DisplayName { get; }
    string? ToolName { get; }
    CameraCommandCapabilities Capabilities { get; }
    IReadOnlyList<CommandOptionDefinition> Options { get; }

    CameraCommandMode ResolveMode(CameraCommandRequest request, CameraCommandContext context);
    Task<CameraCommandResult> ExecuteAsync(CameraCommandContext context, CancellationToken ct);
}

public interface ICommandPromptProvider
{
    IReadOnlyList<CommandPromptDefinition> PromptDefinitions { get; }
}

public interface ICameraActionVariantProvider
{
    IReadOnlyList<CameraActionVariantDefinition> CameraActionVariants { get; }
}

public interface ICameraCommandRegistry
{
    IReadOnlyList<ICameraCommand> Commands { get; }
    bool TryGet(string id, out ICameraCommand command);
    bool TryGetTool(string toolName, out ICameraCommand command);
    ICameraCommand GetRequired(string id);
}

public interface ICameraCommandService
{
    Task<CameraCommandResult> ExecuteAsync(
        CameraCommandRequest request,
        CancellationToken ct = default);
}
