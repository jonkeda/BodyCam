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

1. **UDP, not TCP.** The protocol uses UDP despite what one might assume
   from "IP camera". All packets are datagrams on port 32108.

2. **MJPEG only — no H.264.** The TXW817 SoC has a hardware MJPEG
   encoder (VGA / 720p) and no H.264 capability. The datasheet
   (taixin-semi.com) explicitly lists "内置 MJPEG" with no mention of
   H.264. The cam-reverse project confirms all video payloads are raw
   JPEG frames (`FF D8 FF DB`), and the HTTP server outputs an MJPEG
   stream. This aligns perfectly with `ICameraProvider.CaptureFrameAsync()`
   returning `byte[]` JPEG data — no video decoder needed.
   Note: some *other* PPPP cameras (DG** prefix, vi365 app) use a
   JSON-based protocol variant and may have different codecs, but the
   A9/X5 family is strictly MJPEG.

3. **No FFmpeg dependency.** Since the stream is already JPEG, we don't
   need FFmpeg.AutoGen or LibVLCSharp.

4. **Reconnection.** `A9CameraProvider` wraps `A9Session` with retry
   logic — up to 5 attempts with 3-second delays. On session disconnect
   (timeout or camera drop), automatic reconnection is attempted.

5. **Provider ID:** `"a9-camera"` — selectable via CameraManager like
   any other camera source.

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

Detailed phase docs:

- [Phase 1 - Protocol & Provider](./phase-1-protocol-provider.md)
- [Phase 2 - Testing & Validation](./phase-2-testing-validation.md)
- [Phase 3 - Settings UI](./phase-3-settings-ui.md)
- [Phase 4 - Enhancements](./phase-4-enhancements.md)

### Phase 1 — Protocol & Provider (current)

- [x] `A9Protocol.cs` — packet builders, parsers, cipher
- [x] `A9Session.cs` — session lifecycle and frame reassembly
- [x] `A9CameraProvider.cs` — ICameraProvider with reconnect
- [x] Settings properties on `ISettingsService` / `SettingsService`
- [x] DI registration in `ServiceExtensions.AddCameraServices()`
- [ ] Build verification
- [ ] Unit tests for protocol helpers (XqBytesEnc/Dec, packet builders)

### Phase 2 — Testing & Validation

- [ ] Unit tests for `A9Protocol` (cipher round-trip, packet structure)
- [ ] Integration test with mock UDP server
- [ ] Real-hardware test with physical A9 camera
- [ ] Packet-loss resilience testing

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

- [ ] Add **Add A9 Camera** as a second card option on `AddDevicesPage`.
- [ ] Add `AddA9CameraCommand` on `AddDevicesViewModel`, routed to a new A9
      setup page.
- [ ] Add an A9 setup page/view model for `A9CameraIp`, optional
      `A9CameraUid`, `A9CameraUsername`, and `A9CameraPassword`.
- [ ] Default username/password to `admin` / `admin` when blank, matching the
      provider behavior.
- [ ] Add Save and Test Connection actions. Test should validate the settings by
      starting the `A9CameraProvider` or a short-lived `A9Session`, then report
      success/failure in-page.
- [ ] After a successful save/test, return to Settings > Devices or leave the
      user on the A9 setup page with a clear connected/ready status.
- [ ] Show A9 in the unified **Connected Devices** list as a camera card when the
      `a9-camera` provider is configured and streaming/available.
- [ ] Do not add a separate A9 block to `DeviceSettingsPage`; keep that page to
      connected-device cards, Source, Camera/Microphone/Speaker controls, and
      Button Mappings.
- [ ] Ensure `A9 Camera` can be selected from the Custom **Camera Source** picker
      once configured.
- [ ] Decide whether to add an `A9 Camera` source profile. If added, it should be
      camera-only and preserve the current microphone/speaker choices.
- [ ] Add stable automation IDs for the AddDevices card, A9 setup fields,
      Save/Test buttons, status label, and connected-device card.
- [ ] Unit tests cover A9 settings persistence and test-connection outcomes.
- [ ] UI tests cover Settings > Devices > AddDevices > Add A9 Camera navigation
      and the A9 setup fields.

### Phase 4 — Enhancements (optional)

- [ ] Audio streaming (8KHz A-law PCM from the camera)
- [ ] Camera discovery (broadcast LanSearch, show found cameras)
- [ ] Multiple A9 camera support
- [ ] Packet-loss recovery (JPEG reset marker splicing)
- [ ] Resolution switching at runtime

---

## References

- Protocol reverse engineering: https://github.com/DavidVentura/cam-reverse
- Camera hardware: TXW817 SoC, branded as X5 / A9
- App: YsxLite (Android)
- UDP port: 32108
- Default credentials: admin/admin
