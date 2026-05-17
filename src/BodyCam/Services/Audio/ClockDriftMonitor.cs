using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio;

/// <summary>
/// Monitors long-term clock drift between audio capture and render paths.
/// Phase 6.2: Detects resampler bugs, sub-frame leaks, or platform clock skew.
/// </summary>
public sealed class ClockDriftMonitor
{
    private readonly ILogger<ClockDriftMonitor> _log;
    private readonly double _threshold;
    private long _capture, _render, _capStart, _renStart;
    private DateTime _windowStart = DateTime.UtcNow;

    public event EventHandler<ClockDriftAlarm>? DriftAlarmRaised;

    public ClockDriftMonitor(ILogger<ClockDriftMonitor> log, double thresholdPercent = 0.5)
    {
        _log = log;
        _threshold = thresholdPercent;
    }

    public void RecordCaptureSamples(int n) => Interlocked.Add(ref _capture, n);
    public void RecordRenderSamples(int n) => Interlocked.Add(ref _render, n);

    /// <summary>
    /// Check for drift once per second (driven by AecProcessor stats timer).
    /// Alarms if >0.5% drift over 60s.
    /// </summary>
    public void Tick()
    {
        if ((DateTime.UtcNow - _windowStart).TotalSeconds < 60) return;

        long cap = Interlocked.Read(ref _capture) - _capStart;
        long ren = Interlocked.Read(ref _render) - _renStart;
        if (cap == 0 || ren == 0) { ResetWindow(); return; }

        double drift = Math.Abs(cap - ren) * 100.0 / Math.Max(cap, ren);
        if (drift > _threshold)
        {
            _log.LogWarning("Clock drift {Drift:F2}% over 60s ({Cap} cap vs {Ren} ren samples)", drift, cap, ren);
            DriftAlarmRaised?.Invoke(this, new ClockDriftAlarm(drift, cap, ren, DateTime.UtcNow));
        }
        ResetWindow();
    }

    private void ResetWindow()
    {
        _capStart = Interlocked.Read(ref _capture);
        _renStart = Interlocked.Read(ref _render);
        _windowStart = DateTime.UtcNow;
    }
}

public record ClockDriftAlarm(double Percent, long CaptureSamples, long RenderSamples, DateTime At);
