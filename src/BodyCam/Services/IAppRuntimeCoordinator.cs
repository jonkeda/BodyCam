namespace BodyCam.Services;

/// <summary>
/// Owns long-lived application runtime startup that should not live in page code.
/// </summary>
public interface IAppRuntimeCoordinator
{
    bool IsStarted { get; }

    Task StartAsync(CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);
}
