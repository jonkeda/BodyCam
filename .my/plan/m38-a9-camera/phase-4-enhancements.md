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
- Detailed phase: [Phase 8 - A9 Audio Input](./phase-8-audio-input.md)

### Camera Discovery

- Probe direct RTSP and HTTP MJPEG endpoints first.
- Probe V720/Naxclow AP mode on `192.168.169.1:6123` when the network matches
  that camera family.
- Fall back to `LanSearch` on the local network when direct stream probes fail.
- Also probe prompt-listed PPPP/iLnk discovery variants on UDP `32108` and
  `20190`.
- Show discovered A9/X5 cameras in the A9 setup page.
- Let users choose a discovered camera instead of typing an IP address.
- Store discovered UID/device ID when available.
- Detailed phase: [Phase 5 - A9 Discovery](./phase-5-discovery.md)

### Protocol Variant Validation

- Reconcile the implemented UDP/MJPEG A9 path with the saved `pmpt.md`
  requirement for TCP PPPP/iLnk and raw H.264.
- Include RTSP, HTTP MJPEG, and V720/Naxclow variants in the compatibility
  matrix.
- Capture fixtures before changing runtime behavior.
- Decide whether TCP/H.264 is a real A9 variant, a separate PPPP camera family,
  or an incorrect assumption.
- Detailed phase: [Phase 10 - Protocol Variant Spike](./phase-10-protocol-variant-spike.md)

### TCP H.264 Streaming

- Add an optional TCP session path only for variants confirmed by phase 10.
- Implement login, session-key negotiation, stream request, and keepalive.
- Read raw H.264 NAL units without assuming RTSP or ONVIF.
- Detailed phase: [Phase 11 - TCP PPPP/iLnk H.264 Session](./phase-11-tcp-h264-session.md)

### V720/Naxclow AP Mode

- Add the A9 V720/Naxclow variant from `intx82/a9-v720`.
- Detect the AP-mode camera at `192.168.169.1:6123`.
- Parse/build the custom Naxclow frame and receive JPEG live frames.
- Keep STA/fake-server mode as a later extension because it needs DNS, MQTT, and
  relay infrastructure.
- Detailed phase: [Phase 14 - V720/Naxclow A9 Variant](./phase-14-v720-naxclow-variant.md)

### H.264 Decoding

- Add a decoder abstraction for raw H.264 streams.
- Investigate FFmpeg.AutoGen first and LibVLCSharp as a fallback.
- Keep decoder dependencies optional so UDP/MJPEG remains lightweight.
- Detailed phase: [Phase 12 - H.264 Decoding](./phase-12-h264-decoding.md)

### Multiple A9 Cameras

- Replace single A9 settings with a list of known A9 devices.
- Allow multiple `a9-camera:{id}` providers or a device-selector layer.
- Show each configured camera as its own Connected Devices card.
- Detailed phase: [Phase 6 - Known A9 Devices](./phase-6-known-devices-multiple-cameras.md)

### Packet-Loss Recovery

- Explore JPEG reset marker splicing or partial-frame recovery.
- Keep the first version conservative: never surface corrupt frames.
- Add metrics for dropped frames and reconnect count.
- Detailed phase: [Phase 7 - Stream Controls & Diagnostics](./phase-7-stream-controls-diagnostics.md)

### Runtime Resolution Switching

- Expose 320x240 and 640x480 options if supported by the device variant.
- Add the selected resolution to A9 settings.
- Apply resolution changes by restarting the video stream cleanly.
- Detailed phase: [Phase 7 - Stream Controls & Diagnostics](./phase-7-stream-controls-diagnostics.md)

### Capture Preview

- Capture one still frame from the A9 setup page.
- Render the preview image and show byte count plus latency.
- Keep preview separate from the main Take Picture workflow.
- Detailed phase: [Phase 9 - A9 Capture Preview](./phase-9-capture-preview.md)

### Public A9 API

- Add the prompt-shaped API: `A9Camera.DiscoverAsync()` and
  `A9Camera.ConnectAsync(device)`.
- Normalize discovered devices across RTSP, HTTP MJPEG, V720/Naxclow,
  PPPP/MJPEG, and H.264 variants.
- Keep BodyCam itself integrated through `ICameraProvider`.
- Detailed phase: [Phase 13 - Public A9 Camera API](./phase-13-public-api.md)

## Acceptance Criteria

- Enhancements remain optional and do not destabilize the camera-only provider.
- Any new audio/camera/device capabilities use existing abstractions first.
- Settings additions fit the AddDevices/A9 setup flow, not `DeviceSettingsPage`
  inline sections.
- Every enhancement includes focused unit or integration tests.
- Prompt-driven TCP/H.264 work remains variant-gated until discovery/session
  captures prove which devices require it.
