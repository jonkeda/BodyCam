# M38 — A9 Camera: iLnkP2P/PPPP IP Camera Integration

**Status:** In Progress
**Goal:** Add A9/X5 IP cameras as a camera input source via the existing
`ICameraProvider` abstraction, using the reverse-engineered iLnkP2P/PPPP
protocol documented at https://github.com/DavidVentura/cam-reverse.

**Depends on:** M11 (Camera Abstraction)

---

## Why

The A9 is a sub-$5 IP camera that streams JPEG video over a custom UDP
protocol (branded iLnkP2P / PPPP). Adding it as a camera source gives
BodyCam a cheap, wireless, wearable camera option that doesn't require
a smartphone or smart glasses. The camera can operate in AP mode
(192.168.1.1) or join an existing LAN.

---

## Protocol Summary

Reference: https://github.com/DavidVentura/cam-reverse

The protocol runs over **UDP port 32108** and uses big-endian 2-byte
command IDs as the first two bytes of every packet.

### Packet Structure

```
┌──────────┬───────────┬──────────────────┐
│ Cmd (2B) │ Len (2B)  │ Payload (N bytes)│
└──────────┴───────────┴──────────────────┘
```

The `Drw` command (0xf1d0) uses an inner framing scheme:

- **Control packets** (stream byte = 0): carry sub-commands like
  ConnectUser, StartVideo, DevStatus. Payloads > 4 bytes are
  "encrypted" with a simple XOR-flip + rotate cipher (XqBytesEnc/Dec).
- **Data packets** (stream byte = 1): carry audio or video payloads.
  Video frames are JPEG, delivered as 1028-byte segments with sequence
  numbers.

### Session Handshake

```
App  ──► Cam:  LanSearch           (0xf130, 4 bytes)
Cam  ──► App:  PunchPkt            (0xf141, device serial)
App  ──► Cam:  P2pRdy              (0xf142, echo serial)
Cam  ──► App:  P2pRdy              (reply)
App  ──► Cam:  ConnectUser         (Drw control, username/password)
Cam  ──► App:  ConnectUserAck      (Drw control, 4-byte ticket)
App  ──► Cam:  VideoParamSet       (resolution: 1=320x240, 2=640x480)
App  ──► Cam:  StartVideo          (Drw control, with ticket)
Cam  ──► App:  [continuous Drw data packets with JPEG segments]
```

### Keepalive

Camera sends `P2PAlive` (0xf1e0) every ~400ms; app replies with
`P2PAliveAck` (0xf1e1). Session times out if no packets received
for ~5 seconds.

### Frame Reassembly

JPEG frames arrive as multiple Drw data packets. A new frame starts
either with a framed packet (stream_type = 0x03 with 32-byte header)
or unframed data beginning with `FF D8 FF DB`. Continuation packets
are appended in sequence-number order. Out-of-sequence packets mark
the frame as corrupt and it is dropped.

### Encryption

Control payloads use a trivial cipher:
1. Flip the LSB of every byte (odd→even, even→odd).
2. Rotate the buffer left (encrypt) or right (decrypt) by 4 positions.

---

## Architecture

### New Files

```
Services/Camera/A9/
├── A9Protocol.cs          — Protocol constants, packet builders/parsers,
│                            XqBytesEnc/Dec cipher
├── A9Session.cs           — UDP session lifecycle: discovery, handshake,
│                            keepalive, frame reassembly
└── A9CameraProvider.cs    — ICameraProvider implementation with reconnect logic
```

### Integration Points

| Component          | Change                                              |
|--------------------|-----------------------------------------------------|
| `ICameraProvider`  | New `A9CameraProvider` implementing the interface    |
| `ISettingsService` | Add `A9CameraIp`, `A9CameraUid`, `A9CameraUsername`, `A9CameraPassword` |
| `SettingsService`  | Back new properties with `Preferences`               |
| `ServiceExtensions`| Register `A9CameraProvider` as `ICameraProvider`     |
| Settings UI        | Add A9 through Settings > Devices > AddDevices, then show it in the unified Connected Devices list |

