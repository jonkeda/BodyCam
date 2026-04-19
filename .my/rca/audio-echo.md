# RCA — Audio Echo on Android and Windows

## Problem
Audio echo is present on both Android mobile and Windows desktop when using the app. The AI's spoken output is picked up by the microphone and fed back into the Realtime API, creating a feedback loop.

## Root Cause

The app plays AI audio through the speaker while simultaneously capturing microphone input. Neither the Android `PlatformMicProvider` (phone mic) nor the Windows `PlatformMicProvider` (NAudio `WaveInEvent`) apply any echo cancellation. The speaker output bleeds into the mic input.

**Android Bluetooth** (`AndroidBluetoothAudioProvider`) already enables `AcousticEchoCanceler` and `NoiseSuppressor` — but the main phone mic path does not.

### Current Audio Flow
```
Mic (PlatformMicProvider) → raw PCM → Realtime API → response PCM → Speaker (AudioTrack / WaveOut)
                                          ↑                                        |
                                          └────── echo (speaker → mic bleed) ──────┘
```

---

## Options — Android

### Option A: Enable Android Platform AEC (Recommended — minimal change)
- Use `AudioSource.VoiceCommunication` (already done) **plus** attach `AcousticEchoCanceler` and `NoiseSuppressor` to the `AudioRecord` session.
- This is exactly what `AndroidBluetoothAudioProvider` already does — just missing from `PlatformMicProvider`.
- **Effort**: ~5 lines in `PlatformMicProvider.StartAsync()`
- **Pros**: Uses hardware/DSP AEC built into Android; zero latency; battle-tested
- **Cons**: Quality varies by device; some cheap devices have poor AEC

### Option B: Software AEC (WebRTC / Speex)
- Use a software echo canceller (e.g. `WebRtcVad`, SpeexDSP, or RNNoise via native interop).
- Feed the speaker output as the reference signal.
- **Effort**: Medium — need native library binding + reference signal plumbing
- **Pros**: Consistent across devices; high quality
- **Cons**: CPU cost; latency; native library packaging for Android

### Option C: Server-side noise reduction (OpenAI Realtime API)
- Already configured: `NoiseReduction = "near_field"` in AppSettings.
- The Realtime API applies server-side echo reduction when `noise_reduction` is set.
- **Question**: Is `near_field` actually being sent in the session config? Verify `RealtimeMessages`.
- **Effort**: Zero if already wired; otherwise a one-line fix in session setup
- **Pros**: No client-side work; OpenAI handles it
- **Cons**: Only works during Realtime API sessions; latency; relies on server quality

---

## Options — Windows

### Option A: NAudio with WASAPI Loopback AEC (Recommended)
- Replace `WaveInEvent` with WASAPI capture using `AudioClientStreamFlags.AutomaticGainControl` or use the Windows Audio Session API (WASAPI) with `AUDIOCLIENT_STREAMFLAGS_AUTOCONVERTPCMIX`.
- Better: use `NAudio.Wave.WasapiCapture` with acoustic echo cancellation via the Windows Voice Capture DSP (`IMediaObject`).
- **Effort**: Medium — need to switch from `WaveInEvent` to WASAPI + Windows AEC DMO
- **Pros**: Hardware-accelerated; consistent on Windows 10+
- **Cons**: More complex NAudio setup; DMO interop

### Option B: Windows Audio Echo Cancellation DSP (Voice Capture DMO)
- Windows provides a built-in echo cancellation DMO (`CWMAudioAEC`).
- Feed the speaker render stream as the AEC reference.
- Works with NAudio via `DmoStream` or `MediaFoundationTransform`.
- **Effort**: Medium-high — DMO plumbing, render stream reference
- **Pros**: Best quality on Windows; hardware-assisted
- **Cons**: Complex setup; Windows-only (fine for this app)

### Option C: Software AEC (WebRTC / SpeexDSP)
- Same as Android Option B — cross-platform consistency.
- **Effort**: Medium
- **Pros**: Same approach on both platforms
- **Cons**: CPU; latency; native dependency

### Option D: Server-side noise reduction (already configured)
- Same as Android Option C — verify it's being sent.

---

## Recommendation

| Platform | Quick Win | Best Quality |
|----------|-----------|--------------|
| Android  | **Option A** — enable AcousticEchoCanceler on PlatformMicProvider (~5 lines) | Option A + verify server-side noise reduction (Option D) |
| Windows  | **Option D** — verify server-side noise reduction is wired | Option A or B — WASAPI AEC / Voice Capture DMO |

### Immediate fix (both platforms)
1. **Android**: Add `AcousticEchoCanceler` + `NoiseSuppressor` to `PlatformMicProvider.StartAsync()` (copy from `AndroidBluetoothAudioProvider`)
2. **Windows**: Verify `noise_reduction: "near_field"` is sent in Realtime session config
3. **Both**: If echo persists, consider software AEC (WebRTC/SpeexDSP) as a fallback

---

## Files to Change

- `src/BodyCam/Platforms/Android/PlatformMicProvider.cs` — add AEC + NS
- `src/BodyCam/Services/Realtime/RealtimeMessages.cs` — verify noise_reduction is sent
- `src/BodyCam/Platforms/Windows/PlatformMicProvider.cs` — (later) WASAPI AEC
