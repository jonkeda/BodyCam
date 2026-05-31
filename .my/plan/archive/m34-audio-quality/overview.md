# M34 — Audio Quality & Anti-Echo Improvements

**Status:** Proposed
**Depends on:** M24 (anti-echo, WebRTC APM landed), M32 (voice quality, empty)
**Replaces / supersedes:** M32 (voice-quality scope is folded in here)

## Phases

- [Phase 1 — Correctness fixes](phase-1-correctness/overview.md)
- [Phase 2 — Resampling](phase-2-resampling/overview.md)
- [Phase 3 — Threading & latency](phase-3-threading/overview.md)
- [Phase 4 — Platform coverage](phase-4-platform-coverage/overview.md)
- [Phase 5 — Voice quality polish](phase-5-polish/overview.md)
- [Phase 6 — Observability](phase-6-observability/overview.md)

---

## Why this milestone

M24 landed a working WebRTC APM-based AEC pipeline. It works, but several
concrete shortcuts and rough edges hurt either echo cancellation quality or
overall voice quality. M32 was scoped but never written. This milestone collects
all of the follow-ups into one improvement plan, ranked by impact.

The current state (verified in code):

- [src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs](../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs) — APM init, capture + render
  paths, hardcoded stream delays, linear resampling 24k↔48k, GCHandle-pinned
  float buffers per 10 ms frame.
- [src/BodyCam/Services/Audio/AudioResampler.cs](../../src/BodyCam/Services/Audio/AudioResampler.cs) — linear interpolation only.
- [src/BodyCam/Agents/VoiceInputAgent.cs](../../src/BodyCam/Agents/VoiceInputAgent.cs) — AEC runs on the capture
  thread, synchronous.
- [src/BodyCam/Agents/VoiceOutputAgent.cs](../../src/BodyCam/Agents/VoiceOutputAgent.cs) — `FeedRenderReference` is
  called when a chunk is queued for playback, not when it actually leaves the
  speaker.
- [src/BodyCam/Platforms/Windows/PlatformMicProvider.cs](../../src/BodyCam/Platforms/Windows/PlatformMicProvider.cs) — raw
  `WaveInEvent`; no platform AEC; no headphone detection.
- [src/BodyCam/Platforms/Android/PlatformMicProvider.cs](../../src/BodyCam/Platforms/Android/PlatformMicProvider.cs) — has
  hw `AcousticEchoCanceler` + `NoiseSuppressor` AND we now stack WebRTC APM on
  top.
