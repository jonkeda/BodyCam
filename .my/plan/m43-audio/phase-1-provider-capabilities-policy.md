# M43 Phase 1 - Provider Capabilities And Echo Policy

Goal: make echo-cancellation decisions from provider-declared capabilities and
route state, not from provider IDs or concrete provider types.

## Why

The app already has good provider boundaries:

- `IAudioInputProvider` for microphones and audio capture sources.
- `IAudioOutputProvider` for speakers, headsets, Bluetooth, and glasses audio.
- `AudioInputManager` and `AudioOutputManager` for active provider selection.

The missing piece is behavior metadata. Today, some decisions still depend on
IDs like `platform`, `windows-speaker`, or `phone-speaker`, and
`AecBypassManager` only looks at whether headphones are connected. M43 should
move that behavior knowledge into provider capability properties.

## Scope

- Add `AudioInputCapabilities` and `AudioOutputCapabilities`.
- Add capability properties to `IAudioInputProvider` and
  `IAudioOutputProvider`.
- Add an `AudioRoutePolicy` model and policy service.
- Replace route-only AEC bypass logic with policy-driven AEC selection.
- Keep provider IDs for persistence, UI labels, and diagnostics only.
- Remove switch/case or if/else behavior based on provider ID strings.

## Proposed Models

Add these under `src/BodyCam/Services/Audio/`.

```csharp
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

## Interface Changes

Update:

- `src/BodyCam/Services/Audio/IAudioInputProvider.cs`
- `src/BodyCam/Services/Audio/IAudioOutputProvider.cs`

Add:

```csharp
AudioInputCapabilities InputCapabilities { get; }
```

and:

```csharp
AudioOutputCapabilities OutputCapabilities { get; }
```

Keep existing `ProviderId` properties, but document that IDs must not control
AEC behavior.

## Provider Defaults

Initial capability mapping:

| Provider | Capabilities |
| --- | --- |
| Windows platform mic | no platform AEC, input latency estimate |
| Windows speaker | direct device speaker, needs AEC, supports render reference |
| Windows Bluetooth headset | isolated headset, no AEC needed |
| Windows Bluetooth room speaker | external room speaker, needs AEC |
| Android platform mic | platform AEC available/active when native AEC is configured |
| Android phone speaker | direct device speaker, needs AEC |
| Android Bluetooth SCO headset | isolated headset, no AEC needed |
| Android Bluetooth A2DP room speaker | external room speaker, needs AEC |
| iOS platform mic | platform AEC active when VoiceProcessingIO is active |
| iOS phone speaker | direct device speaker, needs AEC |
| iOS AirPods/headset | isolated headset, no AEC needed |
| HeyCyan glasses output | glasses or wearable, acoustically isolated by default |
| Silent mode | no local playback, no AEC needed |

If a platform cannot distinguish a Bluetooth headset from a room speaker, it
should report `EchoPathKind.Unknown`. The policy can then choose the safer
route and log the uncertainty.

## Policy Service

Add a service such as `IAudioRoutePolicyService`:

```csharp
public interface IAudioRoutePolicyService
{
    AudioRoutePolicy Current { get; }
    event EventHandler<AudioRoutePolicy>? PolicyChanged;
    AudioRoutePolicy Recompute();
}
```

Inputs:

- `AudioInputManager.Active?.InputCapabilities`
- `AudioOutputManager.Active?.OutputCapabilities`
- `IRouteMonitor`
- output mode: `Speak` or `Silent`
- settings overrides, if any

Policy rules:

- Silent mode or no local playback: `AecMode.Off`.
- Output declares `NeedsEchoCancellation = false` and
  `IsAcousticallyIsolated = true`: `AecMode.Off`.
- Input declares `PlatformEchoCancellationActive = true` and output needs AEC:
  `AecMode.PlatformNative`.
- Output needs AEC and input has no platform AEC:
  `AecMode.WebRtcApm`.
- Unknown output with local playback should be conservative and log why.

When provider capabilities and route state disagree, include the conflict in
`Explanation`.

## Replace Current Coupling

Update:

- `AudioInputManager.IsPlatformAecActive`
- `AudioOutputManager.SetActiveCoreAsync`
- `AecBypassManager`

The current `AudioInputManager.IsPlatformAecActive` should stop checking
concrete platform types or provider IDs. It should read
`Active.InputCapabilities.PlatformEchoCancellationActive`.

`AudioOutputManager` should update stream delay from
`Active.OutputCapabilities.EstimatedOutputLatencyMs`.

`AecBypassManager` should either become a thin policy subscriber or be replaced
by the new policy service.

## Tests

Add unit tests for:

- Direct speaker without platform AEC selects `WebRtcApm`.
- Direct phone speaker with platform AEC selects `PlatformNative`.
- Headset-like output selects `Off`.
- Silent mode selects `Off`.
- Same provider ID with different capabilities changes policy.
- Different provider IDs with same capabilities keep the same policy.
- Provider ID strings are not parsed for behavior.
- Route monitor changes recompute policy.
- Policy explanation includes capability and route state.

Update provider tests to assert expected default capabilities.

## Acceptance

- Every audio input and output provider exposes capabilities.
- AEC policy has no provider ID switch/case logic.
- Existing provider selection and saved settings still use provider IDs.
- Direct speakers enable AEC.
- Headsets, glasses, and Silent mode bypass AEC.
- Debug logs can explain the decision from capabilities and route state.
