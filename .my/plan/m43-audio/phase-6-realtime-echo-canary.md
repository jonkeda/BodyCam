# M43 Phase 6 - Realtime Echo Canary

**Status:** Started
**Depends on:** M43 Phase 1 provider policy, M43 Phase 4 diagnostics, M43 Phase 5 Brinell automation

This phase adds an automatic real-world echo regression test: the assistant
speaks a known canary phrase while the room stays silent. The test fails if the
app sends that phrase back to Realtime as user speech, or if the assistant starts
another response without a real user turn.

## Goal

Prove that direct speaker routes do not create an "assistant hears itself" loop.

The canary test is intentionally simple:

1. Start the app in Speak mode.
2. Start a Realtime session.
3. Send text to the session:

   ```text
   Say exactly: "echo canary violet". Then stay silent.
   ```

4. Let the assistant audio play through the active output route.
5. Keep the room silent.
6. Wait through the silent window.
7. Assert that no user transcript contains the canary phrase and no extra
   assistant response starts.

This complements synthetic policy tests. It does not replace them, because the
real test needs a physical route, speaker volume, microphone position, and a
quiet room/device.

## Signals

The test records three independent signals:

- **Transcript echo:** Realtime emits an input/user transcript containing the
  canary phrase after the assistant spoke it.
- **Loop echo:** Realtime starts another assistant response after the canary
  response completed while the room is silent.
- **Audio echo score:** A local correlation check finds the assistant playback
  signal inside microphone capture above the configured threshold.

Any one of these can fail the canary. The transcript and loop signals are the
most user-visible. The audio score is useful when Realtime VAD does not
transcribe the echo but the mic still captured it.

## Required Diagnostics

Every canary run must log the active route policy near the start and at failure:

```text
Audio: DirectDeviceSpeaker | AEC WebRtcApm | cleanup NoiseSuppressionAndAgc | 80ms
```

The test should capture:

- active input provider display name and provider ID as labels
- active output provider display name and provider ID as labels
- input and output capability snapshots
- selected `AecMode`
- selected `VoiceCleanupMode`
- output mode, Speak or Silent
- route monitor state: headphones, Bluetooth
- AEC fallback state when the native processor is unavailable
- best audio echo score and delay, if local capture is enabled

Provider IDs are labels only. The pass/fail decision must be based on
capabilities, policy, transcripts, and audio correlation.

## Brinell Windows Flow

### ECHO-CANARY-WIN-1 - Direct Speaker Does Not Self-Trigger

Setup:

- Windows laptop route with direct speakers selected.
- App output mode: Speak.
- AEC enabled.
- Debug overlay/test accessor enabled.
- User remains silent.

Actions:

- Brinell launches BodyCam.
- Brinell clicks Speak.
- Brinell starts the active listening session.
- Brinell enters the canary prompt in the message box and sends it.
- Brinell waits for the assistant transcript completion.
- Brinell waits 10-15 seconds with no user speech.

Expected:

- `AudioPolicyDebugLabel` reports a direct speaker route.
- `AecMode` is `WebRtcApm` or a supported Windows native fallback.
- No user transcript contains the canary phrase.
- No second assistant response starts during the silent window.

### ECHO-CANARY-WIN-2 - Positive Control With AEC Forced Off

Setup:

- Same direct speaker route.
- AEC forced off using a test-only setting or launch argument.

Expected:

- The test should either fail by detecting transcript/loop echo or report a
  higher audio echo score than the AEC-enabled run.
- This positive control proves the canary is capable of catching the failure
  mode.

## Android Flow

### ECHO-CANARY-ANDROID-1 - Phone Speaker Uses Platform AEC

Setup:

- Physical Android device connected through adb/Appium.
- Phone speaker route selected.
- Output mode: Speak.
- User remains silent.

Expected:

- Policy reports `AecMode.PlatformNative` when Android
  `AcousticEchoCanceler` is active.
- If platform AEC is not active and WebRTC APM is unavailable, the test reports
  `fallback/no AEC` and does not claim echo is solved.
- No input transcript contains the canary phrase during the silent window.

## Deterministic Test Layer

The first implementation slice is deterministic and runs in unit tests:

- `EchoCanaryTranscriptMonitor` tracks assistant canary completion, user
  transcript echo, and silent-window loop events.
- `EchoCanaryAudioAnalyzer` computes normalized correlation between known
  assistant playback PCM and captured microphone PCM.
- Unit tests include both pass cases and positive-control failure cases.

This gives Brinell and RealTests a shared verdict engine before we wire the full
UI/hardware path.

## Acceptance

- [x] A new phase doc defines transcript, loop, and audio-score echo canaries.
- [x] Canary transcript detection has unit tests for pass and failure cases.
- [x] Audio correlation has unit tests for delayed echo and silence.
- [x] A positive-control synthetic echo test fails when AEC is off and passes
      when echo is removed.
- [ ] Brinell Windows direct-speaker run sends the canary prompt through the app.
- [ ] Brinell Windows positive control can force AEC off.
- [ ] Android/Appium run logs the active route policy and checks for self-echo.
- [ ] CI/nightly can opt into real hardware canaries without blocking normal PRs.
