# Phase 1 — Correctness fixes

**Status:** Proposed
**Depends on:** M24 (WebRTC APM in place)
**Sibling phases:** [Phase 2 — Resampling](../phase-2-resampling/overview.md), [Phase 3 — Threading](../phase-3-threading/overview.md), [Phase 4 — Platform coverage](../phase-4-platform-coverage/overview.md), [Phase 5 — Polish](../phase-5-polish/overview.md), [Phase 6 — Observability](../phase-6-observability/overview.md)

---

## Summary

Four small but high-impact bugs in the current WebRTC APM integration. Each
is local in scope, low-risk, and can ship in a single PR. Together they
should eliminate the "first-word-clipped after barge-in" symptom, fix slow
cumulative AEC drift on long calls, give APM the right delay value to
converge, and stop ~400 GCHandle allocations per second on the audio thread.

---

## 1.1 — Drain APM render reference on interruption

### Symptom
When the user barges in, [VoiceOutputAgent.HandleInterruption()](../../../src/BodyCam/Agents/VoiceOutputAgent.cs)
calls `_audioOutput.ClearBuffer()`. Anything already enqueued in the speaker
buffer is discarded — but APM has *already* been fed those frames as render
reference inside [VoiceOutputAgent.PlayAudioDeltaAsync()](../../../src/BodyCam/Agents/VoiceOutputAgent.cs).
For the next ~150 ms the AEC tries to subtract phantom audio that never
played, and the start of the user's reply gets nuked.

### Fix

Add to [AecProcessor](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs):

```csharp
public void ResetRenderReference()
{
    if (!_initialized) return;
    lock (_lock)
    {
        if (_disposed) return;
        // Re-init clears the AEC echo path estimate and any buffered render frames
        int err = WebRtcApmInterop.Initialize(_apm);
        if (err != 0) _logger.LogWarning("APM re-init returned {Error}", err);

        int delayMs = _mobileMode ? MobileStreamDelayMs : DesktopStreamDelayMs;
        WebRtcApmInterop.SetStreamDelayMs(_apm, delayMs);
    }
}
```

(Promote the `mobileMode` flag from a local of `Initialize()` to a field
`_mobileMode` so the re-init can use it.)

Wire from [VoiceOutputAgent.HandleInterruption](../../../src/BodyCam/Agents/VoiceOutputAgent.cs):

```csharp
public void HandleInterruption()
{
    _audioOutput.ClearBuffer();
    _aec?.ResetRenderReference();
}
```

### Test plan
- Unit: in `AecProcessorTests`, feed render frames, call `ResetRenderReference`, then feed mic — assert no negative ERLE on the first frame after reset.
- Manual: barge in 5 times in a row on Windows + Android; the first 200 ms of each user reply should be intact.

### Acceptance
- [ ] `AecProcessor.ResetRenderReference()` exists and is thread-safe.
- [ ] `VoiceOutputAgent.HandleInterruption()` calls it.
- [ ] No regression in `AecProcessorTests`.
- [ ] Manual barge-in test: first user word audible in 5/5 attempts.

---

## 1.2 — Stop dropping sub-frame samples at chunk boundaries

### Symptom
[AecProcessor.ProcessCapture](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs)
and `FeedRenderReference` both iterate `for (offset = 0; offset + 480 <= total; offset += 480)`
and silently drop any tail < 480 samples. With the linear 24 k → 48 k
resampler producing variable sample counts (1–2 sample drift per chunk),
the capture and render clocks drift apart, AEC never fully converges, and
voice gets micro-truncated.

### Fix
Maintain residual float buffers across calls in `AecProcessor`:

```csharp
private readonly List<float> _captureResidual = new(FrameSamples);
private readonly List<float> _renderResidual  = new(FrameSamples);
```

In `ProcessCapture`, after upsampling and converting to float:

