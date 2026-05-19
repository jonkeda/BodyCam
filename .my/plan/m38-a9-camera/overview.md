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
| Settings UI        | Add A9 camera configuration section (future: M37)    |

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

- [ ] Add A9 camera section to DeviceSettingsPage (or integrate with M37
      source profiles as an "A9 Camera" profile)
- [ ] IP address entry, optional UID, username/password fields
- [ ] Connection test button

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
