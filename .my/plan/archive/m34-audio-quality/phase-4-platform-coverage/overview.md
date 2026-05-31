# Phase 4 — Platform coverage

**Status:** Proposed
**Depends on:** Phase 1, Phase 3
**Sibling phases:** [Phase 1](../phase-1-correctness/overview.md), [Phase 2](../phase-2-resampling/overview.md), [Phase 3](../phase-3-threading/overview.md), [Phase 5](../phase-5-polish/overview.md), [Phase 6](../phase-6-observability/overview.md)

---

## Summary

Phase 4 closes the platform-coverage gap. iOS currently has no native
audio input provider — we add one using `AVAudioEngine` with the
`kAudioUnitSubType_VoiceProcessingIO` unit for hardware AEC. Bluetooth
paths suffer from inaccurate latency modeling (hardcoded ~150 ms in
[AecProcessor](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs));
we wire `EstimatedOutputLatencyMs` through `IAudioOutputProvider`,
push it into `AecProcessor.UpdateStreamDelay()` (Phase 1.3) on route
change, and test cross-route combos. Headphone detection lets us bypass
AEC when echo is impossible. Finally, Windows Voice Capture DMO
(`CWMAudioAEC`) is offered as an opt-in fallback for users where APM
underperforms.

---

## 4.1 — iOS native AEC via VoiceProcessingIO

### Scenario

There is no `src/BodyCam/Platforms/iOS/PlatformMicProvider.cs` yet —
iOS audio capture isn't implemented. When the iOS build runs, it has no
mic provider to register. Even if WebRTC APM ran, Apple's hardware AEC
(via `kAudioUnitSubType_VoiceProcessingIO`) is lower-latency, lower-power,
and tuned for AirPods + iPhone speakers — strictly better on Apple
hardware.

### Implementation

#### 4.1.1 — iOS mic provider

New `src/BodyCam/Platforms/iOS/PlatformMicProvider.cs` implementing
[IAudioInputProvider](../../../src/BodyCam/Services/Audio/IAudioInputProvider.cs):

- Configure `AVAudioSession.SharedInstance()`:
  - `Category = .playAndRecord`
  - `Mode = .voiceChat` (engages VoiceProcessingIO)
  - Options: `.duckOthers | .defaultToSpeaker`
  - `.SetActive(true)`
- Build the engine:
  ```csharp
  var engine = new AVAudioEngine();
  var inputNode = engine.InputNode;
  inputNode.SetVoiceProcessingEnabled(true, out _);
  var format = inputNode.GetBusOutputFormat(0);
  inputNode.InstallTapOnBus(0, frameLength, format, (buffer, when) => {
      // PCM16 mono → AudioChunkAvailable
  });
  engine.Prepare();
  engine.StartAndReturnError(out _);
  ```
- Convert AudioBufferList float samples → PCM16 → `AudioChunkAvailable` at the chunk cadence.
- Honour `AppSettings.SampleRate` (resample if hardware tap rate differs).

#### 4.1.2 — Bypass WebRTC APM when iOS native AEC is active

iOS's VoiceProcessingIO already does AEC, NS and AGC. Stacking WebRTC APM
on top double-processes and *reduces* quality.

- Add `bool IsPlatformAecActive { get; }` on
  [AudioInputManager](../../../src/BodyCam/Services/Audio/AudioInputManager.cs).
- Surface via `AudioInputManager.ProvidersChanged`.
- In [VoiceInputAgent.OnAudioChunk](../../../src/BodyCam/Agents/VoiceInputAgent.cs):

  ```csharp
  byte[] processed = (_aec is not null && !_audioInput.IsPlatformAecActive)
      ? _aec.ProcessCapture(chunk)
      : chunk;
  ```

- Add `AppSettings.IosUsePlatformAecOnly = true` (default) for users to
  opt back into APM stacking for testing.

#### 4.1.3 — Speaker provider

