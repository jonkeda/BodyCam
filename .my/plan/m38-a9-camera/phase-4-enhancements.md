# Phase 4 - Enhancements

**Status:** Optional

## Goal

Extend the A9 integration beyond the initial camera-source workflow after the
core provider, tests, and settings UI are stable.

## Candidate Enhancements

### Audio Streaming

- Decode the camera audio stream if the device emits 8 KHz A-law PCM.
- Decide whether A9 audio should become an `IAudioInputProvider`.
- Keep A9 audio optional so the camera-only source remains reliable.

### Camera Discovery

- Broadcast `LanSearch` on the local network.
- Show discovered A9/X5 cameras in the A9 setup page.
- Let users choose a discovered camera instead of typing an IP address.
- Store discovered UID/device ID when available.

### Multiple A9 Cameras

- Replace single A9 settings with a list of known A9 devices.
- Allow multiple `a9-camera:{id}` providers or a device-selector layer.
- Show each configured camera as its own Connected Devices card.

### Packet-Loss Recovery

- Explore JPEG reset marker splicing or partial-frame recovery.
- Keep the first version conservative: never surface corrupt frames.
- Add metrics for dropped frames and reconnect count.

### Runtime Resolution Switching

- Expose 320x240 and 640x480 options if supported by the device variant.
- Add the selected resolution to A9 settings.
- Apply resolution changes by restarting the video stream cleanly.

## Acceptance Criteria

- Enhancements remain optional and do not destabilize the camera-only provider.
- Any new audio/camera/device capabilities use existing abstractions first.
- Settings additions fit the AddDevices/A9 setup flow, not `DeviceSettingsPage`
  inline sections.
- Every enhancement includes focused unit or integration tests.
