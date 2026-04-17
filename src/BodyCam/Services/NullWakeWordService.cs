namespace BodyCam.Services;

public class NullWakeWordService : IWakeWordService
{
    public bool IsListening => false;

    public event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public void RegisterKeywords(IEnumerable<WakeWordEntry> entries) { }
}