- [src/BodyCam/Platforms/Android/Audio/AndroidBluetoothAudioProvider.cs](../../src/BodyCam/Platforms/Android/Audio/AndroidBluetoothAudioProvider.cs) —
  Bluetooth SCO at 16 kHz; AEC is effectively bypassed for BT (24 k→48 k math
  doesn't hit it the same way and BT speaker latency is not modeled).
- iOS — no `PlatformMicProvider.cs` exists yet.

---

## Goals

1. **Better echo cancellation** — measurable ERLE improvement, no audible echo
   on Windows/Android with built-in speakers.
2. **Better voice quality** — no aliasing / pumping artifacts, less GC churn,
   no chunk-boundary clicks.
3. **Adaptive, not hardcoded** — measure speaker→mic latency instead of
   guessing 40 ms / 150 ms.
4. **Cover all paths** — Bluetooth, iOS, headphones.
5. **Observable** — surface AEC metrics (ERLE, convergence, voice/noise
   levels) so future regressions are visible.

---

## Phase 1 — Correctness fixes (cheap, high impact)

### 1.1 — Drain APM render reference on interruption
**Symptom:** When `HandleInterruption()` clears the speaker buffer, the AEC has
already been fed render-reference frames for audio that will never reach the
speaker. The next ~150 ms of mic audio gets phantom subtraction, which can:
- Cancel the start of a real user reply (clipped first word).
- Cause AEC to mis-converge for several seconds.

**Fix:**
- Add `AecProcessor.ResetRenderReference()` that re-initializes APM state via
  `WebRtcApmInterop.Initialize()`.
- Call it from `VoiceOutputAgent.HandleInterruption()` immediately after
  `_audioOutput.ClearBuffer()`.

### 1.2 — Stop dropping sub-frame samples at chunk boundaries
**Symptom:** `AecProcessor.ProcessCapture` and `FeedRenderReference` iterate
in 10 ms frames and silently drop any tail < 480 samples. With the 24 k→48 k
linear resampler, sample counts can drift by 1–2 samples per chunk. Over
minutes this means render and capture clocks diverge, AEC convergence
degrades, and voice gets micro-truncated.

**Fix:**
- Maintain a residual buffer in `AecProcessor` (one for capture float[], one
  for render float[]).
- On each call: prepend residual, process all complete 480-sample frames,
  carry remainder forward.
- Output samples for capture must be re-aligned with original chunk length
  (use the residual+overlap so the C# caller sees the same byte count it sent
  in).

### 1.3 — Adaptive `SetStreamDelayMs`
**Symptom:** Delay is hardcoded (40 ms desktop, 150 ms mobile). Real
speaker→mic latency varies by device, by Bluetooth codec, and by speaker
volume / DSP. WebRTC APM only converges well within ±20 ms of the true delay.

**Fix:**
- Have the speaker provider expose `EstimatedOutputLatencyMs` (NAudio:
  `WaveOut.DesiredLatency`; WASAPI: `IAudioClient.GetStreamLatency`; Android:
  `AudioTrack.BufferSizeInFrames` / sample rate; BT: codec-specific +
  ~150 ms).
- In `AecProcessor`, expose `UpdateStreamDelay(int totalDelayMs)` and call it
  whenever the active output provider changes (route changes from speaker to
  BT, volume change, etc.).

### 1.4 — Clear AEC handle leak on AGC overrun
**Inspection finding:** in `ProcessStreamFrame` and `ProcessReverseStreamFrame`
we allocate four `GCHandle`s per 10 ms frame. At 50 ms chunks this is
4 × 5 = 20 GCHandle alloc/free pairs per chunk, ~400/sec. Cheap individually,
but a measurable source of GC pressure during long calls.

**Fix:**
- Switch to `fixed (float* pSrc = src) { ... }` blocks; pass `&pSrc` directly.
- Pre-allocate the IntPtr[1] arrays once in the field init.

---

## Phase 2 — Better resampling

### 2.1 — Replace linear resampler in AEC hot path
**Symptom:** `AudioResampler.Resample` uses linear interpolation. For
24 k→48 k upsampling this leaves an aliased mirror image of the 0–12 k band
around 12–24 k, which the APM then "sees" as noise and partially suppresses,
dulling the voice.

**Fix:**
- Bundle a polyphase FIR resampler. The WebRTC APM build already includes a
  `PushResampler` — expose it via P/Invoke (`webrtc_audio_processing` exports
  `WebRtcSpl_Resample48khzTo16khz` etc).
- OR: switch to processing at 48 kHz natively and only resample once at the
  Realtime API boundary (see 2.2).

### 2.2 — Native pipeline at 48 kHz, single resample at the API boundary
**Idea:** Today every 50 ms chunk goes 24k → 48k → AEC → 48k → 24k. Two
resamples per chunk, both linear, both lossy. Most platforms natively capture
at 48 kHz and then we downsample.

**Proposed pipeline:**
```
mic(48k) → AEC(48k) → resample 48→24 (once) → API
API → playback(48k or 24k native) → speaker
```

- `PlatformMicProvider` (Windows/Android) opens at 48 kHz directly.
- `AppSettings.InternalSampleRate = 48000`, `AppSettings.ApiSampleRate = 24000`.
- `AecProcessor` no longer resamples internally.
- One high-quality 48→24 resample step before `SendAudioChunkAsync`.

This is a larger refactor and should be sequenced after Phase 1.

---

## Phase 3 — Threading & latency

### 3.1 — Move AEC processing off the capture thread
**Symptom:** `VoiceInputAgent.OnAudioChunk` runs `_aec.ProcessCapture` on the
NAudio / Android `AudioRecord` callback thread. Resampling + AEC can take
3–8 ms; if it ever spikes (GC, page fault) the capture thread blocks and we
lose mic samples.

**Fix:**
- Introduce a bounded `Channel<byte[]>` inside `AudioInputManager` between the
  provider and the AEC stage.
- Drop-oldest policy on overflow with a metric counter.
- AEC runs on a dedicated `Task.Run` consumer.

### 3.2 — Output jitter buffer with adaptive sizing
**Symptom:** Realtime API delivers chunks at irregular cadence; current
playback path queues directly. On Windows `WaveOutEvent` glitches when the
queue underruns.

**Fix:**
- Small adaptive jitter buffer (target 40 ms, max 200 ms) inside
  `AudioOutputManager`.
- Drain monotonically; when a buffer underrun is detected, re-target up by
  one chunk; on overflow, target down.

---

## Phase 4 — Platform coverage

### 4.1 — iOS native AEC (`kAudioUnitSubType_VoiceProcessingIO`)
- Add `src/BodyCam/Platforms/iOS/PlatformMicProvider.cs` and matching
  speaker provider that share a single `AVAudioEngine` with the
  VoiceProcessingIO audio unit.
- This is hardware-accelerated, lower latency than WebRTC APM on iOS, and
  Apple gates it on real signal paths.
- Disable WebRTC APM on iOS when this provider is active (or keep it as a
  fallback for AirPods + macOS Catalyst quirks).

### 4.2 — Bluetooth path AEC
- `AndroidBluetoothAudioProvider` resamples to 24 k after capture. Today
  the AEC reference is fed from `VoiceOutputAgent` regardless of route, but
  the BT speaker has 100–250 ms more latency than the built-in speaker, and
  the stream delay isn't updated → AEC never converges over BT.
- Wire `EstimatedOutputLatencyMs` through `IAudioOutputProvider` and call
  `AecProcessor.UpdateStreamDelay` on route change.
- Test BT-mic + built-in-speaker (common scenario) and built-in-mic +
  BT-speaker separately — they require different delays.

### 4.3 — Headphone detection → AEC bypass
- When wired or Bluetooth headphones are connected, there is no acoustic
  echo path. AEC adds CPU cost and slight quality loss for nothing.
- Detect headphones (Android: `AudioManager.IsWiredHeadsetOn`,
  `AudioDeviceInfo.Type`; Windows: WASAPI device role / form factor; iOS:
  `AVAudioSession.CurrentRoute`).
- Toggle `AecProcessor.IsEnabled` on route change. Keep noise suppression
  on; turn AGC down to soft mode.

### 4.4 — Windows: try Voice Capture DMO as a fallback
- For users on devices where WebRTC APM's AEC underperforms (large desktop
  speakers, sub-bass), expose a "Use OS Voice Capture" toggle that switches
  the input provider to one backed by the Windows Voice Capture DMO
  (`CWMAudioAEC`). Makes us a thin client of OS AEC, like Teams / Zoom.

