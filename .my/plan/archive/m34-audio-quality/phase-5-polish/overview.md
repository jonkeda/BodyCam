# Phase 5 — Voice quality polish

**Status:** Proposed
**Depends on:** Phase 1 (correctness)
**Sibling phases:** [Phase 1](../phase-1-correctness/overview.md), [Phase 2](../phase-2-resampling/overview.md), [Phase 3](../phase-3-threading/overview.md), [Phase 4](../phase-4-platform-coverage/overview.md), [Phase 6](../phase-6-observability/overview.md)

---

## Summary

Final layer of voice quality: AGC target and compression tuning, NS level
adjustment, optional mic ducking during playback, and eliminating the
audible click on barge-in. Mostly parameter changes plus one small
fade-out implementation. Low risk; everything ships behind safe defaults
that preserve current behaviour or improve it.

---

## 5.1 — Tune AGC target and compression

### Problem

Current configuration in
[AecProcessor.Initialize](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs):

```csharp
WebRtcApmInterop.ConfigSetGainController1(config, 1, 1, 3, 9, 1);
// mode=1 (AdaptiveDigital), target=-3 dBFS, compression=9 dB, limiter=on
```

`-3 dBFS` target is too hot:
- Plosives (`p`, `b`, `t`) clip → audible distortion.
- The aggressive 9 dB compression curve produces "pumping" / "breathing".
- Loud short bursts cause AGC to gain-down the entire signal, leading
  to false barge-ins from `semantic_vad` reading the post-attack quiet
  as a turn boundary.

### Fix

New defaults: **target = −9 dBFS, compression = 6 dB, limiter on**, exposed via `AppSettings`.

[AppSettings.cs](../../../src/BodyCam/AppSettings.cs):

```csharp
// AGC tuning
public int AgcTargetLevelDbfs   { get; set; } = -9;
public int AgcCompressionGainDb { get; set; } =  6;
```

[AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs):

```csharp
public AecProcessor(ILogger<AecProcessor> logger, AppSettings? settings = null)
{
    _logger = logger;
    _settings = settings;
}

// In Initialize():
int target = -Math.Abs(_settings?.AgcTargetLevelDbfs ?? -9);
int comp   = _settings?.AgcCompressionGainDb ?? 6;
// WebRTC API expects target as a positive number representing dB below 0 dBFS
WebRtcApmInterop.ConfigSetGainController1(config, 1, 1, Math.Abs(target), comp, 1);
```

### Test plan
- Record "Peter Parker picked plenty of pickled peppers" → measure peak level pre/post-AGC.
- Before: peaks pinned at −3 dBFS, plosives flat-topped.
- After: peaks float at −9 to −6 dBFS, no pumping.
- Realtime API transcription accuracy should stay flat or improve.

### Acceptance
- [ ] `AppSettings.AgcTargetLevelDbfs` and `AgcCompressionGainDb` exist with the new defaults.
- [ ] `AecProcessor` reads them in `Initialize`.
- [ ] Plosive recording shows no clipping; no audible pumping.

---

## 5.2 — Tune noise suppression

### Problem

Current setting in
[AecProcessor.Initialize](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs):

```csharp
WebRtcApmInterop.ConfigSetNoiseSuppression(config, 1, 2); // High
```

Level 2 (Aggressive / High) introduces "musical noise" — chirpy spectral
notches that sound worse than the original background hum, especially in
office / HVAC environments.

### Fix

New default: **level = 1 (Moderate)**, exposed via `AppSettings`.

[AppSettings.cs](../../../src/BodyCam/AppSettings.cs):
```csharp
public int NoiseSuppressionLevel { get; set; } = 1; // 0..3
```

[AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs):
```csharp
int nsLevel = Math.Clamp(_settings?.NoiseSuppressionLevel ?? 1, 0, 3);
WebRtcApmInterop.ConfigSetNoiseSuppression(config, 1, nsLevel);
```

### Test plan
- Record in a noisy environment (fan, ambient chatter), level 1 vs level 2 — listen for musical-noise artifacts.
- Steady noise floor should be only ~6–8 dB louder at level 1, with no chirps.

### Acceptance
- [ ] `AppSettings.NoiseSuppressionLevel` defaults to 1.
- [ ] No chirp/musical-noise artifacts in test recording.
- [ ] Level can be overridden via setting (0–3).

---

## 5.3 — Mic ducking during playback (opt-in)

### Problem

Even with APM doing its job, some users report sporadic AI self-replies
in highly reverberant or loud-speaker environments. For those cases an
opt-in "freeze the mic while the assistant is speaking" mode is useful —
it kills barge-in but eliminates the last few percent of edge cases.

### Fix

[AppSettings.cs](../../../src/BodyCam/AppSettings.cs):
```csharp
public bool PauseMicWhilePlaying { get; set; } = false;
```

[VoiceInputAgent.cs](../../../src/BodyCam/Agents/VoiceInputAgent.cs):

```csharp
public VoiceInputAgent(
    IAudioInputService audioInput,
    ILogger<VoiceInputAgent> logger,
    AecProcessor? aec = null,
    AudioPlaybackTracker? playbackTracker = null,
    AppSettings? settings = null)
{
    ...
    _playbackTracker = playbackTracker;
    _settings        = settings;
}

private async void OnAudioChunk(object? sender, byte[] chunk)
{
    try
    {
        if (!_isConnected || _audioSink is null) return;

        if (_settings?.PauseMicWhilePlaying == true && _playbackTracker?.IsPlaying == true)
            return; // gated

        byte[] processed = _aec is not null ? _aec.ProcessCapture(chunk) : chunk;
        await _audioSink(processed, CancellationToken.None);
        _chunksSent++;
    }
    catch (Exception ex) { _logger.LogWarning(ex, "Audio chunk send failed"); }
}
```

