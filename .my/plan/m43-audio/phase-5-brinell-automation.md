# M43 Phase 5 - Brinell Audio Automation

**Status:** Proposed
**Depends on:** M43 Phase 1 provider policy, M15 Brinell test providers

This phase defines how to test M43 automatically through Brinell without real
speakers, microphones, headsets, or glasses.

## What Can Be Automated

Brinell can reliably automate:

- Which AEC mode is selected for each route.
- Whether AEC is bypassed for headset-like providers.
- Whether direct speaker routes feed render reference audio to AEC.
- Whether Silent mode suppresses local playback and therefore bypasses AEC.
- Whether route changes update AEC state and stream-delay estimates.
- Whether the app avoids "assistant hears itself" loops in a simulated echo
  environment.
- Whether UI state exposes enough diagnostic information for blind debugging.

Brinell cannot fully prove real acoustic cancellation quality. That still needs
real-device validation because room acoustics, speaker DSP, mic placement, and
OS-level audio processing matter. The automatic tests should catch policy and
pipeline regressions before manual testing.

## Test Infrastructure

### 1. Test Audio Route Providers

Add test-only audio providers in the BodyCam test infrastructure, wrapping
Brinell generic audio mocks where possible.

```text
TestMicProvider
  provider id: test-mic, used only for labels and diagnostics
  configurable InputCapabilities
  emits deterministic 48 kHz PCM chunks
  can inject "user speech", silence, or delayed speaker echo

TestOutputProvider
  provider id: stable test label only
  configurable OutputCapabilities:
    EchoPathKind
    NeedsEchoCancellation
    IsAcousticallyIsolated
    SupportsRenderReference
    EstimatedOutputLatencyMs
  captures played chunks
  can raise OutputRouteChanged
```

Provider capabilities should identify whether the output is:

- direct speaker
- isolated headset
- Bluetooth headset
- glasses/headset-like route
- no local playback

These capabilities feed the M43 `AudioRoutePolicy`. Test names and provider IDs
may describe the scenario for readability, but policy assertions must mutate
capability properties rather than relying on provider ID matching.

### 2. Fake Route Monitor

Create a test implementation of `IRouteMonitor`:

```csharp
public sealed class TestRouteMonitor : IRouteMonitor
{
    public bool IsHeadphonesConnected { get; private set; }
    public bool IsBluetoothAudioConnected { get; private set; }
    public event EventHandler? RouteChanged;

    public void SetRoute(bool headphones, bool bluetooth)
    {
        IsHeadphonesConnected = headphones;
        IsBluetoothAudioConnected = bluetooth;
        RouteChanged?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync() => default;
}
```

Brinell tests can drive route changes through a test service accessor or a
test-only UI/debug command.

### 3. Spy AEC Processor

Use a spy/fake `IAecProcessor` for most Brinell tests:

```text
SpyAecProcessor
  records IsEnabled transitions
  records ProcessCapture calls
  records FeedRenderReference calls
  records UpdateStreamDelay values
  records ResetRenderReference calls
  optionally suppresses known echo bytes
```

This avoids depending on native WebRTC binaries in UI tests and makes the
assertions deterministic.

Use the real `AecProcessor` only in lower-level unit/integration tests where
native library loading is part of the scenario.

### 4. Test Realtime Client

To test self-echo loops, use a fake realtime client:

```text
FakeRealtimeClient
  captures audio chunks sent by VoiceInputAgent
  emits deterministic assistant audio chunks
  counts user-turn starts
  can flag "assistant echo received as user audio"
```

The fake client should make it possible to assert:

- direct speaker route with AEC on does not feed assistant echo back as user
  speech after the spy AEC suppresses it
- headset route with AEC off still avoids echo because no echo is injected
- direct speaker route with AEC forced off demonstrates the failure mode in a
  controlled negative test

### 5. Brinell Test Backchannel

Brinell needs a way to control test-only services from the running app.

Preferred approach:

- Add a test-only `TestServiceAccessor` available when `BODYCAM_TEST_MODE=1`.
- Expose safe commands:
  - set output capability preset: direct speaker / headset / Bluetooth headset
    / glasses / room speaker / no local playback
  - set output mode: Speak / Silent
  - inject mic PCM
  - emit assistant PCM
  - read current `AudioRoutePolicy`
  - read `SpyAecProcessor` counters

Fallback approach:

