using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio.WebRtcApm;

/// <summary>
/// No-op AEC implementation used when a native processor is unavailable.
/// </summary>
public sealed class NullAecProcessor : IAecProcessor
{
    private readonly ILogger<NullAecProcessor>? _logger;
    private readonly string _reason;
    private bool _logged;

    public bool IsEnabled { get; set; }

    public NullAecProcessor(ILogger<NullAecProcessor>? logger = null, string reason = "AEC processor unavailable")
    {
        _logger = logger;
        _reason = reason;
    }

    public void Initialize(bool mobileMode = false) => LogOnce();

    public byte[] ProcessCapture(byte[] pcm16At48k)
    {
        LogOnce();
        return pcm16At48k;
    }

    public void FeedRenderReference(byte[] pcm16At48k) => LogOnce();

    public void UpdateStreamDelay(int totalDelayMs) => LogOnce();

    public void ResetRenderReference() => LogOnce();

    public void Dispose()
    {
    }

    private void LogOnce()
    {
        if (_logged) return;
        _logged = true;
        _logger?.LogWarning("{Reason}; audio capture is passing through unprocessed", _reason);
    }
}