`AudioPlaybackTracker` already exists and is owned by `VoiceOutputAgent`;
inject the same instance from the orchestrator into both agents.

**Tradeoff documented**: when enabled, the user cannot interrupt the
assistant mid-sentence. Surface this in the settings UI text.

### Test plan
- Unit: stub tracker `IsPlaying = true` + `PauseMicWhilePlaying = true` → assert no chunks sent.
- Same with `false` → chunks flow.
- Manual: enable, attempt to interrupt — should not break in.

### Acceptance
- [ ] Setting exists, default `false`.
- [ ] When enabled and `IsPlaying` is true, no chunks reach the API.
- [ ] When disabled, behaviour unchanged.

---

## 5.4 — Soft fade on barge-in

### Problem

[VoiceOutputAgent.HandleInterruption](../../../src/BodyCam/Agents/VoiceOutputAgent.cs)
calls `_audioOutput.ClearBuffer()`, which truncates the active speaker
sample mid-cycle. Result: audible click/pop at every interruption.

### Fix

Add to [IAudioOutputProvider](../../../src/BodyCam/Services/Audio/IAudioOutputProvider.cs):

```csharp
/// <summary>
/// Fade out any buffered audio over the given duration, then clear the buffer.
/// Default fadeMs ≈ 30 prevents audible clicks on barge-in.
/// </summary>
Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default);
```

Implement on each provider. Two strategies depending on the buffer model:

**Strategy A (queue of byte[] chunks — Android/Windows BT):**
- Locate the most-recently-queued chunk.
- Apply a linear fade-out over `fadeMs` worth of samples at the end of that chunk.
- Discard everything queued *after* it.
- Then `ClearBuffer()`.

**Strategy B (continuous PCM provider — Windows `BufferedWaveProvider`):**
- Read out the last `fadeMs * sampleRate / 1000` samples from the buffer (they aren't physically reachable in NAudio's BufferedWaveProvider — it's write-only).
- Easier alternative: write a final fade-out chunk *before* clearing:
  ```csharp
  var fade = GenerateFadeOutChunk(_lastChunkPlayed, fadeMs, sampleRate);
  await PlayChunkAsync(fade);
  await Task.Delay(fadeMs);
  ClearBuffer();
  ```
  where `_lastChunkPlayed` is a small ring of recent samples (track 50 ms).

For now, strategy B with a 30 ms ring buffer of recent output is sufficient.

[VoiceOutputAgent.HandleInterruption](../../../src/BodyCam/Agents/VoiceOutputAgent.cs):

```csharp
public async Task HandleInterruptionAsync(CancellationToken ct = default)
{
    await _audioOutput.FadeOutAndClearAsync(fadeMs: 30, ct);
    _aec?.ResetRenderReference(); // Phase 1.1
}
```

(Convert `HandleInterruption` to async, OR do `Task.Run` and forget — either is acceptable for a barge-in event.)

### Test plan
- Unit: feed a constant tone, call `FadeOutAndClearAsync(30)`, capture last samples — assert linear ramp from full → 0 over 30 ms, then silence.
- Manual: trigger interruption mid-sentence — no audible click.

### Acceptance
- [ ] `IAudioOutputProvider.FadeOutAndClearAsync` defined and implemented in all providers.
- [ ] `VoiceOutputAgent.HandleInterruption` calls it.
- [ ] No click on interruption in manual test.
- [ ] Fade fully completes (30 ms ramp, no abrupt cutoff).

---

## Files touched

- [src/BodyCam/AppSettings.cs](../../../src/BodyCam/AppSettings.cs) — 4 new settings.
- [src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs) — read AGC + NS settings.
- [src/BodyCam/Agents/VoiceInputAgent.cs](../../../src/BodyCam/Agents/VoiceInputAgent.cs) — ducking gate.
- [src/BodyCam/Agents/VoiceOutputAgent.cs](../../../src/BodyCam/Agents/VoiceOutputAgent.cs) — async interruption + fade.
- [src/BodyCam/Services/Audio/IAudioOutputProvider.cs](../../../src/BodyCam/Services/Audio/IAudioOutputProvider.cs) — `FadeOutAndClearAsync`.
- [src/BodyCam/Services/Audio/AudioOutputManager.cs](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs) — proxy through to provider.
- All output providers ([WindowsSpeakerProvider](../../../src/BodyCam/Platforms/Windows/WindowsSpeakerProvider.cs), [PhoneSpeakerProvider](../../../src/BodyCam/Platforms/Android/PhoneSpeakerProvider.cs), BT variants, iOS) — implement fade.
- New tests: `src/BodyCam.Tests/Services/Audio/FadeOutTests.cs`, `VoiceInputAgentDuckingTests.cs`.

---

## Execution order within Phase 5

1. **5.1 + 5.2** — single config-only PR, ships first (immediate quality bump).
2. **5.4** — fade-out (small but visible / audible).
3. **5.3** — opt-in ducking (last; needs settings UI surface from M30 Phase 4).