- Show an audio diagnostics debug label with `AutomationId="AudioPolicyDebugLabel"`.
- Brinell asserts the visible policy text.
- Test route changes are driven by test-only buttons in a hidden/debug panel.

The debug label should be screen-reader safe: hidden unless debug/test mode is
enabled and not announced during normal use.

## Automatic Test Cases

### B-AUD-1 - Direct Speaker Enables AEC

Setup:

- Input provider: `TestMicProvider` with no platform AEC.
- Output provider: `TestOutputProvider` with
  `EchoPathKind.DirectDeviceSpeaker`, `NeedsEchoCancellation = true`,
  `IsAcousticallyIsolated = false`, and render-reference support.
- Route monitor: no headphones, no Bluetooth
- Output mode: Speak

Actions:

- Start the app in Brinell test mode.
- Navigate to main page.
- Activate Speak.
- Start audio session.

Expected:

- `AudioRoutePolicy.AecMode == WebRtcApm`
- `SpyAecProcessor.IsEnabled == true`
- `FeedRenderReference` is called for assistant playback.
- `UpdateStreamDelay` receives the direct-speaker latency.
- Debug overlay says direct speaker AEC is active.

### B-AUD-2 - Headset Bypasses AEC

Setup:

- Output provider: `TestOutputProvider` with `EchoPathKind.IsolatedHeadset`,
  `NeedsEchoCancellation = false`, and `IsAcousticallyIsolated = true`.
- Route monitor: headphones connected
- Output mode: Speak

Expected:

- `AudioRoutePolicy.AecMode == Off`
- `SpyAecProcessor.IsEnabled == false`
- Assistant audio still reaches `TestOutputProvider`.
- `FeedRenderReference` is not required for policy correctness.
- No mic gating is applied unless `PauseMicWhilePlaying` is enabled.

### B-AUD-3 - Bluetooth Headset Bypasses AEC

Setup:

- Output provider: `TestOutputProvider` with `EchoPathKind.IsolatedHeadset`,
  `NeedsEchoCancellation = false`, and `IsAcousticallyIsolated = true`.
- Route monitor: headphones connected, Bluetooth connected

Expected:

- `AecMode == Off`
- Bluetooth route is treated as isolated when the provider is headset-like.
- Debug policy distinguishes "Bluetooth headset" from "room Bluetooth speaker"
  through `OutputCapabilities`, not provider ID strings.

### B-AUD-4 - Glasses Audio Bypasses AEC

Setup:

- Output provider: `TestOutputProvider` with `EchoPathKind.GlassesOrWearable`,
  `NeedsEchoCancellation = false`, and `IsAcousticallyIsolated = true`.
- Input provider: `TestMicProvider` with glasses-like input capabilities.

Expected:

- `AecMode == Off` by default because glasses are headset-like and close-field.
- Active providers persist correctly when glasses connect/disconnect.
- If glasses output is later proven to leak into a mic path, this test can be
  changed to expect a dedicated glasses policy instead of direct-speaker AEC.

### B-AUD-5 - Silent Mode Bypasses AEC And Playback

Setup:

- Output provider: `TestOutputProvider` with
  `EchoPathKind.DirectDeviceSpeaker` and `NeedsEchoCancellation = true`.
- Route monitor: no headphones
- Output mode: Silent

Actions:

- Trigger an assistant response.

Expected:

- Transcript updates.
- `TestOutputProvider` receives no audio chunks.
- `FeedRenderReference` is not called.
- `AecMode == Off` or `HasLocalPlayback == false`.
- Switching back to Speak restores direct-speaker AEC policy.

### B-AUD-6 - Route Change Updates Policy

Setup:

- Start with a direct-speaker output capability preset, no headphones.
- Then change to headphones connected with isolated-headset output
  capabilities.
- Then return to direct-speaker output capabilities.

Expected:

- AEC state transitions: `true -> false -> true`.
- `RouteChanged` is observed within the test timeout.
- `AudioPolicyDebugLabel` updates each time.
- No app restart is required.

### B-AUD-7 - Output Provider Switch Updates Delay

Setup:

- Start with direct-speaker output capabilities and latency 80 ms.
- Switch to room-speaker output capabilities and latency 220 ms.
- Switch back.

Expected:

- `SpyAecProcessor.UpdateStreamDelay` records `80, 220, 80`.
- The current policy uses the active output provider latency.
- Direct Bluetooth room speaker can still select AEC because its provider
  declares `NeedsEchoCancellation = true` and is not acoustically isolated.

### B-AUD-8 - Simulated Echo Loop Is Suppressed

