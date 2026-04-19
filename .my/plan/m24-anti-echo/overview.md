# M24 — Anti-Echo

## Problem

Echo persists on Android and Windows despite:
- Android `AcousticEchoCanceler` + `NoiseSuppressor` enabled on `PlatformMicProvider`
- OpenAI Realtime API `noise_reduction: "near_field"` now sent in session config

The hardware AEC on Android is device-dependent and often ineffective. Windows has no AEC at all (NAudio `WaveInEvent` is raw capture). Server-side noise reduction helps but can't fully cancel echo — it doesn't have a reference signal of what's being played.

## Why hardware AEC isn't enough

AEC needs a **reference signal** — the exact audio being played through the speaker — to subtract from the mic input. Android's `AcousticEchoCanceler` gets this implicitly from the OS audio path, but:
- Many devices have poor AEC implementations
- The `AudioSource.VoiceCommunication` hint doesn't guarantee the OS routes the reference signal
- On Windows, NAudio captures raw mic data with no AEC pipeline at all

## Option: WebRTC Audio Processing Module (APM)

The WebRTC APM is the gold standard for real-time echo cancellation. Google uses it in Chrome, Meet, and Android.

### Available via NuGet

**`SoundFlow.Extensions.WebRtc.Apm`** (v1.4.0, MIT license)
- Wraps the native WebRTC APM library from the PulseAudio/WebRTC project
- Provides `WebRtcApmModifier` with AEC, noise suppression, AGC, high-pass filter
- .NET 8+ compatible
- **Caveat**: Project maintainer on hiatus Jan 2026–Feb 2027

### Platform support

SoundFlow core supports: **Windows, macOS, Linux, Android, iOS, FreeBSD**

The WebRTC APM extension ships native binaries — need to verify which runtimes are included in the NuGet package. The native `webrtc_audio_processing` library can be compiled for all platforms but the package may not ship iOS/Android binaries. If missing, we'd need to build them from the [PulseAudio WebRTC APM source](https://gitlab.freedesktop.org/pulseaudio/webrtc-audio-processing).

### Sample rate constraint

WebRTC APM only supports: **8000, 16000, 32000, or 48000 Hz**

Our app uses **24000 Hz** (`AppSettings.SampleRate`). Options:
1. Change app sample rate to 48000 Hz (breaking — affects Realtime API, all providers)
2. Resample 24000→48000 before AEC, then 48000→24000 after (adds latency, ~2-4ms)
3. Use 16000 Hz for AEC processing (lower quality but closer to 24000)

**Recommendation**: Resample to 48000 Hz for AEC processing. We already have `AudioResampler` used in the Bluetooth path.

## How it works with ChatGPT Realtime API

The Realtime API sends and receives PCM audio. The echo cancellation would sit in the **client-side audio pipeline**, not the API:

```
Mic → [Resample 24k→48k] → [WebRTC AEC] → [Resample 48k→24k] → Realtime API
                                  ↑ reference signal
Speaker ← Realtime API response PCM ──────────────┘
```

1. **Capture path**: Mic audio is resampled to 48kHz, passed through WebRTC AEC, resampled back to 24kHz, then sent to the Realtime API
2. **Reference signal**: The audio chunks received from the Realtime API (played through the speaker) are fed into AEC as the "render" reference
3. **Playback path**: Unchanged — PCM from the API goes straight to the speaker

The key integration point is feeding the speaker output as the AEC reference signal. This requires the `AudioOutputManager` to copy each playback chunk to the AEC module.

## Integration approach (without SoundFlow dependency)

We don't need the full SoundFlow engine. We can use the **native WebRTC APM library directly** via P/Invoke:

### Option A: Use SoundFlow.Extensions.WebRtc.Apm (easier)
- Add the NuGet package
- Extract just the native P/Invoke wrapper (`AudioProcessingModule.cs`)
- Process audio chunks inline in our existing pipeline
- **Effort**: Medium — need to wire reference signal, handle resampling
- **Risk**: Package maintainer on hiatus; native binaries may not include all platforms

### Option B: Direct native WebRTC APM via P/Invoke (more control)
- Build `webrtc_audio_processing` native lib for each platform from source
- Write thin P/Invoke wrapper (the API surface is small)
- **Effort**: High — cross-compilation for Android/iOS/Windows, CMake build scripts
- **Pros**: Full control, no dependency risk
- **Cons**: Significant build infrastructure work

### Option C: Platform-specific AEC only (current approach, enhanced)
- **Android**: Use `AudioSource.VoiceCommunication` + `AcousticEchoCanceler` (already done)
- **Windows**: Use Windows Voice Capture DMO (`CWMAudioAEC`) via NAudio/MediaFoundation
- **iOS**: Use `AVAudioEngine` with `AVAudioUnitEffect` for Apple's built-in AEC
- **Effort**: Medium per platform, but no cross-platform consistency
- **Pros**: Uses OS-optimized AEC; no native build needed
- **Cons**: Different behavior per platform; iOS needs separate implementation

## iOS considerations

### With WebRTC APM
- Need to compile `webrtc_audio_processing` for iOS arm64 (and simulator x86_64)
- NuGet package may not include iOS binaries — would need to build and package
- Once built, same C# code works across all platforms

### Without WebRTC APM (platform-native)
- iOS has excellent built-in AEC via `AVAudioEngine` and `Voice Processing I/O` audio unit
- Set `AVAudioSession.Category` to `.playAndRecord` with `.defaultToSpeaker` option
- The `kAudioUnitSubType_VoiceProcessingIO` audio unit provides AEC automatically
- Apple's AEC is very high quality on all iOS devices
- **This is what most iOS VoIP apps use**

## Recommendation

| Phase | Action | Platforms | Effort |
|-------|--------|-----------|--------|
| 1 | **Windows**: Add Voice Capture DMO for AEC | Windows | Medium |
| 1 | **Android**: Verify current AEC is wired correctly, test on multiple devices | Android | Low |
| 2 | **iOS**: Use `kAudioUnitSubType_VoiceProcessingIO` | iOS | Medium |
| 3 | If platform AEC is still insufficient, add WebRTC APM as cross-platform fallback | All | High |

### Why not jump straight to WebRTC APM?
- Platform-native AEC is simpler to implement and maintain
- Apple's iOS AEC is better than WebRTC on iOS devices
- Android's hardware AEC works well on flagship devices
- WebRTC APM is the fallback if platform-specific solutions fail

## Files to change

### Phase 1 — Windows AEC
- `src/BodyCam/Platforms/Windows/PlatformMicProvider.cs` — Replace `WaveInEvent` with WASAPI + Voice Capture DMO
- May need NAudio's `DmoStream` or `WasapiCapture`

### Phase 1 — Android verification
- `src/BodyCam/Platforms/Android/PlatformMicProvider.cs` — Already has AEC, verify it's working
- Add logging to confirm `AcousticEchoCanceler.IsAvailable` and `SetEnabled` result

### Phase 2 — iOS
- `src/BodyCam/Platforms/iOS/PlatformMicProvider.cs` — New file, use `AVAudioEngine` with voice processing IO

### Reference signal plumbing (all approaches)
- `src/BodyCam/Services/Audio/AudioOutputManager.cs` — Add `OnChunkPlayed` event to feed reference signal to AEC
- `src/BodyCam/Services/Audio/IAudioInputProvider.cs` — Add optional `FeedReferenceAudio(byte[] playbackChunk)` method
