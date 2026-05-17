using Microsoft.Extensions.Logging;

namespace BodyCam.Platforms.Windows.Audio;

/// <summary>
/// Windows Voice Capture DMO (CWMAudioAEC) echo cancellation processor.
/// Opt-in fallback for users where WebRTC APM underperforms.
/// DEPRECATED — Microsoft deprecated this DMO, but it remains shipped on Windows 10/11.
/// </summary>
public sealed class VoiceCaptureDmoAecProcessor : BodyCam.Services.Audio.WebRtcApm.IAecProcessor
{
    private readonly ILogger<VoiceCaptureDmoAecProcessor> _logger;
    private bool _initialized;

    public bool IsEnabled { get; set; } = true;

    public VoiceCaptureDmoAecProcessor(ILogger<VoiceCaptureDmoAecProcessor> logger)
    {
        _logger = logger;
    }

    public void Initialize(bool mobileMode = false)
    {
        if (_initialized) return;

        _logger.LogWarning(
            "*** DEPRECATION WARNING ***\n" +
            "Windows Voice Capture DMO (CWMAudioAEC) is enabled. This is a deprecated Microsoft component.\n" +
            "Use for testing/fallback only. Prefer WebRTC APM (set WindowsUseVoiceCaptureDmo=false).");

        // TODO: Initialize IMFTransform with CLSID {745057C7-F353-4F2D-A7EE-58434477730E}
        // For now, this is a stub that passes audio through unchanged.
        // Full implementation would require MediaFoundation.NetCore or NAudio WASAPI echo cancellation mode.

        _initialized = true;
        _logger.LogInformation("VoiceCaptureDmo AEC initialized (STUB — passthrough mode)");
    }

    public byte[] ProcessCapture(byte[] pcm16At48k)
    {
        if (!IsEnabled || !_initialized)
            return pcm16At48k;

        // STUB: Real implementation would feed to DMO ProcessInput, get ProcessOutput
        return pcm16At48k;
    }

    public void FeedRenderReference(byte[] pcm16At48k)
    {
        if (!_initialized || !IsEnabled) return;

        // STUB: Real implementation would feed to DMO reverse stream
    }

    public void UpdateStreamDelay(int totalDelayMs)
    {
        // DMO does not expose delay tuning in the same way as WebRTC APM
        // Clamp to reasonable range for logging
        int clamped = Math.Clamp(totalDelayMs, 10, 500);
        _logger.LogInformation("DMO AEC delay hint: {DelayMs}ms (not applied in stub)", clamped);
    }

    public void ResetRenderReference()
    {
        if (!_initialized) return;

        // STUB: Real implementation would flush DMO state
        _logger.LogDebug("DMO AEC render reference reset (stub)");
    }

    public void Dispose()
    {
        // STUB: Real implementation would release IMFTransform
        _initialized = false;
    }
}
