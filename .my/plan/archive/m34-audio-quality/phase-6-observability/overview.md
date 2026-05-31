# Phase 6 — Observability

**Status:** Proposed
**Depends on:** Phase 1 (correctness), Phase 3 (threading)
**Sibling phases:** [Phase 1](../phase-1-correctness/overview.md), [Phase 2](../phase-2-resampling/overview.md), [Phase 3](../phase-3-threading/overview.md), [Phase 4](../phase-4-platform-coverage/overview.md), [Phase 5](../phase-5-polish/overview.md)

---

## Summary

Phase 6 makes the AEC pipeline observable: surface WebRTC APM internal
statistics (ERLE, residual echo likelihood, divergent filter, delay), add
a clock-drift monitor between capture and render paths to detect
resampler / clock-skew bugs early, and ship an A/B-able AEC bypass with
a built-in WAV recorder for blind regression testing in the field.

---

## 6.1 — Surface AEC metrics

### Goal

Expose APM's internal statistics so we can:
- Watch ERLE / convergence in real time during development.
- Detect regressions automatically (CI logs).
- Verify Phase 1.1 (drain on interruption) and Phase 1.3 (adaptive delay) actually improve metrics.

### Implementation

#### 6.1.1 — DllImport bindings

The native lib `webrtc-apm` exposes statistics via a getter that fills a
struct. Verify exact symbol against the prebuilt artifacts in
[soundflow-apm/runtimes/](../../../soundflow-apm/) (the same package
M24 extracted natives from). Add to
[WebRtcApmInterop.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/WebRtcApmInterop.cs):

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct ApmStatistics
{
    public float EchoReturnLossDb;
    public float EchoReturnLossEnhancementDb;
    public int   DelayMs;
    public float ResidualEchoLikelihood;        // 0..1
    public float DivergentFilterFraction;       // 0..1
}