### Key Design Decisions

1. **Current A9/X5 path is UDP.** The implemented cam-reverse path uses UDP
   despite what one might assume from "IP camera". All packets are datagrams
   on port 32108. The saved `pmpt.md` describes a possible TCP PPPP/iLnk
   variant; that work is tracked separately in phases 10-13.

2. **Current A9/X5 path is MJPEG.** The TXW817 SoC has a hardware MJPEG
   encoder (VGA / 720p) and no H.264 capability. The datasheet
   (taixin-semi.com) explicitly lists "内置 MJPEG" with no mention of
   H.264. The cam-reverse project confirms all video payloads are raw
   JPEG frames (`FF D8 FF DB`), and the HTTP server outputs an MJPEG
   stream. This aligns perfectly with `ICameraProvider.CaptureFrameAsync()`
   returning `byte[]` JPEG data — no video decoder needed.
   Note: some *other* PPPP cameras (DG** prefix, vi365 app) use a
   JSON-based protocol variant and may have different codecs, but the
   A9/X5 family is strictly MJPEG.

3. **No FFmpeg dependency for UDP/MJPEG.** Since the implemented stream is
   already JPEG, it does not need FFmpeg.AutoGen or LibVLCSharp. Decoder work
   belongs to the optional TCP/H.264 variant phases if real hardware requires
   it.

4. **Reconnection.** `A9CameraProvider` wraps `A9Session` with retry
   logic — up to 5 attempts with 3-second delays. On session disconnect
   (timeout or camera drop), automatic reconnection is attempted.

5. **Provider ID:** `"a9-camera"` — selectable via CameraManager like
   any other camera source.

6. **Discovery should be protocol-adaptive.** Many A9 cameras use PPPP/iLnk,
   but some expose direct RTSP or HTTP MJPEG. Phase 5 should probe direct
   RTSP/MJPEG endpoints first and use PPPP/iLnk as the fallback path.

7. **V720/Naxclow is a separate variant.** The `intx82/a9-v720` code documents
   another A9 family using the V720/Naxclow app, AP IP `192.168.169.1`, and a
   custom TCP `6123` frame protocol. That work belongs in phase 14.

---

## Settings

| Setting             | Type     | Default       | Stored In     |
|---------------------|----------|---------------|---------------|
| `A9CameraIp`        | `string` | —             | `Preferences` |
| `A9CameraUid`       | `string` | —             | `Preferences` |
| `A9CameraUsername`   | `string` | `"admin"`     | `Preferences` |
| `A9CameraPassword`  | `string` | `"admin"`     | `Preferences` |

---

## Phases

Roadmap: [M38 - A9 Camera Roadmap](./roadmap.md)

Detailed phase docs:

- [Phase 0 - A9 Hardware Probe CLI And RealTests](./phase-0-realtests.md)
- [Phase 1 - Protocol & Provider](./phase-1-protocol-provider.md)
- [Phase 2 - Testing & Validation](./phase-2-testing-validation.md)
- [Phase 3 - Settings UI](./phase-3-settings-ui.md)
- [Phase 4 - Enhancements](./phase-4-enhancements.md)
- [Phase 5 - A9 Discovery](./phase-5-discovery.md)
- [Phase 6 - Known A9 Devices](./phase-6-known-devices-multiple-cameras.md)
- [Phase 7 - Stream Controls & Diagnostics](./phase-7-stream-controls-diagnostics.md)
- [Phase 8 - A9 Audio Input](./phase-8-audio-input.md)
- [Phase 9 - A9 Capture Preview](./phase-9-capture-preview.md)
- [Phase 10 - Protocol Variant Spike](./phase-10-protocol-variant-spike.md)
- [Phase 11 - TCP PPPP/iLnk H.264 Session](./phase-11-tcp-h264-session.md)
- [Phase 12 - H.264 Decoding](./phase-12-h264-decoding.md)
- [Phase 13 - Public A9 Camera API](./phase-13-public-api.md)
- [Phase 14 - V720/Naxclow A9 Variant](./phase-14-v720-naxclow-variant.md)

