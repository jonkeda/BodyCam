# Phase 8 - A9 Audio Input

**Status:** Optional

## Goal

Evaluate and optionally expose A9 camera audio as a microphone input source.

A9 video must remain useful without audio; this phase should not destabilize the
camera-provider path.

## Scope

- Decode A9 audio packets if the camera emits 8 KHz A-law PCM.
- Decide whether decoded audio quality is good enough for BodyCam use.
- If viable, expose A9 audio through `IAudioInputProvider`.
- Keep A9 audio disabled unless the camera is actively configured and selected.

## Implementation

1. Extend `A9Session.HandleDrwData` to detect `StreamTypeAudio`.
2. Parse audio payload framing separately from JPEG frame reassembly.
3. Add an A-law decoder if needed.
4. Convert decoded PCM to the format expected by `IAudioInputProvider`.
5. Add `A9AudioInputProvider`.
6. Register it without creating a circular dependency with `AudioInputManager`.
7. Add UI status in A9 setup showing whether audio packets are present.
8. Decide whether A9 camera and A9 mic should auto-pair in Source profiles or stay
   independently selectable in Custom mode.

## Files

- `src/BodyCam/Services/Camera/A9/A9Session.cs`
- `src/BodyCam/Services/Audio/A9/A9AudioInputProvider.cs`
- `src/BodyCam/Services/Audio/A9/ALawDecoder.cs`
- `src/BodyCam/ServiceExtensions.cs`
- `src/BodyCam/ViewModels/Settings/A9CameraSettingsViewModel.cs`
- `src/BodyCam.Tests/Services/Audio/A9/ALawDecoderTests.cs`
- `src/BodyCam.Tests/Services/Audio/A9/A9AudioInputProviderTests.cs`

## Acceptance Criteria

- Audio packets are identified without interfering with JPEG frames.
- Decoder tests validate known A-law sample conversions.
- `A9AudioInputProvider` can start/stop cleanly.
- A9 audio appears as a microphone option only when viable.
- Camera-only A9 workflows continue to work when audio is unavailable.
- No source-profile auto-pairing is added unless explicitly decided in this phase.