[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
public static extern int GetStatistics(IntPtr apm, out ApmStatistics statistics);
```

If the entry point isn't `webrtc_apm_get_statistics`, check the package
header / nm output and adjust `EntryPoint=`. If symbols aren't exported in
the prebuilt artifacts, fall back to computing simplified metrics
client-side (track input vs output RMS over time → estimated ERL).

#### 6.1.2 — `AecProcessor.GetStatistics`

In [AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs):

```csharp
public event EventHandler<ApmStatistics>? StatisticsUpdated;
private System.Timers.Timer? _statsTimer;
private int _statsTickCount;

public ApmStatistics? GetStatistics()
{
    if (!_initialized) return null;
    lock (_lock)
    {
        if (_disposed) return null;
        int err = WebRtcApmInterop.GetStatistics(_apm, out var s);
        return err == 0 ? s : (ApmStatistics?)null;
    }
}

// In Initialize() after _initialized = true:
_statsTimer = new System.Timers.Timer(1000) { AutoReset = true };
_statsTimer.Elapsed += (_, _) =>
{
    if (GetStatistics() is { } s)
    {
        StatisticsUpdated?.Invoke(this, s);
        if (++_statsTickCount % 10 == 0)
            _logger.LogInformation(
                "AEC: ERLE={ERLE:F1}dB ERLEnh={Enh:F1}dB delay={Delay}ms resEcho={Res:F2} divFilter={Div:F2}",
                s.EchoReturnLossDb, s.EchoReturnLossEnhancementDb, s.DelayMs,
                s.ResidualEchoLikelihood, s.DivergentFilterFraction);
    }
};
_statsTimer.Start();
```

Stop the timer in `Dispose()`.

#### 6.1.3 — Debug overlay on `MainPage`

[AppSettings.cs](../../../src/BodyCam/AppSettings.cs):
```csharp
public bool DebugMode { get; set; } = false;
```

In [MainPage.xaml](../../../src/BodyCam/MainPage.xaml) add a small overlay
label bound to a viewmodel string (e.g. `AecDebugText`):

```xml
<Label x:Name="AecDebugLabel"
       IsVisible="{Binding DebugMode}"
       Text="{Binding AecDebugText}"
       FontFamily="Consolas" FontSize="10"
       BackgroundColor="#80000000" TextColor="Lime"
       Padding="6,2" VerticalOptions="Start" HorizontalOptions="End"/>
```

Wire `AecProcessor.StatisticsUpdated` → viewmodel `AecDebugText`:

```
ERLE 24.1 dB · res 0.04 · delay 92 ms · div 0.01
```

### Acceptance
- [ ] `WebRtcApmInterop.GetStatistics` binds successfully on Windows + Android.
- [ ] `AecProcessor.GetStatistics()` is thread-safe and returns null when not initialized.
- [ ] `StatisticsUpdated` fires at ~1 Hz.
- [ ] Structured log every 10 s.
- [ ] Debug overlay visible only when `DebugMode = true`, updates live.
- [ ] Convergence target: ERLE > 20 dB within 2 s, residual likelihood < 0.1.

### Test plan
- Unit (`AecProcessorStatisticsTests`): stub native, assert event cadence and log emission.
- Integration: real APM with reference tone, assert convergence thresholds.

---

## 6.2 — Capture/render clock-drift counter

### Goal

Detect long-term drift between samples submitted to AEC capture vs render
paths. >0.5 % drift over 60 s indicates a resampler bug, sub-frame leak
(Phase 1.2 regression), or platform clock skew — all of which silently
degrade AEC.

### Implementation

New `src/BodyCam/Services/Audio/ClockDriftMonitor.cs`:

```csharp
public sealed class ClockDriftMonitor
{
    private readonly ILogger<ClockDriftMonitor> _log;
    private readonly double _threshold;
    private long _capture, _render, _capStart, _renStart;
    private DateTime _windowStart = DateTime.UtcNow;

    public event EventHandler<ClockDriftAlarm>? DriftAlarmRaised;

    public ClockDriftMonitor(ILogger<ClockDriftMonitor> log, double thresholdPercent = 0.5)
    { _log = log; _threshold = thresholdPercent; }

    public void RecordCaptureSamples(int n) => Interlocked.Add(ref _capture, n);
    public void RecordRenderSamples(int n)  => Interlocked.Add(ref _render,  n);

    public void Tick()
    {
        if ((DateTime.UtcNow - _windowStart).TotalSeconds < 60) return;

        long cap = Interlocked.Read(ref _capture) - _capStart;
        long ren = Interlocked.Read(ref _render)  - _renStart;
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
```

Hook into [AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs):

```csharp
public AecProcessor(ILogger<AecProcessor> log, AppSettings? s = null, ClockDriftMonitor? drift = null) { ... }

// In ProcessCapture, after successful native call:
_drift?.RecordCaptureSamples(pcm16At24k.Length / 2);

// In FeedRenderReference, same:
_drift?.RecordRenderSamples(pcm16At24k.Length / 2);
```

`Tick()` is driven by the 1 Hz stats timer (6.1) — call it once per tick.

### Test plan
- Unit (`ClockDriftMonitorTests`): record 96 000 cap / 96 000 ren over 60 s → no alarm.
- 96 000 cap / 95 000 ren (~1 %) → alarm, correct percent in event args.
- Real-world: hook to a deliberately broken resampler dropping 1 sample/chunk → alarm fires within 1 minute.

### Acceptance
- [ ] `ClockDriftMonitor` class with thread-safe counters.
- [ ] Hooked in `AecProcessor` capture + render paths.
- [ ] Driven by 1 Hz tick from 6.1.
- [ ] `Warning` log + `DriftAlarmRaised` event when drift > 0.5 %.
- [ ] Unit test with deliberate skew passes.

---

## 6.3 — A/B-able AEC bypass + WAV capture

### Goal

Let testers (or the developer) toggle AEC off and capture short audio
samples to disk for blind A/B comparison.

### Implementation

[AppSettings.cs](../../../src/BodyCam/AppSettings.cs):
```csharp
public bool DisableAec { get; set; } = false;
```

In [AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs)
already has `IsEnabled`. Wire it from settings:

```csharp
public AecProcessor(ILogger<AecProcessor> log, AppSettings? s = null, ...)
{
    _settings = s;
    if (s is not null) IsEnabled = !s.DisableAec;
}
```

When `IsEnabled = false`, `ProcessCapture` already short-circuits to
return its input (existing behaviour — verify in current code).

#### 6.3.1 — `MicCaptureRecorder`

New `src/BodyCam/Services/Audio/MicCaptureRecorder.cs`:

```csharp
public sealed class MicCaptureRecorder
{
    private readonly int _sampleRate;
    private readonly int _maxSamples;
    private readonly Queue<byte[]> _buffer = new();
    private long _bufferedSamples;
    private readonly object _lock = new();

    public MicCaptureRecorder(int sampleRate, int seconds = 10)
    {
        _sampleRate = sampleRate;
        _maxSamples = sampleRate * seconds;
    }

    public void RecordChunk(byte[] pcm16)
    {
        lock (_lock)
        {
            _buffer.Enqueue(pcm16);
            _bufferedSamples += pcm16.Length / 2;
            while (_bufferedSamples > _maxSamples && _buffer.Count > 0)
                _bufferedSamples -= _buffer.Dequeue().Length / 2;
        }
    }

    public void SaveToWav(string path) { /* RIFF/WAVE PCM16 mono writer */ }
}
```

Hook into [AudioInputManager](../../../src/BodyCam/Services/Audio/AudioInputManager.cs)
post-AEC channel (Phase 3.1): record only when `AppSettings.DebugMode` is true to avoid overhead in production.

#### 6.3.2 — Triggering a save

A simple debug command: long-press the mic button (or a hidden settings
button) calls `recorder.SaveToWav(Path.Combine(FileSystem.CacheDirectory, $"mic-{DateTime.Now:yyyyMMdd-HHmmss}.wav"))`.

For automation, expose via a logger trace: when an integration test sets
`DisableAec = true`, run for 10 s, save WAV, set `DisableAec = false`,
run another 10 s, save WAV — both files now sit in cache for offline
listening comparison.

### Acceptance
- [ ] `AppSettings.DisableAec` toggle works at runtime (no restart).
- [ ] When true, `AecProcessor.ProcessCapture` returns input unmodified.
- [ ] `MicCaptureRecorder` keeps a sliding 10-second window.
- [ ] WAV file is valid PCM16 mono at the configured sample rate (verify by playback in Audacity).
- [ ] Recording is gated by `DebugMode` — no overhead in production builds.

### Test plan
- Unit (`MicCaptureRecorderTests`): write 15 s of audio, assert buffer contains last 10 s.
- Round-trip: write WAV, read back, assert bit-exact PCM.
- Manual: enable `DisableAec`, hear echo; disable, echo cancelled; save WAV pair, listen offline.

---

## Files touched

### New
- `src/BodyCam/Services/Audio/ClockDriftMonitor.cs`
- `src/BodyCam/Services/Audio/MicCaptureRecorder.cs`
- `src/BodyCam.Tests/Services/Audio/AecProcessorStatisticsTests.cs`
- `src/BodyCam.Tests/Services/Audio/ClockDriftMonitorTests.cs`
- `src/BodyCam.Tests/Services/Audio/MicCaptureRecorderTests.cs`

### Modified
- [WebRtcApmInterop.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/WebRtcApmInterop.cs) — `GetStatistics` + `ApmStatistics` struct.
- [AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs) — stats event/timer, drift monitor hook, settings binding.
- [AppSettings.cs](../../../src/BodyCam/AppSettings.cs) — `DebugMode`, `DisableAec`.
- [AudioInputManager.cs](../../../src/BodyCam/Services/Audio/AudioInputManager.cs) — wire `MicCaptureRecorder` (gated by `DebugMode`).
- [MainPage.xaml](../../../src/BodyCam/MainPage.xaml) and [MainPage.xaml.cs](../../../src/BodyCam/MainPage.xaml.cs) — debug overlay.

---

## Execution order within Phase 6

1. **6.1** first — metrics let us validate every other phase quantitatively.
2. **6.2** — drift monitor is small and depends on 6.1's tick.
3. **6.3** — bypass + WAV capture; useful for field debugging once metrics exist.
