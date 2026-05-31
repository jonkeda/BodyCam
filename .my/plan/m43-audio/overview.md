# M43 - Audio

**Status:** Proposed source of truth
**Supersedes:** M24 anti-echo and M34 audio quality
**Keeps:** M12 input audio and M13 output audio as the provider architecture

M43 consolidates the echo cancellation and voice-quality work into one
provider-aware audio roadmap.

## Core Decision

Echo cancellation is not a global always-on feature. It should depend on the
active audio route and sound provider.

The practical rule:

```text
Direct laptop or phone speaker -> echo risk -> enable AEC
Headset, headphones, AirPods, glasses speakers -> isolated route -> bypass AEC
Silent output / no local playback -> no speaker echo path -> bypass AEC
```

Noise suppression and gain control are related to voice quality, but they are
not the same as echo cancellation. M43 should separate those controls so using
a headset can bypass AEC without necessarily losing useful mic cleanup.

## Why This Replaces The Old Roadmaps

We tried several paths:

- Android hardware AEC through `AudioSource.VoiceCommunication`,
  `AcousticEchoCanceler`, and `NoiseSuppressor`.
- OpenAI Realtime `near_field` noise reduction.
- WebRTC APM with render-reference audio from the playback path.
- A 48 kHz internal pipeline with 24 kHz API boundary resampling.
- Bounded capture processing and output jitter buffering.
- Route monitors that disable AEC when headphones are detected.
- iOS VoiceProcessingIO as the preferred Apple path.
- A Windows Voice Capture DMO fallback stub.

The important learning is simpler than the implementation history: echo is a
local speaker-to-mic problem. It appears when the assistant is played through a
laptop or phone speaker. It should not be treated the same when audio is routed
to a headset or other isolated provider.

## Current Code State

The current implementation already contains much of the audio foundation:

- `AppSettings.InternalSampleRate = 48000` and `ApiSampleRate = 24000`.
- [VoiceInputAgent.cs](../../../src/BodyCam/Agents/VoiceInputAgent.cs)
  receives 48 kHz post-processing audio and resamples to 24 kHz for the API.
- [VoiceOutputAgent.cs](../../../src/BodyCam/Agents/VoiceOutputAgent.cs)
  resamples API audio from 24 kHz to 48 kHz, feeds render reference audio, and
  plays through the output manager.
- [IAecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/IAecProcessor.cs)
  abstracts the WebRTC APM path and Windows fallback path.
- [AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs)
  implements WebRTC APM, stream delay updates, residual buffers, AGC/NS tuning,
  render reset, and optional statistics.
- [AudioInputManager.cs](../../../src/BodyCam/Services/Audio/AudioInputManager.cs)
  moves capture processing to a bounded channel and bypasses WebRTC APM when
  platform-native AEC is active.
- [AudioOutputManager.cs](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs)
  owns active output selection, stream-delay updates, and jitter buffering.
- [AecBypassManager.cs](../../../src/BodyCam/Services/Audio/AecBypassManager.cs)
  disables AEC when the route monitor reports headphones.
- Platform route monitors exist for Windows, Android, and iOS.

Current limitation: `IAecProcessor.IsEnabled = false` bypasses the whole APM
processor, not only acoustic echo cancellation. If we still want NS/AGC on
headsets, M43 needs to split "echo cancellation" from "voice cleanup".

## Provider Echo Policy

M43 should introduce provider-declared audio capabilities. Provider IDs are
still useful for persistence, user selection, and log correlation, but they
must not decide behavior. The policy service should not switch on provider
IDs; echo behavior comes from provider properties and route state.

If the current input/output provider interfaces do not expose enough
information, change the interfaces rather than encoding knowledge in ID
parsing.

Suggested provider interface additions:

```csharp
public interface IAudioInputProvider : IAsyncDisposable
{
    // Existing provider members stay here.
    AudioInputCapabilities InputCapabilities { get; }
}

public interface IAudioOutputProvider : IAsyncDisposable
{
    // Existing provider members stay here.
    AudioOutputCapabilities OutputCapabilities { get; }
}

public sealed record AudioInputCapabilities(
    bool HasPlatformEchoCancellation,
    bool PlatformEchoCancellationActive,
    int EstimatedInputLatencyMs);

public sealed record AudioOutputCapabilities(
    EchoPathKind EchoPathKind,
    bool NeedsEchoCancellation,
    bool IsAcousticallyIsolated,
    bool SupportsRenderReference,
    int EstimatedOutputLatencyMs);

public enum EchoPathKind
{
    DirectDeviceSpeaker,
    ExternalRoomSpeaker,
    IsolatedHeadset,
    GlassesOrWearable,
    NoLocalPlayback,
    Unknown
}
```

