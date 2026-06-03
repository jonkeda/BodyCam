namespace BodyCam.Services.Session;

public enum SessionLayer
{
    Sleep = 0,
    WakeWord = 1,
    ActiveSession = 2,
}

public sealed record SessionTransitionOptions(
    Func<Task<string?>>? PromptForApiKeyAsync = null,
    Func<CancellationToken, Task<byte[]?>>? FrameCaptureFunc = null);

public sealed record SessionTransitionResult(
    bool Success,
    SessionLayer CurrentLayer,
    bool IsRunning,
    string StatusText,
    string ToggleButtonText,
    string? Error = null);

public sealed class SessionStateChangedEventArgs : EventArgs
{
    public required SessionLayer Layer { get; init; }
    public required bool IsRunning { get; init; }
    public required string StatusText { get; init; }
    public required string ToggleButtonText { get; init; }
}

public interface ISessionRuntime
{
    bool IsRunning { get; }

    Func<CancellationToken, Task<byte[]?>>? FrameCaptureFunc { get; set; }

    Task StartAsync();
    Task StopAsync();
    Task StartListeningAsync();
    Task StopListeningAsync();
    Task StopSpeakingAsync(CancellationToken ct = default);
    Task SendTextInputAsync(string text, CancellationToken ct = default);
}

public interface ISessionCoordinator
{
    SessionLayer CurrentLayer { get; }
    bool IsRunning { get; }

    event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    Task<SessionTransitionResult> SetLayerAsync(
        SessionLayer target,
        SessionTransitionOptions? options = null,
        CancellationToken ct = default);

    Task<SessionTransitionResult> ToggleAsync(
        SessionTransitionOptions? options = null,
        CancellationToken ct = default);

    Task StopSpeakingAsync(CancellationToken ct = default);
    Task SendTextInputAsync(string text, CancellationToken ct = default);
}