---

## Phase 5 — Voice quality polish

### 5.1 — Tune AGC
- Current config: `mode=AdaptiveDigital, target=-3 dBFS, compression=9 dB,
  limiter=on`. `-3 dBFS` is hot — combined with `semantic_vad` it produces
  clipping artifacts on plosives and false barge-ins.
- Switch target to `-9 dBFS`, compression `6 dB`. Re-test on quiet/loud
  speakers.

### 5.2 — Tune noise suppression
- Currently `level=2` (High). High suppression introduces "musical noise"
  / metallic artifacts when the speaker has a mild fan or HVAC background.
- Default to `level=1` (Moderate) and expose a setting.

### 5.3 — Mic ducking during playback (optional, per-user)
- Even with AEC, some users prefer push-to-talk feel. Add an opt-in setting
  "Pause mic while assistant is speaking" that gates `_audioSink` invocation
  in `VoiceInputAgent` while `Tracker.IsPlaying`.
- This loses barge-in but eliminates the last 5 % of edge-case echo.

### 5.4 — Soft fade on barge-in
- `HandleInterruption()` clears the speaker buffer instantly, which causes a
  click. Add a 30 ms linear fade-out of the last queued chunk before clearing.

---

## Phase 6 — Observability

### 6.1 — Surface AEC metrics
- WebRTC APM exposes `Statistics`: `EchoReturnLoss`, `EchoReturnLossEnhancement`,
  `DelayMs`, `ResidualEchoLikelihood`. Add P/Invoke bindings.