`NeedsEchoCancellation` is intentionally provider-owned. A Windows speaker
provider, phone speaker provider, or future room-speaker provider can declare
that echo cancellation is needed. Headset-like providers can declare that the
route is acoustically isolated and does not need echo cancellation.

The route monitor still contributes dynamic state, such as headphones
connected, Bluetooth active, or no local playback. When provider capabilities
and route state disagree, the policy should choose the safer behavior and log
the conflict.

Suggested policy shape:

```csharp
public sealed record AudioRoutePolicy(
    AudioInputCapabilities InputCapabilities,
    AudioOutputCapabilities OutputCapabilities,
    bool HasLocalPlayback,
    bool RouteReportsHeadphones,
    bool RouteReportsBluetoothAudio,
    int EstimatedRoundTripLatencyMs,
    AecMode AecMode,
    VoiceCleanupMode VoiceCleanupMode,
    string Explanation);

public enum AecMode
{
    Off,
    PlatformNative,
    WebRtcApm,
    WindowsDmoFallback
}

public enum VoiceCleanupMode
{
    Off,
    NoiseSuppressionOnly,
    NoiseSuppressionAndAgc
}
```

Policy examples:

| Input capabilities | Output capabilities | Route state | Expected policy |
| --- | --- | --- | --- |
| No platform AEC | `NeedsEchoCancellation=true`, direct device speaker | no headphones | `WebRtcApm`, direct speaker latency |
| Any mic | `IsAcousticallyIsolated=true`, headset-like output | headphones connected | `Off`, optional cleanup only |
| Platform AEC active | `NeedsEchoCancellation=true`, phone speaker | no headphones | `PlatformNative`, shared communication session |
| Any mic | `IsAcousticallyIsolated=true`, Bluetooth headset | Bluetooth headset route | `Off`, optional cleanup only |
| VoiceProcessingIO active | `NeedsEchoCancellation=true`, iPhone speaker | no headphones | `PlatformNative`, VoiceProcessingIO |
| Any mic | `EchoPathKind.NoLocalPlayback` | Silent output mode | `Off`, no render reference |

## Windows Plan

### Current Windows Path

- Input: [PlatformMicProvider.cs](../../../src/BodyCam/Platforms/Windows/PlatformMicProvider.cs)
  uses NAudio `WaveInEvent`.
- Output: [WindowsSpeakerProvider.cs](../../../src/BodyCam/Platforms/Windows/WindowsSpeakerProvider.cs)
  uses NAudio `WaveOutEvent`.
- AEC: WebRTC APM is the main Windows path.
- Route detection:
  [WindowsRouteMonitor.cs](../../../src/BodyCam/Platforms/Windows/WindowsRouteMonitor.cs)
  uses endpoint change notifications and friendly-name heuristics.
- Fallback:
  [VoiceCaptureDmoAecProcessor.cs](../../../src/BodyCam/Platforms/Windows/Audio/VoiceCaptureDmoAecProcessor.cs)
  is currently a passthrough stub behind `WindowsUseVoiceCaptureDmo`.

### Windows Decisions

- Enable WebRTC APM when the active output provider declares
  `NeedsEchoCancellation = true` and no platform-native AEC is active.
- The built-in Windows speaker provider should declare
  `EchoPathKind.DirectDeviceSpeaker`, `NeedsEchoCancellation = true`, and
  `SupportsRenderReference = true`.
- Headphones, headsets, Bluetooth headset routes, and glasses audio routes
  should be represented by providers that declare
  `IsAcousticallyIsolated = true` and `NeedsEchoCancellation = false`.
- Improve route classification beyond friendly-name matching. Prefer endpoint
  form factor and device role when available. Route classification may refine
  the provider capabilities at runtime, but policy should not branch on
  provider IDs.
- Keep the Windows DMO path low priority unless a target machine proves WebRTC
  APM cannot handle its speakers.

### Windows Acceptance

- Direct laptop speaker: no assistant self-reply loop during normal volume
  playback.
- Headset or headphones: AEC disabled within one route-change event.
- Bluetooth headset: no WebRTC APM processing unless explicitly forced for
  diagnostics.
- Stream delay changes when the active output provider changes.
- Debug logs clearly show the active `AudioRoutePolicy`.