Use the same `AVAudioEngine` for output (an `AVAudioPlayerNode`) so the
session and the VoiceProcessingIO path see both directions consistently.
Engine lifecycle: created on first start, kept alive across mic/speaker
start/stop pairs (avoid tearing it down per call).

### Test plan

- Unit (`IosPlatformMicProviderTests`, simulator-only): mock `AVAudioSession` interactions; verify `Mode = .voiceChat`.
- Integration on real iPhone: 10 s recording with speaker playing reference tone; chunks monotonic; subjective echo gone.
- Compare ERLE on the same iPhone with native vs APM-stacked: native should be ≥ APM and lower CPU.

### Acceptance

- [ ] `Platforms/iOS/PlatformMicProvider.cs` exists, registered in DI.
- [ ] `Platforms/iOS/PhoneSpeakerProvider.cs` (or matching) shares the engine.
- [ ] `AVAudioSession` set to `.playAndRecord` + `.voiceChat`.
- [ ] `VoiceProcessingIO` enabled (`SetVoiceProcessingEnabled(true)` returns no error).
- [ ] `AudioInputManager.IsPlatformAecActive` reports correctly; `VoiceInputAgent` skips APM when true.
- [ ] `AppSettings.IosUsePlatformAecOnly` toggle works.
- [ ] iOS build runs without manual setup beyond mic permission.

---

## 4.2 — Bluetooth path AEC: adaptive latency

### Scenario

Switching to a Bluetooth speaker / headset jumps output latency from
~50 ms (built-in) to 150–400 ms depending on codec (SBC, AAC, aptX, LDAC).
APM's adaptive filter only converges within ±20 ms of the true
speaker→mic delay; if delay is mis-set by 200 ms, AEC silently fails for
the entire call. Today `SetStreamDelayMs` is only called once at
`Initialize`.

### Implementation

#### 4.2.1 — Latency property + route event on `IAudioOutputProvider`

(Already specified in Phase 1.3; this section makes sure all the
Bluetooth-specific providers actually populate it.)

- [WindowsBluetoothAudioOutputProvider](../../../src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothAudioOutputProvider.cs):
  return `180` ms default; if codec query is available later, refine.