Setup:

- `TestOutputProvider` captures assistant audio.
- `EchoInjector` mirrors assistant audio into `TestMicProvider` after 80 ms
  at -12 dB, simulating speaker bleed.
- `SpyAecProcessor` removes the known mirrored signal when AEC is enabled.

Actions:

- Fake realtime client emits assistant speech.
- Echo injector feeds mirrored audio into mic.

Expected:

- With direct speaker/AEC on: fake realtime client does not receive the
  assistant echo as a user turn.
- With headset/AEC off and no echo injection: no user turn.
- Negative control with direct speaker/AEC forced off: fake realtime client
  does receive the echo. Keep this negative test isolated and named clearly.

### B-AUD-9 - Interruption Resets Render Reference

Setup:

- Direct-speaker output capabilities.
- Assistant playback is active.

Actions:

- Trigger barge-in/interruption through the fake realtime client.

Expected:

- `FadeOutAndClearAsync` is called.
- `SpyAecProcessor.ResetRenderReference` is called exactly once.
- No stale render reference is used after interruption.

### B-AUD-10 - Debug Output Is Accessible

Setup:

- Debug/test mode on.

Expected:

- `AudioPolicyDebugLabel` exists only in debug/test mode.
- It contains concise state like:

```text
Audio: direct speaker | AEC WebRtcApm | cleanup NS+AGC | 80ms
```

- It is not shown or announced during normal production mode.

### B-AUD-11 - Provider ID Does Not Drive Policy

Setup:

- Output provider ID remains `test-output`.
- First capability set: `EchoPathKind.DirectDeviceSpeaker`,
  `NeedsEchoCancellation = true`, `IsAcousticallyIsolated = false`.
- Second capability set: `EchoPathKind.IsolatedHeadset`,
  `NeedsEchoCancellation = false`, `IsAcousticallyIsolated = true`.
- Route monitor changes are applied to match each capability set.

Expected:

- With the same provider ID, AEC changes when capabilities change.
- With different provider IDs but identical capabilities, AEC policy stays the
  same.
- No policy test depends on parsing provider ID strings.

## Synthetic Audio Fixtures

Recommended generated fixtures:

- `silence-48k-1s.pcm`
- `assistant-tone-48k-1s.pcm`
- `assistant-speech-like-48k-2s.pcm`
- `user-speech-like-48k-2s.pcm`
- `echo-assistant-80ms-minus12db-48k.pcm`
- `echo-assistant-220ms-minus18db-48k.pcm`

These do not need real speech. Deterministic tone bursts and speech-shaped
noise are easier to assert in CI.

## Suggested Test Layers

### Unit Tests

- `AudioRoutePolicyTests`
- `AecBypassManagerTests`
- `SpyAecProcessorTests`
- `EchoInjectorTests`

These are fastest and should cover the full route matrix.

### Integration Tests

- Use real `AudioInputManager` and `AudioOutputManager`.
- Use test providers and spy AEC.
- Verify provider switches, route changes, stream delay, jitter buffer, and
  interruption behavior.

### Brinell UI Tests

- Launch full app in `BODYCAM_TEST_MODE=1`.
- Drive main page controls: Speak, Silent, Actions, Look/Read/Scan.
- Read `AudioPolicyDebugLabel` or `TestServiceAccessor` counters.
- Validate user-facing route behavior without touching real devices.

## CI Matrix

Minimum CI:

| Target | Tests |
| --- | --- |
| Windows unit | route policy, echo injector, fake AEC |
| Windows integration | managers + test providers + fake route monitor |
| Windows Brinell UI | direct speaker, headset, Silent mode, route change |

Later CI:

| Target | Tests |
| --- | --- |
| Android emulator/device | policy with fake route monitor, not real acoustic AEC |
| iOS simulator | policy with fake route monitor, VoiceProcessingIO compile path only |
| Physical devices | manual or nightly real acoustic validation |

## Acceptance

- [ ] M43 route policy can be tested without hardware.
- [ ] Brinell can start the app in test audio mode.
- [ ] Direct speaker, headset, Bluetooth headset, glasses, and Silent mode have
      automatic assertions.
- [ ] Capability-driven policy is covered by tests that prove provider IDs do
      not control AEC behavior.
- [ ] Simulated echo loop has a deterministic positive and negative test.
- [ ] Debug/test policy output is accessible to Brinell.
- [ ] CI can run the Windows Brinell audio route tests headlessly.