## Android Plan

### Current Android Path

- Input: [PlatformMicProvider.cs](../../../src/BodyCam/Platforms/Android/PlatformMicProvider.cs)
  uses `AudioRecord`, `AudioSource.VoiceCommunication`, hardware AEC, and NS.
- Output: [PhoneSpeakerProvider.cs](../../../src/BodyCam/Platforms/Android/PhoneSpeakerProvider.cs)
  uses `AudioTrack` in voice communication mode and tries to share the mic
  audio session.
- Route detection:
  [AndroidRouteMonitor.cs](../../../src/BodyCam/Platforms/Android/AndroidRouteMonitor.cs)
  detects wired, USB, Bluetooth A2DP, and Bluetooth SCO routes.
- Bluetooth mic:
  [AndroidBluetoothAudioProvider.cs](../../../src/BodyCam/Platforms/Android/Audio/AndroidBluetoothAudioProvider.cs)
  captures over SCO and resamples into the internal sample rate.

### Android Decisions

- Prefer Android platform-native AEC when the output provider declares
  `NeedsEchoCancellation = true`, especially the phone speaker provider.
- Do not stack WebRTC APM over Android platform AEC by default.
- Bypass AEC for wired headsets, USB headsets, Bluetooth SCO, Bluetooth A2DP,
  and glasses/headset-style output when their providers declare
  `IsAcousticallyIsolated = true`.
- Treat external Bluetooth speakers differently from Bluetooth headsets if the
  platform exposes enough route detail. A room speaker is still an echo risk;
  a headset is not. This distinction should be expressed through provider
  capabilities, not provider ID matching.
- Verify the session-sharing assumption between `AudioRecord` and `AudioTrack`
  on real devices.

### Android Acceptance

- Direct phone speaker: no assistant self-reply loop.
- Wired headset and Bluetooth headset: AEC bypassed; speech remains clear.
- Route changes update policy without restarting the app.
- Bluetooth SCO capture remains stable at the internal 48 kHz boundary.
- Logs identify whether platform AEC or WebRTC APM is active.

## iOS Plan

### Current iOS Path

- Input: [PlatformMicProvider.cs](../../../src/BodyCam/Platforms/iOS/PlatformMicProvider.cs)
  uses `AVAudioEngine` and enables VoiceProcessingIO when
  `IosUsePlatformAecOnly` is true.
- Output: [PhoneSpeakerProvider.cs](../../../src/BodyCam/Platforms/iOS/PhoneSpeakerProvider.cs)
  shares the same `AVAudioEngine` with an `AVAudioPlayerNode`.
- Route detection:
  [IosRouteMonitor.cs](../../../src/BodyCam/Platforms/iOS/IosRouteMonitor.cs)
  watches `AVAudioSession` route changes.

### iOS Decisions

- Prefer VoiceProcessingIO when the output provider declares
  `NeedsEchoCancellation = true`, especially iPhone speaker playback.
- Bypass WebRTC APM when VoiceProcessingIO is active.
- Bypass AEC for AirPods, Bluetooth HFP/A2DP, Bluetooth LE, wired headphones,
  and other isolated routes when their providers declare
  `IsAcousticallyIsolated = true`.
- Keep WebRTC APM as a diagnostic fallback only, not the default iOS path.
- Validate on a real iPhone, not only simulator/build output.

### iOS Acceptance

- Direct iPhone speaker: VoiceProcessingIO active, no assistant self-reply
  loop.
- AirPods/headset: AEC bypassed, no double-processing artifacts.
- Route changes fire and update policy reliably.
- Real-device run confirms mic permission, engine startup, and playback.
- No WebRTC APM stacking when `IosUsePlatformAecOnly` is true.

## Implementation Phases

### Phase 1 - Provider-Based Echo Policy

- Add `AudioInputCapabilities` and `AudioOutputCapabilities` to the input and
  output provider interfaces.
- Add an `AudioRoutePolicy` or `IAecPolicyService`.
- Derive policy from provider capability properties, `IRouteMonitor`, and
  output mode (`Speak` / `Silent`).
- Replace the current broad `AecBypassManager` rule with the policy result.
- Do not switch on provider IDs for policy decisions. IDs are allowed for
  persistence, UI labels, and structured diagnostics only.
- Log policy changes as structured events.

Acceptance:

- AEC is enabled for direct speakers only.
- AEC is disabled for headsets and Silent mode.
- The app can explain why AEC is on or off from provider capabilities and route
  state.