- [AndroidBluetoothAudioOutputProvider](../../../src/BodyCam/Platforms/Android/Audio/) (file
  may need creating if it doesn't exist yet — confirm during impl):
  `audioTrack.BufferSizeInFrames * 1000 / sampleRate + 150`.
- Fire `OutputRouteChanged` when:
  - BT device connect/disconnect.
  - Speaker → headphones jack changes (Android `AudioDeviceCallback`).
  - Windows default-device change (WASAPI `IMMNotificationClient.OnDefaultDeviceChanged`).

#### 4.2.2 — Wire updates to AEC

[AudioOutputManager.SetActiveAsync](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs):

```csharp
private void OnRouteChanged(object? sender, EventArgs e)
{
    if (_active is null || _aec is null) return;
    _aec.UpdateStreamDelay(_active.EstimatedOutputLatencyMs);
}
```

Subscribe on `SetActiveAsync` (and unsubscribe from the previous active
provider). Also call once immediately on switch.

#### 4.2.3 — Cross-route test combos

Four combinations matter:

1. Built-in mic + built-in speaker (delay ~ 80 ms).
2. Built-in mic + BT speaker (~ 200 ms).
3. BT mic + built-in speaker (~ 200 ms — BT mic adds capture latency).
4. BT mic + BT speaker (~ 350 ms).

For (3) and (4), `EstimatedOutputLatencyMs` alone isn't enough — the
*input* side also adds latency. Phase 4 ships with output-only modeling
(simpler, biggest impact); a future refinement (call it 4.2.b) can add
`EstimatedInputLatencyMs` on `IAudioInputProvider` and sum the two.

### Test plan

- Unit `AecProcessor_UpdateStreamDelay`: clamp to [10, 500].
- Integration: switch active provider speaker → BT → speaker; assert
  `WebRtcApmInterop.SetStreamDelayMs` invoked 3 times with distinct values
  (intercept via test wrapper or check logs).
- Manual: connect BT speaker mid-call; echo should clear within ~5 s.

### Acceptance

- [ ] All BT-output providers report a non-default `EstimatedOutputLatencyMs`.
- [ ] `OutputRouteChanged` fires reliably on connect/disconnect.
- [ ] `AudioOutputManager` wires the event to `AecProcessor.UpdateStreamDelay`.
- [ ] Manual cross-route test: AEC reconverges < 5 s on each combo.

---

## 4.3 — Headphone detection → AEC bypass

### Scenario

When headphones (wired or BT) are connected, the speaker→mic acoustic
path is broken — there is *no* echo to cancel. Running APM in this
configuration adds CPU cost, slight quality loss (NS still removes some
voice harmonics), and battery drain for zero benefit.

### Implementation

#### 4.3.1 — `IRouteMonitor`

New `src/BodyCam/Services/Audio/IRouteMonitor.cs`:

```csharp
public interface IRouteMonitor : IAsyncDisposable
{
    bool IsHeadphonesConnected { get; }
    bool IsBluetoothAudioConnected { get; }
    event EventHandler? RouteChanged;
}
```

Per-platform implementations:

- **Android** (`Platforms/Android/AndroidRouteMonitor.cs`):
  - API 23+: `AudioManager.GetDevices(GetDevicesTargets.Outputs)`, look for
    `AudioDeviceType.WiredHeadphones`, `AudioDeviceType.WiredHeadset`,
    `AudioDeviceType.UsbHeadset`, `AudioDeviceType.BluetoothA2dp`,
    `AudioDeviceType.BluetoothSco`.
  - Subscribe via `AudioManager.RegisterAudioDeviceCallback`.

- **Windows** (`Platforms/Windows/WindowsRouteMonitor.cs`):
  - Use NAudio `MMDeviceEnumerator` + `IMMNotificationClient`.
  - Form factor lookup: `MMDevice.Properties[PKEY_AudioEndpoint_FormFactor]`
    matching `EndpointFormFactor.Headphones` / `Headset` / `LineLevel`.
  - Bluetooth: check `KSNODETYPE_*` GUIDs on the endpoint.

- **iOS** (`Platforms/iOS/IosRouteMonitor.cs`):
  - `AVAudioSession.SharedInstance().CurrentRoute.Outputs` — `PortType` of
    `Headphones`, `BluetoothA2DP`, `BluetoothHFP`, `BluetoothLE`.
  - `AVAudioSession.Notifications.ObserveRouteChange`.

#### 4.3.2 — `AecBypassManager`

New `src/BodyCam/Services/Audio/AecBypassManager.cs`:

```csharp
public sealed class AecBypassManager : IAsyncDisposable
{
    public AecBypassManager(IRouteMonitor monitor, AecProcessor aec, ILogger<AecBypassManager> log) { ... }

    private void OnRouteChanged(object? s, EventArgs e)
    {
        bool isolated = _monitor.IsHeadphonesConnected;
        _aec.IsEnabled = !isolated;
        _log.LogInformation("Route changed; AEC IsEnabled={Enabled}", !isolated);
    }
}
```

Register as singleton; start at app startup.

### Test plan

- Unit (`AecBypassManagerTests`): stub `IRouteMonitor`; toggle headphones; assert `AecProcessor.IsEnabled` flips.
- Integration on Android device with 3.5 mm jack + USB-C adapter.
- Manual on iPhone: connect AirPods → AEC disabled (verify via Phase 6 metrics overlay); disconnect → re-enabled.

### Acceptance

- [ ] `IRouteMonitor` defined; impls for Android, Windows, iOS.
- [ ] `RouteChanged` fires within 500 ms of plug/unplug.
- [ ] `AecBypassManager` toggles `AecProcessor.IsEnabled` accordingly.
- [ ] No audible artifact on transition (Phase 5.4 handles fades).

---

## 4.4 — Windows Voice Capture DMO (opt-in fallback)

### Scenario

Some Windows machines (older Intel HD Audio, certain laptop OEM stacks)
get poor results from WebRTC APM but excellent results from the Windows
Voice Capture DMO (`CWMAudioAEC`, CLSID
`{745057C7-F353-4F2D-A7EE-58434477730E}`). The DMO is officially
deprecated by Microsoft but remains shipped on Windows 10 and 11.

This is an **opt-in escape hatch**, not a primary path. Low priority.

### Implementation

- New `src/BodyCam/Platforms/Windows/Audio/VoiceCaptureDmoAecProcessor.cs`
  implementing the same minimal contract as
  [AecProcessor](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs)
  (`ProcessCapture`, `FeedRenderReference`, `UpdateStreamDelay`,
  `IsEnabled`) — extract a small `IAecProcessor` interface to make the
  swap clean.
- Use `IMFTransform` via .NET via `MediaFoundation.NetCore` package, OR
  use NAudio's WASAPI in **echo cancellation mode** if the WaveIn endpoint
  exposes it.
- `AppSettings.WindowsUseVoiceCaptureDmo = false` (default).
- DI: when true and platform is Windows, register
  `VoiceCaptureDmoAecProcessor` as `IAecProcessor` instead of WebRTC's.
- Log a clear deprecation warning at startup when enabled.

### Test plan

- Manual A/B on a target machine where APM is known to underperform.
- Verify deprecation warning is logged.
- No automated tests beyond a smoke test (DMO availability).

### Acceptance

- [ ] `IAecProcessor` interface extracted; `AecProcessor` implements it.
- [ ] `VoiceCaptureDmoAecProcessor` ships behind `WindowsUseVoiceCaptureDmo`.
- [ ] Deprecation warning logged when enabled.
- [ ] Marked as low priority in roadmap.

---

## Files touched

### New
- `src/BodyCam/Platforms/iOS/PlatformMicProvider.cs`
- `src/BodyCam/Platforms/iOS/PhoneSpeakerProvider.cs` (or rename existing if it exists)
- `src/BodyCam/Platforms/iOS/IosRouteMonitor.cs`
- `src/BodyCam/Platforms/Android/AndroidRouteMonitor.cs`
- `src/BodyCam/Platforms/Windows/WindowsRouteMonitor.cs`
- `src/BodyCam/Services/Audio/IRouteMonitor.cs`
- `src/BodyCam/Services/Audio/AecBypassManager.cs`
- `src/BodyCam/Services/Audio/IAecProcessor.cs` (extracted interface)
- `src/BodyCam/Platforms/Windows/Audio/VoiceCaptureDmoAecProcessor.cs`

### Modified
- [IAudioOutputProvider.cs](../../../src/BodyCam/Services/Audio/IAudioOutputProvider.cs) — already touched in 1.3.
- [WindowsBluetoothAudioOutputProvider.cs](../../../src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothAudioOutputProvider.cs) — implement latency + route event.
- Android BT output provider — same.
- [AudioOutputManager.cs](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs) — subscribe to route events.
- [AudioInputManager.cs](../../../src/BodyCam/Services/Audio/AudioInputManager.cs) — `IsPlatformAecActive` flag.
- [VoiceInputAgent.cs](../../../src/BodyCam/Agents/VoiceInputAgent.cs) — bypass APM when iOS native is active.
- [AppSettings.cs](../../../src/BodyCam/AppSettings.cs) — `IosUsePlatformAecOnly`, `WindowsUseVoiceCaptureDmo`.
- [AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs) — implement `IAecProcessor`.

---

## Execution order within Phase 4

1. **4.2** first — Bluetooth latency wiring is a small, high-impact follow-on to Phase 1.3 and unblocks any user with BT headphones.
2. **4.3** — headphone detection gets free wins (CPU/battery) once the route monitor exists.
3. **4.1** — iOS provider; biggest chunk of work; requires iOS build pipeline and a device.
4. **4.4** — Windows DMO; lowest priority; can be deferred indefinitely.