### Phase 0 - A9 Hardware Probe CLI And RealTests

- [ ] Define the human-in-the-loop A9 probe CLI flow
- [ ] Add a CLI that can print readable and JSON probe output
- [ ] Document env vars and commands for running A9 hardware tests
- [ ] Keep real tests skipped unless explicitly enabled
- [ ] Add discovery/protocol matrix RealTests that reuse the CLI probe services
      after the user switches on one camera

### Phase 1 — Protocol & Provider

- [x] `A9Protocol.cs` — packet builders, parsers, cipher
- [x] `A9Session.cs` — session lifecycle and frame reassembly
- [x] `A9CameraProvider.cs` — ICameraProvider with reconnect
- [x] Settings properties on `ISettingsService` / `SettingsService`
- [x] DI registration in `ServiceExtensions.AddCameraServices()`
- [x] Build verification
- [x] Unit tests for protocol helpers (XqBytesEnc/Dec, packet builders)

### Phase 2 — Testing & Validation

- [x] Unit tests for `A9Protocol` (cipher round-trip, packet structure)
- [x] Integration test with mock UDP server
- [x] Real-hardware test with physical A9 camera, gated by `A9_E2E=1`
- [x] Packet-loss resilience testing

### Phase 3 — Settings UI

Settings now uses the M37 flow:

1. Settings > Devices has one **+ Connect Device** entry point.
2. **+ Connect Device** opens `AddDevicesPage`.
3. `AddDevicesPage` lists addable device cards. It currently starts with
   **Add Cyan Glasses**.
4. Connected hardware renders in one unified **Connected Devices** card list on
   `DeviceSettingsPage`; device-specific configuration should not become a
   separate inline section there.

For A9, phase 3 should plug into that flow instead of adding a standalone
`DeviceSettingsPage` section.

```markui
# Add Devices

v------------------------------------------------------v
| #camera Add A9 Camera                                |
| Connect an A9/X5 IP camera over iLnkP2P/PPPP.        |
v------------------------------------------------------v
```

- [x] Add **Add A9 Camera** as a second card option on `AddDevicesPage`.
- [x] Add `AddA9CameraCommand` on `AddDevicesViewModel`, routed to a new A9
      setup page.
- [x] Add an A9 setup page/view model for `A9CameraIp`, optional
      `A9CameraUid`, `A9CameraUsername`, and `A9CameraPassword`.
- [x] Default username/password to `admin` / `admin` when blank, matching the
      provider behavior.
- [x] Add Save and Test Connection actions. Test should validate the settings by
      starting the `A9CameraProvider` or a short-lived `A9Session`, then report
      success/failure in-page.
- [x] After a successful save/test, return to Settings > Devices or leave the
      user on the A9 setup page with a clear connected/ready status.
- [x] Show A9 in the unified **Connected Devices** list as a camera card when the
      `a9-camera` provider is configured and streaming/available.
- [x] Do not add a separate A9 block to `DeviceSettingsPage`; keep that page to
      connected-device cards, Source, Camera/Microphone/Speaker controls, and
      Button Mappings.
- [x] Ensure `A9 Camera` can be selected from the Custom **Camera Source** picker
      once configured.
- [x] Decide whether to add an `A9 Camera` source profile. Decision: no A9 source
      profile in this phase; A9 remains a Custom Camera Source option.
- [x] Add stable automation IDs for the AddDevices card, A9 setup fields,
      Save/Test buttons, status label, and connected-device card.
- [x] Unit tests cover A9 settings persistence and test-connection outcomes.
- [x] UI tests cover Settings > Devices > AddDevices > Add A9 Camera navigation
      and the A9 setup fields.

### Phase 4 — Enhancements (optional)

