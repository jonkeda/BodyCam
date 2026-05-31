# M43 Phase 2 - Split Echo Cancellation From Voice Cleanup

Goal: allow echo cancellation, noise suppression, gain control, and high-pass
filtering to be controlled separately.

## Why

`IAecProcessor.IsEnabled = false` currently bypasses the whole WebRTC APM path.
That is too broad. A headset route should not need acoustic echo cancellation,
but it may still benefit from noise suppression or gain control.

M43 should separate:

- acoustic echo cancellation: removes local speaker bleed
- noise suppression: reduces background noise
- gain control: normalizes microphone level
- high-pass filtering: removes low-frequency rumble

## Scope

- Add an audio processing policy model.
- Replace single `IsEnabled` semantics with explicit processing flags.
- Let `AudioRoutePolicy` choose `AecMode` and `VoiceCleanupMode`.
- Keep existing settings compatible where possible.
- Do not make mic gating the default.

## Proposed Models

Add:

```csharp
public sealed record AudioProcessingPolicy(
    AecMode AecMode,
    bool EchoCancellationEnabled,
    bool NoiseSuppressionEnabled,
    bool GainControlEnabled,
    bool HighPassFilterEnabled,
    int NoiseSuppressionLevel,
    int AgcTargetLevelDbfs,
    int AgcCompressionGainDb,
    int StreamDelayMs,
    string Explanation);
```

`AudioRoutePolicy` can either contain this directly or expose enough data to
derive it.

## Processor Interface

Update `IAecProcessor` from a single broad toggle to explicit policy
application.

Preferred shape:

```csharp
public interface IAudioProcessor : IDisposable
{
    AudioProcessingPolicy CurrentPolicy { get; }
    void Initialize(bool mobileMode = false);
    void ApplyPolicy(AudioProcessingPolicy policy);
    byte[] ProcessCapture(byte[] pcm16At48k);
    void FeedRenderReference(byte[] pcm16At48k);
    void UpdateStreamDelay(int totalDelayMs);
    void ResetRenderReference();
}
```

If renaming `IAecProcessor` is too much churn for one phase, keep the interface
name but add `ApplyPolicy`. Mark `IsEnabled` as legacy and remove it in a later
cleanup.

## Route Behavior

Expected behavior:

| Route | Echo cancellation | Cleanup |
| --- | --- | --- |
| Direct speaker | on | NS + AGC unless disabled |
| External room speaker | on | NS + AGC unless disabled |
| Headset/headphones | off | optional NS + AGC |
| Glasses/wearable | off by default | optional NS + AGC |
| Silent mode | off | optional input cleanup |
| Platform-native AEC active | WebRTC AEC off | avoid double-processing |

## Settings

Add or normalize settings names:

- `EchoCancellationMode`: `Auto`, `Off`, `ForceWebRtc`, `ForcePlatform`
- `NoiseSuppressionMode`: `Auto`, `Off`, `On`
- `NoiseSuppressionLevel`: 0-3
- `GainControlMode`: `Auto`, `Off`, `On`
- `AgcTargetLevelDbfs`
- `AgcCompressionGainDb`
- `PauseMicWhilePlaying`: stays diagnostic/fallback only

Settings should override policy only when explicitly set. Default should be
`Auto`.

## WebRTC APM Notes

`AecProcessor` currently configures AEC, NS, high-pass filter, and AGC during
initialization. This phase should make policy changes safe at runtime.

Options:

- Re-apply WebRTC APM config when `ApplyPolicy` changes flags.
- Keep one processor alive and bypass only the relevant processing stage if the
  native wrapper supports it.
- Recreate the APM instance on rare policy changes if dynamic reconfiguration
  is not reliable.

Policy changes happen on route/provider changes, not every audio frame, so a
small reconfiguration cost is acceptable.

## Tests

Add tests for:

- Headset route keeps cleanup on while AEC is off.
- Direct speaker route enables AEC and cleanup.
- Silent mode disables AEC.
- Platform-native AEC disables WebRTC AEC but can keep cleanup according to
  policy.
- Runtime route change applies a new processor policy.
- Settings override `Auto` decisions.
- `FeedRenderReference` is not required when AEC is off.

## Acceptance

- AEC can be off while NS/AGC remain on.
- Direct speakers still feed render reference audio.
- Headset routes avoid unnecessary AEC artifacts.
- Existing audio pipeline still emits 48 kHz internal PCM.
- Diagnostic logs show both AEC mode and cleanup mode.
