using BodyCam.Services;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Camera.Commands;

public sealed class CameraCommandService : ICameraCommandService
{
    private readonly ICameraCommandRegistry _registry;
    private readonly CameraManager _cameras;
    private readonly ISettingsService _settings;
    private readonly IManualCameraCaptureCoordinator _manualCapture;
    private readonly ILogger<CameraCommandService> _log;

    public CameraCommandService(
        ICameraCommandRegistry registry,
        CameraManager cameras,
        ISettingsService settings,
        IManualCameraCaptureCoordinator manualCapture,
        ILogger<CameraCommandService> log)
    {
        _registry = registry;
        _cameras = cameras;
        _settings = settings;
        _manualCapture = manualCapture;
        _log = log;
    }

    public async Task<CameraCommandResult> ExecuteAsync(
        CameraCommandRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var command = _registry.GetRequired(request.CommandId);
            var context = CreateContext(request, CameraCommandMode.FullAuto);
            var mode = command.ResolveMode(request, context);

            if (!SupportsMode(command, mode))
            {
                var message = $"{command.DisplayName} does not support {mode}.";
                return new CameraCommandResult(command.Id, false, message, new { error = message }, message);
            }

            context = context with { ResolvedMode = mode };
            return await command.ExecuteAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Camera command {CommandId} failed", request.CommandId);
            var message = $"Command error: {ex.Message}";
            return new CameraCommandResult(
                request.CommandId,
                Success: false,
                TranscriptText: message,
                Data: new { error = ex.Message },
                Error: ex.Message);
        }
    }

    private CameraCommandContext CreateContext(
        CameraCommandRequest request,
        CameraCommandMode resolvedMode)
    {
        return new CameraCommandContext(
            request,
            resolvedMode,
            _cameras,
            _settings,
            CaptureFrame: _cameras.CaptureFrameAsync,
            WaitForManualCapture: ct => _manualCapture.WaitForCaptureAsync(
                request,
                _cameras.CaptureFrameAsync,
                ct));
    }

    private static bool SupportsMode(ICameraCommand command, CameraCommandMode mode) =>
        mode switch
        {
            CameraCommandMode.FullAuto => command.Capabilities.SupportsFullAuto,
            CameraCommandMode.ManualAim => command.Capabilities.SupportsManualAim,
            _ => false,
        };
}