1. Prepend `_captureResidual` to the new sample array.
2. Process all complete 480-sample frames.
3. Save the tail (< 480 samples) into `_captureResidual` for next call.
4. Output: in-place modify the prefixed buffer — but only return samples
   that were actually processed (skip the residual tail). Pad the
   processed-prefix back to the original chunk length using zeros at the
   *front*; the next call's residual will fill in for those, plus a
   one-frame initial latency that callers must accept.

Same pattern in `FeedRenderReference` (no output; just consume).

### Test plan
- Unit: feed 99 samples, then 99, then 99, ... assert that after N calls
  the cumulative sample count fed to native APM equals
  `(input_samples − residual_left_at_end)`.
- Unit: feed a 1 kHz sine at 50 ms chunks for 10 minutes worth of calls;
  assert no phase discontinuity at chunk boundaries (FFT in test harness).
- Reset the residuals in `ResetRenderReference()` (1.1) and `Dispose()`.

### Acceptance
- [ ] No samples dropped in `ProcessCapture` / `FeedRenderReference`.
- [ ] Residuals reset on `ResetRenderReference` and `Dispose`.
- [ ] FFT test confirms < −60 dB spurious content at chunk boundary frequency.

---

## 1.3 — Adaptive `SetStreamDelayMs`

### Symptom
[AecProcessor.Initialize](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs)
hardcodes `DesktopStreamDelayMs = 40` and `MobileStreamDelayMs = 150`.
Real speaker→mic latency varies by device, by Bluetooth codec, by audio
buffer size, and by user volume. APM's adaptive filter only converges
within ±20 ms of the true delay; outside that window AEC silently fails.

### Fix

1. Add to [IAudioOutputProvider](../../../src/BodyCam/Services/Audio/IAudioOutputProvider.cs):

   ```csharp
   /// <summary>
   /// Best-effort estimate, in ms, from PlayChunkAsync return to actual sound
   /// emission. Includes OS buffer + DAC + speaker enclosure delay.
   /// Default 40ms wired desktop; 80ms phone built-in; ~200ms BT.
   /// </summary>
   int EstimatedOutputLatencyMs { get; }

   /// <summary>Fired when the route changes (BT connect/disconnect, headphones, etc).</summary>
   event EventHandler? OutputRouteChanged;
   ```

2. Implement per provider:
   - [WindowsSpeakerProvider](../../../src/BodyCam/Platforms/Windows/WindowsSpeakerProvider.cs) — `_waveOut?.DesiredLatency ?? 80`.
   - [PhoneSpeakerProvider](../../../src/BodyCam/Platforms/Android/PhoneSpeakerProvider.cs) — `_audioTrack.BufferSizeInFrames * 1000 / sampleRate + 25`.
   - Bluetooth providers: `180–250` (codec dependent), expose route change.

3. Add to [AecProcessor](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs):

   ```csharp
   public void UpdateStreamDelay(int totalDelayMs)
   {
       if (!_initialized) return;
       lock (_lock)
       {
           if (_disposed) return;
           int clamped = Math.Clamp(totalDelayMs, 10, 500);
           WebRtcApmInterop.SetStreamDelayMs(_apm, clamped);
           _logger.LogInformation("APM stream delay set to {DelayMs}ms", clamped);
       }
   }
   ```

4. Wire from [AudioOutputManager](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs):
   on `SetActiveAsync` and on `OutputRouteChanged`, push the new value into
   `AecProcessor.UpdateStreamDelay`.

### Test plan
- Unit (`AecProcessorTests`): call `UpdateStreamDelay(5)` → assert clamped to 10. `(600)` → assert clamped to 500.
- Integration: switch provider Windows speaker → BT → speaker; assert APM receives 3 different delay values.
- Manual: connect a BT speaker mid-call; echo should clear within ~5 s instead of never.