- Sample at 1 Hz, log + push to a debug overlay (gated by
  `AppSettings.DebugMode`).
- Expected target: ERLE > 20 dB after 2 s convergence, residual likelihood < 0.1.

### 6.2 — Capture/render clock-drift counter
- Track samples in vs samples out per minute. Alarm at >0.5 % drift —
  indicates resampler bug or sub-frame leakage (1.2).

### 6.3 — A/B-able AEC bypass for blind tests
- A diagnostic toggle in settings: "Disable AEC (recording test)".
- Lets us record a 10 s sample with and without AEC for offline comparison
  during regression testing.

---

## Suggested execution order

| Order | Item                                  | Cost   | Impact   |
|-------|---------------------------------------|--------|----------|
| 1     | 1.1 Drain reference on interruption   | XS     | High     |
| 2     | 1.2 Sub-frame residuals               | S      | High     |
| 3     | 1.3 Adaptive stream delay             | M      | High     |
| 4     | 6.1 AEC metrics                       | S      | Medium   |
| 5     | 5.1 / 5.2 AGC + NS tuning             | XS     | Medium   |
| 6     | 1.4 Pinning / GCHandle cleanup        | S      | Low      |
| 7     | 4.3 Headphone detection               | S      | Medium   |
| 8     | 3.1 Capture-thread offload            | M      | Medium   |
| 9     | 2.1 Better resampler                  | M      | Medium   |
| 10    | 4.2 Bluetooth latency wiring          | M      | High     |
| 11    | 4.1 iOS VoiceProcessingIO             | L      | High     |
| 12    | 2.2 Native 48 kHz pipeline            | L      | Medium   |
| 13    | 3.2 Adaptive jitter buffer            | M      | Medium   |
| 14    | 5.3 / 5.4 ducking + soft fade         | S      | Low      |
| 15    | 4.4 Windows Voice Capture DMO         | M      | Low      |

Phases 1 + part of 5/6 are a single PR's worth of work and would already
remove the most noticeable artifacts. Phase 2.2 / 4.1 are the biggest
follow-ons.

---

## Exit criteria

- [ ] No audible echo on Android (built-in speaker), Windows (built-in
      speaker), iOS (built-in speaker), with the assistant talking over the
      user.
- [ ] No "first word clipped" after a barge-in interruption.
- [ ] ERLE ≥ 20 dB after 2 s of double-talk on all three platforms.
- [ ] Capture/render sample drift < 0.5 % over a 10 minute call.
- [ ] AEC settings (on/off, NS level, AGC target) reachable from the
      settings page (M30 Phase 4).
- [ ] Bluetooth speaker echo cancelled within 5 s of route switch.
- [ ] Debug overlay shows live ERLE / delay / residual-echo numbers.

---

## Out of scope

- Switching off WebRTC APM and back to platform AEC on Android — current
  stack (HW AEC + APM) is fine; just needs the fixes above.
- Browser-WebRTC approach (see [m24-anti-echo/webrtc/browser-webrtc-approach.md](../m24-anti-echo/webrtc/browser-webrtc-approach.md)) —
  parked unless WebRTC APM proves unfixable.
- New voice models / TTS quality (that's a separate Realtime-API milestone).