Details:

- [phase-1-provider-capabilities-policy.md](phase-1-provider-capabilities-policy.md)

### Phase 2 - Split Echo Cancellation From Voice Cleanup

- Separate AEC enablement from NS/AGC enablement.
- Keep optional noise suppression for headset routes if it improves clarity.
- Add settings names that match behavior:
  - `EchoCancellationMode`
  - `NoiseSuppressionLevel`
  - `AgcTargetLevelDbfs`
  - `PauseMicWhilePlaying`

Acceptance:

- Headset route can run with AEC off and cleanup on.
- Direct speaker route can run with AEC plus cleanup.
- Diagnostic toggles do not require restart.

Details:

- [phase-2-echo-vs-cleanup.md](phase-2-echo-vs-cleanup.md)

### Phase 3 - Platform Validation

- Windows: direct laptop speaker, wired headset, Bluetooth headset, HeyCyan
  audio route.
- Android: direct phone speaker, wired/USB headset, Bluetooth SCO headset,
  HeyCyan audio route.
- iOS: direct iPhone speaker, AirPods, wired/USB-C headset.

Acceptance:

- Each route has a recorded policy log.
- Direct speakers do not loop the assistant back into itself.
- Headsets do not run unnecessary AEC.

Details:

- [phase-3-platform-route-validation.md](phase-3-platform-route-validation.md)

### Phase 4 - Diagnostics

- Surface current policy in the debug overlay.
- Keep ERLE/residual echo metrics when the native WebRTC build exposes them.
- Add a simple A/B capture flow for direct speaker versus headset routes.
- Record route transitions with provider capability snapshots, route state,
  provider IDs as labels, and estimated latency.

Acceptance:

- A tester can tell which route was active, which AEC mode was selected, and
  why.
- A 10 second WAV capture can be saved for regression comparison when debug
  mode is enabled.

Details:

- [phase-4-diagnostics.md](phase-4-diagnostics.md)

### Phase 5 - Brinell Audio Automation

- Add test-only audio route providers with configurable capabilities for direct
  speaker, headset, Bluetooth headset, glasses, and no-local-playback routes.
- Add a fake `IRouteMonitor` and spy `IAecProcessor` so Brinell can assert
  policy decisions deterministically.
- Add a fake realtime client and echo injector to simulate the assistant
  hearing itself through direct speakers.
- Expose the current audio policy through a test service accessor or
  `AudioPolicyDebugLabel`.

Acceptance:

- Direct speaker, headset, Bluetooth headset, glasses, Silent mode, route
  change, stream delay, interruption, and simulated echo-loop scenarios are
  covered automatically.
- Tests prove AEC behavior follows provider capability properties, not
  provider ID strings.
- Windows Brinell tests can run these scenarios without real audio hardware.

Details:

- [phase-5-brinell-automation.md](phase-5-brinell-automation.md)

### Phase 6 - Realtime Echo Canary

- Add a real-world canary where the assistant speaks a known phrase while the
  room stays silent.
- Fail if Realtime transcribes the assistant's canary phrase as user speech.
- Fail if a second assistant response starts during the silent window.
- Add a local audio correlation score so echo can be measured even when
  Realtime VAD does not create a transcript.
- Keep deterministic unit tests for the verdict engine and opt-in
  Brinell/Appium runs for physical routes.

Acceptance:

- Transcript canary detection covers pass and failure cases.
- Audio correlation detects delayed speaker echo and reports low scores for
  silence or suppressed echo.
- Real hardware canaries log the active `AudioRoutePolicy` before making a
  pass/fail claim.

Details:

- [phase-6-realtime-echo-canary.md](phase-6-realtime-echo-canary.md)

## Out Of Scope

- Reopening the browser WebRTC/WebView audio bridge plan.
- Always-on WebRTC APM for every route.
- Making mic gating the default. It remains an opt-in fallback because it
  removes barge-in.
- Replacing M12/M13 provider architecture docs. They remain useful foundations.

## Archived Source Material

The old plans are kept for history after this M43 overview:

- [M24 - Anti-Echo](../archive/m24-anti-echo/overview.md)
- [M24 - Echo Diagnosis](../archive/m24-anti-echo/diagnosis.md)
- [M34 - Audio Quality](../archive/m34-audio-quality/overview.md)
- [M34 Phase 4 Implementation Report](../archive/m34-audio-quality/phase-4-platform-coverage/IMPLEMENTATION_REPORT.md)