### Acceptance
- [ ] `IAudioOutputProvider.EstimatedOutputLatencyMs` and `OutputRouteChanged` defined.
- [ ] All four output providers implement them.
- [ ] `AecProcessor.UpdateStreamDelay()` exists, clamps to [10, 500] ms.
- [ ] `AudioOutputManager` pushes delay updates on switch + route change.
- [ ] Manual route-switch test: AEC reconverges < 5 s.

---

## 1.4 — Eliminate per-frame GCHandle churn

### Symptom
[AecProcessor.ProcessStreamFrame](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs)
and `ProcessReverseStreamFrame` each allocate **four** `GCHandle.Alloc`
calls per 10 ms frame. At 5 frames per 50 ms chunk that's **400+ GCHandle
ops per second** — measurable GC pressure, occasional STW pauses on the
audio thread.

### Fix
Replace `GCHandle.Alloc(...,Pinned)` with `fixed` blocks; pre-allocate the
1-element pointer arrays as fields:

```csharp
private readonly IntPtr[] _srcPtrSlot = new IntPtr[1];
private readonly IntPtr[] _destPtrSlot = new IntPtr[1];

private unsafe void ProcessStreamFrame(float[] src, float[] dest)
{
    fixed (float* pSrc = src)
    fixed (float* pDest = dest)
    fixed (IntPtr* pSrcArr = _srcPtrSlot)
    fixed (IntPtr* pDestArr = _destPtrSlot)
    {
        _srcPtrSlot[0]  = (IntPtr)pSrc;
        _destPtrSlot[0] = (IntPtr)pDest;

        int err = WebRtcApmInterop.ProcessStream(
            _apm,
            (IntPtr)pSrcArr,
            _streamConfig, _streamConfig,
            (IntPtr)pDestArr);
        if (err != 0) _logger.LogTrace("ProcessStream error {Error}", err);
    }
}
```

Same for `ProcessReverseStreamFrame`. Add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
to [BodyCam.csproj](../../../src/BodyCam/BodyCam.csproj) if not already enabled.

### Test plan
- Existing AEC tests must still pass.
- Microbench (`BodyCam.Tests`): process 1000 frames, assert no `GCHandle` allocations
  via `GC.GetAllocatedBytesForCurrentThread()` delta < 8 KB total.

### Acceptance
- [ ] No `GCHandle.Alloc` in `AecProcessor` hot path.
- [ ] Existing AEC tests pass.
- [ ] Bench: < 8 KB allocated per 1000 frames processed.

---

## Execution order within Phase 1

1. **1.1** (smallest, biggest user-visible win) — drain on interruption.
2. **1.2** — sub-frame residuals (touches the same code; do together with 1.1 in one PR).
3. **1.3** — adaptive delay (changes interface; separate PR).
4. **1.4** — pinning cleanup (mechanical refactor; last).

## Files touched

- [src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs)
- [src/BodyCam/Agents/VoiceOutputAgent.cs](../../../src/BodyCam/Agents/VoiceOutputAgent.cs)
- [src/BodyCam/Services/Audio/IAudioOutputProvider.cs](../../../src/BodyCam/Services/Audio/IAudioOutputProvider.cs)
- [src/BodyCam/Services/Audio/AudioOutputManager.cs](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs)
- [src/BodyCam/Platforms/Windows/WindowsSpeakerProvider.cs](../../../src/BodyCam/Platforms/Windows/WindowsSpeakerProvider.cs)
- [src/BodyCam/Platforms/Android/PhoneSpeakerProvider.cs](../../../src/BodyCam/Platforms/Android/PhoneSpeakerProvider.cs)
- [src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothAudioOutputProvider.cs](../../../src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothAudioOutputProvider.cs)
- [src/BodyCam/BodyCam.csproj](../../../src/BodyCam/BodyCam.csproj) (`AllowUnsafeBlocks` if needed)
- New: `src/BodyCam.Tests/Services/Audio/WebRtcApm/AecProcessorResidualTests.cs`