- [ ] Phase 5: Camera discovery with a Discover button on the A9 setup page
- [ ] Phase 6: Known/saved A9 devices and multiple camera support
- [ ] Phase 7: Stream controls, resolution switching, and diagnostics
- [ ] Phase 8: Optional A9 audio input
- [ ] Phase 9: A9 capture preview on the setup page
- [ ] Phase 10: Protocol variant spike for saved `pmpt.md`
- [ ] Phase 11: Optional TCP PPPP/iLnk H.264 session path
- [ ] Phase 12: Optional H.264 decoding
- [ ] Phase 13: Public A9 camera facade API
- [ ] Phase 14: V720/Naxclow AP-mode protocol variant

### Phase 5 — A9 Discovery

- [ ] Add **Discover** button to `A9CameraSettingsPage`
- [ ] Probe direct RTSP and HTTP MJPEG stream endpoints first
- [ ] Probe V720/Naxclow AP mode on `192.168.169.1:6123` when applicable
- [ ] Broadcast `LanSearch` and list `PunchPkt` replies on UDP `32108`
- [ ] Probe saved-prompt discovery variants on UDP `32108` and `20190`
- [ ] Selecting a discovered camera fills IP, UID, port, protocol, and stream URL
      fields

### Phase 6 — Known A9 Devices

- [ ] Persist more than one A9 camera
- [ ] Migrate existing single-camera settings into the saved-device list
- [ ] Let the user Use, Update, and Remove saved A9 cameras

### Phase 7 — Stream Controls & Diagnostics

- [ ] Persist selected resolution
- [ ] Add Restart Stream and Test Capture controls
- [ ] Show stream state, last frame time, dropped frames, and reconnects

### Phase 8 — A9 Audio Input (optional)

- [ ] Parse A9 audio packets
- [ ] Decode 8 KHz A-law PCM if viable
- [ ] Expose A9 audio as an optional microphone provider

### Phase 9 — A9 Capture Preview

- [ ] Add one-shot Capture Preview to the A9 setup page
- [ ] Render the captured frame and show byte count plus latency
- [ ] Keep preview separate from the main Take Picture workflow

### Phase 10 — Protocol Variant Spike

- [ ] Validate whether saved `pmpt.md` describes a real A9 variant or a
      separate PPPP camera family
- [ ] Capture discovery/session fixtures for RTSP, HTTP MJPEG, UDP `32108`,
      UDP `20190`, and TCP PPPP probes
- [ ] Document variant support in `protocol-variants.md`

### Phase 11 — TCP PPPP/iLnk H.264 Session

- [ ] Add an optional TCP session for variants confirmed by phase 10
- [ ] Implement login, session-key negotiation, and stream request commands
- [ ] Read raw H.264 NAL units without assuming RTSP or ONVIF

### Phase 12 — H.264 Decoding

- [ ] Add decoder abstraction and `VideoFrame` model
- [ ] Investigate FFmpeg.AutoGen first and LibVLCSharp as a fallback
- [ ] Keep decoder dependencies optional for the UDP/MJPEG path

### Phase 13 — Public A9 Camera API

- [ ] Add `A9Camera.DiscoverAsync()`
- [ ] Add `A9Camera.ConnectAsync(device)`
- [ ] Expose decoded frames and raw H.264 access where supported

### Phase 14 — V720/Naxclow A9 Variant

- [ ] Add Naxclow frame parser/builder
- [ ] Add AP-mode TCP `6123` session
- [ ] Detect `V720NaxclowAp` separately from RTSP, HTTP MJPEG, cam-reverse
      UDP/MJPEG, and TCP PPPP/iLnk
- [ ] Add hardware-gated V720/Naxclow RealTests

---

## References

- Protocol reverse engineering: https://github.com/DavidVentura/cam-reverse
- Camera hardware: TXW817 SoC, branded as X5 / A9
- App: YsxLite (Android)
- UDP port: 32108
- Default credentials: admin/admin
- Saved enhancement prompt: ./pmpt.md
- V720/Naxclow reference: https://github.com/intx82/a9-v720
