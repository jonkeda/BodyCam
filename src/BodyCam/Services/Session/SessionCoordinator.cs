using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Session;

/// <summary>
/// Owns high-level listening/session transitions for the app.
/// </summary>
public sealed class SessionCoordinator : ISessionCoordinator
{
    private readonly ISessionRuntime _runtime;
    private readonly IApiKeyService _apiKeyService;
    private readonly ILogger<SessionCoordinator> _logger;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);

    private SessionLayer _currentLayer = SessionLayer.Sleep;

    public SessionCoordinator(
        ISessionRuntime runtime,
        IApiKeyService apiKeyService,
        ILogger<SessionCoordinator> logger)
    {
        _runtime = runtime;
        _apiKeyService = apiKeyService;
        _logger = logger;
    }

    public SessionLayer CurrentLayer => _currentLayer;
    public bool IsRunning => _runtime.IsRunning;

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public async Task<SessionTransitionResult> ToggleAsync(
        SessionTransitionOptions? options = null,
        CancellationToken ct = default)
    {
        var target = IsRunning ? SessionLayer.Sleep : SessionLayer.ActiveSession;
        return await SetLayerAsync(target, options, ct);
    }

    public async Task<SessionTransitionResult> SetLayerAsync(
        SessionLayer target,
        SessionTransitionOptions? options = null,
        CancellationToken ct = default)
    {
        await _transitionGate.WaitAsync(ct);
        try
        {
            if (target == _currentLayer)
                return PublishState();

            _logger.LogInformation(
                "Session transition {CurrentLayer} -> {TargetLayer}",
                _currentLayer,
                target);

            if (target < _currentLayer)
            {
                await DeescalateAsync(target, ct);
            }

            if (target == SessionLayer.WakeWord && _currentLayer != SessionLayer.WakeWord)
            {
                await _runtime.StartListeningAsync();
                _currentLayer = SessionLayer.WakeWord;
                return PublishState();
            }

            if (target == SessionLayer.ActiveSession && _currentLayer != SessionLayer.ActiveSession)
            {
                var keyResult = await EnsureApiKeyAsync(options, ct);
                if (!keyResult.Success)
                    return keyResult;

                await _runtime.StopListeningAsync();
                _runtime.FrameCaptureFunc = options?.FrameCaptureFunc;
                await _runtime.StartAsync();
                _currentLayer = SessionLayer.ActiveSession;
                return PublishState();
            }

            _currentLayer = target;
            return PublishState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session transition to {TargetLayer} failed", target);
            return PublishState($"Start failed: {ex.Message}");
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public Task StopSpeakingAsync(CancellationToken ct = default) =>
        _runtime.StopSpeakingAsync(ct);

    public Task SendTextInputAsync(string text, CancellationToken ct = default) =>
        _runtime.SendTextInputAsync(text, ct);

    private async Task DeescalateAsync(SessionLayer target, CancellationToken ct)
    {
        if (_currentLayer == SessionLayer.ActiveSession)
        {
            await _runtime.StopAsync();
            _runtime.FrameCaptureFunc = null;
            _currentLayer = SessionLayer.WakeWord;
        }

        if (target == SessionLayer.Sleep && _currentLayer == SessionLayer.WakeWord)
        {
            await _runtime.StopListeningAsync();
            _currentLayer = SessionLayer.Sleep;
        }
    }

    private async Task<SessionTransitionResult> EnsureApiKeyAsync(
        SessionTransitionOptions? options,
        CancellationToken ct)
    {
        var key = await _apiKeyService.GetApiKeyAsync();
        if (!string.IsNullOrWhiteSpace(key))
            return PublishState();

        if (options?.PromptForApiKeyAsync is null)
            return PublishState("API key required", success: false);

        key = await options.PromptForApiKeyAsync();
        if (string.IsNullOrWhiteSpace(key))
            return PublishState("API key required", success: false);

        await _apiKeyService.SetApiKeyAsync(key);
        return PublishState();
    }

    private SessionTransitionResult PublishState(string? error = null, bool success = true)
    {
        var result = new SessionTransitionResult(
            success,
            _currentLayer,
            IsRunning,
            GetStatusText(_currentLayer, error),
            IsRunning ? "Stop" : "Start",
            error);

        StateChanged?.Invoke(this, new SessionStateChangedEventArgs
        {
            Layer = result.CurrentLayer,
            IsRunning = result.IsRunning,
            StatusText = result.StatusText,
            ToggleButtonText = result.ToggleButtonText,
        });

        return result;
    }

    private static string GetStatusText(SessionLayer layer, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return error == "API key required" ? error : "Ready";

        return layer switch
        {
            SessionLayer.Sleep => "Sleeping",
            SessionLayer.WakeWord => "Listening...",
            SessionLayer.ActiveSession => "Active",
            _ => "Ready",
        };
    }
}
