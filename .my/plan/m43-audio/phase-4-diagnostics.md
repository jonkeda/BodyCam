# M43 Phase 4 - Audio Diagnostics

Goal: make audio route, AEC, cleanup, and latency decisions visible enough to
debug without guessing.

## Why

Echo bugs are hard to reproduce because they depend on hardware, route state,
room acoustics, and OS audio processing. The app should explain what it decided
and why.

Diagnostics must also work for blind and visually impaired testers. Important
state should be available through accessible labels, logs, and Brinell
backchannels, not only visual UI.

## Scope

- Add structured audio policy logs.
- Add a debug/test audio policy label.
- Add route transition history.
- Add optional 10 second WAV capture for regression comparison.
- Add an A/B capture flow for direct speaker versus headset routes.
- Keep diagnostics hidden or quiet in normal production use.

## Policy Log Fields

Log a structured event whenever policy changes:

- input provider ID as label
- input capabilities
- output provider ID as label
- output capabilities
- route monitor state
- output mode: Speak or Silent
- selected `AecMode`
- selected `VoiceCleanupMode`
- estimated round-trip latency
- policy explanation
- conflict flag when provider capabilities and route state disagree

Provider IDs are labels only. The log should make it clear that behavior came
from capabilities.

## Debug Label

Expose a concise label in debug/test mode, for example:

```text
Audio: direct speaker | AEC WebRtcApm | cleanup NS+AGC | 80ms
```

Requirements:

- `AutomationId="AudioPolicyDebugLabel"`
- visible only in debug/test mode
- not announced during normal production use
- screen-reader friendly when debug/test mode is enabled
- concise enough for Brinell assertions

## Route History

Keep an in-memory route history buffer, such as the latest 20 changes.

Each entry should contain:

- timestamp
- route monitor state
- input/output provider labels
- input/output capability snapshots
- selected policy
- explanation

This can be exposed through a debug view or test service accessor.

## WAV Capture

Add or reuse the debug capture recorder so testers can save a short regression
artifact.

Capture options:

- processed mic audio
- raw mic audio if available
- render reference audio
- selected route policy metadata sidecar

Default duration: 10 seconds.

The sidecar can be JSON so manual test results can be compared over time.

## A/B Capture Flow

Add a simple debug flow:

1. Capture direct-speaker sample.
2. Capture headset sample.
3. Store both with their policy metadata.
4. Mark whether assistant self-reply was observed.

This is not a production feature. It is for test builds and local regression
work.

## Brinell/Test Access

Expose diagnostics through one of:

- `TestServiceAccessor` in `BODYCAM_TEST_MODE=1`
- `AudioPolicyDebugLabel`
- structured app logs that Brinell can read

Preferred test fields:

- current `AudioRoutePolicy`
- latest `AudioProcessingPolicy`
- AEC enabled flag
- cleanup enabled flags
- render-reference feed count
- stream-delay update history
- route transition history

## Tests

Add tests for:

- policy changes emit structured logs.
- debug label text updates on route change.
- debug label is hidden outside debug/test mode.
- route history keeps latest entries.
- provider IDs appear only as labels in diagnostics.
- WAV capture creates audio file plus policy metadata sidecar.
- Brinell can read current policy without relying on visual-only state.

## Acceptance

- A tester can tell which route was active and why AEC was on/off.
- The current policy is accessible in debug/test mode.
- Route transitions are recorded with capability snapshots.
- 10 second WAV capture can be saved with policy metadata.
- Diagnostics do not add noise to normal production use.
