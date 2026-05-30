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
- [Phase 15 - Vue990 VStarcam/VeePai PPCS Harness](./phase-15-vue990-vstarcam-ppcs-harness.md)
- [Phase 16 - C#-First Vue990 Capture Download](./phase-16-csharp-capture-download.md)
- [Phase 17 - C# Vendor Adapter And Java Reduction](./phase-17-csharp-vendor-adapter.md)
- [Phase 18 - Pure C# VStarcam/PPCS Replacement](./phase-18-pure-csharp-ppcs-replacement.md)
- [Phase 19 - Generated Binding Screenshot Spike](./phase-19-generated-binding-screenshot-spike.md)
- [Phase 20 - C# Render Surface Capture Fallback](./phase-20-csharp-render-surface-capture-fallback.md)
- [Phase 21 - C# MJPEG AVI Video Artifact](./phase-21-csharp-mjpeg-avi-video-artifact.md)
- [Phase 22 - Windows-Native C# Capture](./phase-22-windows-native-csharp-capture.md)
- [Phase 23 - Managed Vue990 PPCS Control And Raw Channel](./phase-23-managed-vue990-ppcs-control.md)
- [Phase 24 - DAS And ConnectByServer Reverse Path](./phase-24-das-connectbyserver-reverse.md)
- [Phase 25 - Managed HLP2P Relay Hello](./phase-25-managed-hlp2p-relay-hello.md)
- [Phase 26 - Android C# Capture Stabilization And Windows Packaging](./phase-26-android-csharp-capture-stabilization.md)
- [Phase 27 - Windows C# Android Capture Orchestration](./phase-27-windows-csharp-android-capture-orchestration.md)
- [Phase 28 - Android Native Packet Oracle](./phase-28-native-packet-oracle.md)
- [Phase 29 - Fake DAS Local Relay Oracle](./phase-29-fake-das-local-relay-oracle.md)
- [Phase 30 - Native Second-Stage Packet Oracle](./phase-30-native-second-stage-oracle.md)
- [Phase 31 - Cross-Platform C# PPCS Library](./phase-31-cross-platform-csharp-ppcs-library.md)
- [Phase 32 - Parameterized Second-Stage Fields](./phase-32-parameterized-second-stage-fields.md)
- [Phase 33 - Managed Android Local Stream](./phase-33-managed-android-local-stream.md)
- [Phase 34 - Managed PPCS Relay and Channel Dump](./phase-34-managed-ppcs-relay-channel-dump.md)
- [Phase 35 - Android Managed C# Stream Attempt](./phase-35-android-managed-csharp-stream-attempt.md)
- [Phase 36 - Vue990 Proprietary Relay Encryption](./phase-36-vue990-proprietary-relay-encryption.md)
- [Phase 37 - Android Managed Direct Port Matrix](./phase-37-android-managed-direct-port-matrix.md)
- [Phase 38 - Managed TCP Relay Builder](./phase-38-managed-tcp-relay-builder.md)
- [Phase 39 - Android C# UDP Session Opener](./phase-39-android-csharp-udp-session-opener.md)
- [Phase 40 - Native Channel Session Oracle](./phase-40-native-channel-session-oracle.md)
- [Phase 41 - Managed Live CGI And Channel Opener](./phase-41-managed-live-cgi-and-channel-opener.md)
- [Phase 42 - Managed Session Transport Replacement](./phase-42-managed-session-transport-replacement.md)
- [Phase 43 - Native HLP2P Connect-By-Server Map](./phase-43-native-hlp2p-connect-map.md)
- [Phase 44 - Managed LAN-Hole Session Opener](./phase-44-managed-lan-hole-session-opener.md)
- [Phase 45 - Native LAN-Hole Session Engine Map](./phase-45-native-lan-hole-session-engine-map.md)
- [Phase 47 - Managed HLP2P Direct C# Capture](./phase-47-managed-hlp2p-direct-csharp-capture.md)
- [Phase 48 - Control Derivation And Windows Port](./phase-48-control-derivation-and-windows-port.md)
- [Phase 49 - Final C# Hardening](./phase-49-final-csharp-hardening.md)
- [Phase 50 - Vue990 BodyCam Camera Provider](./phase-50-vue990-bodycam-provider.md)

### Phase 0 - A9 Hardware Probe CLI And RealTests

- [x] Define the human-in-the-loop A9 probe CLI flow
- [x] Add a CLI that can print readable and JSON probe output
- [x] Document env vars and commands for running A9 hardware tests
- [x] Keep real tests skipped unless explicitly enabled
- [x] Add discovery/protocol matrix RealTests that reuse the CLI probe services
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
- [ ] Phase 15: Vue990 VStarcam/VeePai PPCS harness for `@MC-0025644`
      (PPCS connect/control proven; player/frame metadata pending)
- [ ] Phase 16: C#-first Vue990 capture download for explicit image/video
      artifacts after the Phase 15 metadata path is proven
- [ ] Phase 17: Move the proven vendor-library path into C# orchestration and
      reduce Java to the smallest required JNI surface
- [ ] Phase 18: Replace the vendor libraries and Java stubs with a pure C#
      VStarcam/PPCS implementation
- [ ] Phase 19: Use .NET Android generated bindings to run the PPCS/player
      session from C# and try `AppPlayerApi.Screenshot` for one still image
- [ ] Phase 20: If the vendor screenshot path fails, capture one rendered frame
      from a C# owned Android surface
- [x] Phase 21: If vendor video download fails, capture a bounded screenshot
      sequence and package it into a C# MJPEG AVI artifact
- [ ] Phase 22: Retrieve image and video directly from Windows C# without the
      Android phone helper
- [ ] Phase 23: Implement managed Vue990 PPCS connect/login/live-open and save
      raw channel bytes from Windows
- [ ] Phase 24: Recover DAS decode and native `ConnectByServer` parsing after
      direct transport fingerprinting found no local socket signal
- [ ] Phase 25: Implement the managed HLP2P relay hello/session-open packet
      sequence for decoded TCP `65527` relay hosts
- [x] Phase 26: Stabilize Android C# picture/frame download and package pulled
      frames into a Windows C# MJPEG AVI
- [x] Phase 27: Add a Windows C# command that drives the Android C# probe and
      downloads picture/video artifacts
- [x] Phase 28: Confirm native empty packet creator bytes with an Android C#
      oracle
- [x] Phase 29: Try fake DAS/local relay capture; blocked by likely DAS
      checksum/token validation before any local connection is opened
- [x] Phase 30: Recover larger native second-stage packet helpers through a
      loopback oracle
- [ ] Phase 31: Build the final cross-platform C#-only PPCS library
- [ ] Phase 32: Map the dynamic fields needed for relay-accepted
      `TCPRlyReq` / `TCPRSLgn` frames
- [x] Phase 33: Exhaust Android local HTTP/UDP media probing without native
      Vue990 libraries
- [x] Phase 34: Run managed relay/channel candidate replay from Android and
      Windows; no response bytes yet
- [x] Phase 35: Add Android C# stream/control code and prove this camera does
      not answer the local classic PPPP session
- [ ] Phase 36: Port Vue990 proprietary relay encryption and reproduce a
      relay-accepted second-stage request in C#
- [x] Phase 37: Expand Android direct/local C# probing and rule out more local
      endpoint/session guesses
- [x] Phase 38: Port native TCP relay frame construction to managed C#
- [x] Phase 39: Close Android UDP/HLP2P session opener as negative evidence
- [x] Phase 40: Use native channel oracle to expose JPEG-in-envelope media
- [x] Phase 41: Replace native `writeCgi` framing with C# command bytes and
      save image/video from the resulting channel bytes
- [ ] Phase 42: Replace the remaining native session transport/read carrier
- [x] Phase 43: Capture native HLP2P debug logs and identify the LAN-hole
      session path
- [ ] Phase 44: Implement a focused managed C# LAN-hole session opener

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

### Phase 15 - Vue990 VStarcam/VeePai PPCS Harness

- [x] Create the Phase 15 plan doc
- [x] Recover Vue990 `com.vstarcam.JNIApi` signatures and call order
- [x] Build an Android-side harness around the required VStarcam/VeePai native
      libraries
- [x] Connect to `@MC-0025644` with the captured VUID/client/server values
- [x] Add VeePai player/frame metadata observation, then promote the repeatable
      path to hardware-gated RealTests

### Phase 16 - C#-First Vue990 Capture Download

- [x] Create the Phase 16 plan doc
- [x] Move Phase 15 orchestration into C# where practical while keeping only
      minimal JNI stubs if the vendor libraries require Java class names
- [x] Add explicit still-image capture from the proven live stream path
- [x] Add explicit short-video capture after still image works
- [x] Add capture-specific hardware-gated RealTests

### Phase 17 - C# Vendor Adapter And Java Reduction

- [x] Create the Phase 17 plan doc
- [ ] Add C# wrappers for the known `JNIApi` and `AppPlayerApi` calls
- [ ] Move the PPCS/player state machine out of `PpcsProbeBridge.java`
- [ ] Keep only thin Java native declarations/interfaces if required by the
      vendor JNI exports
- [x] Prove the metadata and Phase 16 image-capture paths through C#
      orchestration

### Phase 18 - Pure C# VStarcam/PPCS Replacement

- [x] Create the Phase 18 plan doc
- [x] Add the PPCS protocol notes document
- [ ] Instrument the vendor path as an oracle
- [ ] Reverse-engineer the PPCS connect/login/channel framing
- [ ] Implement managed C# connect/login/CGI-over-PPCS
- [ ] Implement managed channel `1` stream read
- [ ] Download one image without vendor libraries or Java stubs

### Phase 19 - Generated Binding Screenshot Spike

- [x] Create the Phase 19 plan doc
- [x] Add screenshot/save/download declarations to `AppPlayerApi`
- [x] Add a C# `Vue990PpcsSession` generated-binding runner
- [x] Build and install the Android probe
- [x] Add a hardware-gated still-image capture RealTest
- [x] Run the explicit still-image capture while the phone is on
      `@MC-0025644`

### Phase 20 - C# Render Surface Capture Fallback

- [x] Create the Phase 20 fallback plan doc
- [ ] Try `TextureView.GetBitmap` if `AppPlayerApi.Screenshot` fails
- [ ] Try `PixelCopy` if the texture readback path fails
- [ ] Pull and verify one rendered-frame image artifact

### Phase 21 - C# MJPEG AVI Video Artifact

- [x] Create the Phase 21 fallback plan doc
- [x] Try vendor `startDown` and record that it returns `False`
- [x] Add C# bounded screenshot-sequence video capture
- [x] Add pure C# MJPEG AVI writer
- [x] Pull and verify manual AVI artifact
- [x] Add and run hardware-gated video RealTest

### Phase 22 - Windows-Native C# Capture

- [x] Create the Phase 22 plan doc
- [x] Add Windows-only status/topology probe
- [x] Add Windows-native RealTest gates
- [x] Prove Windows-native status while connected to `@MC-0025644`
- [x] Add direct Windows HTTP media probe and rule out snapshot/video URLs
- [ ] Implement managed PPCS connect/login from Windows
- [ ] Open live channel `1` from Windows
- [ ] Save one Windows-captured JPEG
- [ ] Save one Windows-captured short-video artifact

### Phase 23 - Managed Vue990 PPCS Control And Raw Channel

- [x] Create the Phase 23 plan doc
- [x] Add managed CGI-over-PPCS request frame builder
- [x] Add unit tests for the CGI request frame
- [x] Decode the `DAS-...` server parameter shape and run bounded transport
      fingerprinting
- [x] Add a hardware-gated Windows transport fingerprint RealTest
- [ ] Implement managed PPCS connect/login from Windows
- [ ] Send live-open CGI and save bounded channel `1` bytes

### Phase 24 - DAS And ConnectByServer Reverse Path

- [x] Create the Phase 24 plan doc
- [x] Locate `ConnectByServer` / DAS parser paths in `libOKSMARTPPCS.so`
- [x] Implement managed DAS decode candidates with explicit failure reporting
- [x] Map which transport family is selected by `connectType=0x3F`,
      `p2pType=1`
- [x] Extract decoded relay hosts and probe them from Windows
- [x] Return the new stop condition to Phase 23/25: decoded relay TCP `65527`
      opens, but the managed relay hello/session-open packet is missing

### Phase 25 - Managed HLP2P Relay Hello

- [x] Create the Phase 25 plan doc
- [x] Reverse native empty-header hello/server-request packet evidence
- [x] Implement first managed C# relay hello packet builders
- [x] Add a bounded relay-hello probe command
- [ ] Receive first relay response bytes from TCP `65527`
- [ ] Send live-open CGI and save bounded channel `1` bytes after session open

### Phase 26 - Android C# Capture Stabilization And Windows Packaging

- [x] Create the Phase 26 plan doc
- [x] Add a timeout around Android main-thread native calls
- [x] Save a verified frame-sequence manifest from the Android C# probe
- [x] Pull a fresh still image and six frame JPEGs from the phone
- [x] Assemble the pulled frame sequence into an MJPEG AVI on Windows with C#
- [x] Update the hardware-gated video RealTest to use pulled frames plus
      Windows C# AVI assembly
- [x] Run the hardware-gated video RealTest successfully

### Phase 27 - Windows C# Android Capture Orchestration

- [x] Create the Phase 27 plan doc
- [x] Add `BodyCam.A9Probe vue990-android-capture`
- [x] Pull a fresh still JPEG through the Android C# probe from Windows C#
- [x] Pull frame JPEGs and assemble a Windows C# MJPEG AVI
- [x] Document that this works as a C# command path but is not yet pure Windows
      PPCS replacement

### Phase 28 - Android Native Packet Oracle

- [x] Create the Phase 28 plan doc
- [x] Add Android C# native oracle mode
- [x] Confirm `create_Hello` writes `F1000000`
- [x] Confirm `create_RlyHello` writes `F1700000`
- [x] Confirm `create_SvrReq` writes `F2100000`
- [x] Add a loopback socket oracle for `TCPSend_Hello`
- [x] Add the native `TCPSend_Hello` bytes as a managed relay candidate

### Phase 29 - Fake DAS Local Relay Oracle

- [x] Create the Phase 29 plan doc
- [x] Add managed DAS re-encoding round-trip helper
- [x] Add Android fake-relay listener mode
- [x] Add Android `serverParam` override mode
- [x] Attempt local fake relay with short loopback, same-length loopback, and phone IP
- [ ] Capture native connect bytes against a local fake relay
- [x] Document blocked result: rewritten DAS values opened no local connection

### Phase 30 - Native Second-Stage Packet Oracle

- [x] Create the Phase 30 plan doc
- [x] Map `TCPSend_TCPRlyReq` / `TCPSend_TCPRSLgn` signatures
- [x] Add safe loopback oracle calls for second-stage packet helpers
- [x] Promote stable packets into managed C# builders and tests
- [x] Retest decoded relays with native-generated second-stage frames

### Phase 31 - Cross-Platform C# PPCS Library

- [x] Create the Phase 31 plan doc
- [ ] Receive relay response bytes from managed C#
- [ ] Complete managed login/control channel
- [ ] Retrieve still/video from Windows without vendor native libraries
- [ ] Retrieve still/video from Android without vendor native libraries

### Phase 32 - Parameterized Second-Stage Fields

- [x] Create the Phase 32 plan doc
- [x] Add Android oracle argument-variation runs
- [ ] Map native helper arguments to byte offsets
- [x] Partially map `Write_TCPRlyReq` / `Write_TCPRSLgn` offsets
- [ ] Replace fixed second-stage oracle constants with parameterized C# builders
- [ ] Retest decoded relays with live camera/session arguments

### Phase 33 - Managed Android Local Stream

- [x] Create the Phase 33 plan doc
- [x] Probe Android local HTTP/CGI/UDP without native libraries
- [x] Confirm no local media bytes are exposed

### Phase 34 - Managed PPCS Relay and Channel Dump

- [x] Create the Phase 34 plan doc
- [x] Run Android relay fallback
- [x] Run Windows cached relay candidate replay
- [x] Confirm fixed candidate replay opens sockets but returns no bytes

### Phase 35 - Android Managed C# Stream Attempt

- [x] Create the Phase 35 plan doc
- [x] Add managed C# control-channel builders
- [x] Add Android classic PPPP stream attempt
- [x] Confirm this camera returns no local `PunchPkt` / `P2pReady`

### Phase 36 - Vue990 Proprietary Relay Encryption

- [x] Create the Phase 36 plan doc
- [x] Port managed proprietary P2P/TCP relay codec
- [ ] Reproduce native `TCPSend_TCPRlyReq` byte-for-byte in C#
- [ ] Send a relay-accepted second-stage request and capture response bytes

### Phase 37 - Android Managed Direct Port Matrix

- [x] Create the Phase 37 plan doc
- [x] Bind the Android C# probe to the active camera Wi-Fi network
- [x] Acquire Android multicast and Wi-Fi locks during probing
- [x] Expand local UDP/session probe ports and PPPP/HLP2P variants
- [x] Test while the Vue990 vendor app is live
- [x] Test again after force-stopping the Vue990 vendor app
- [x] Confirm no C#-only image/video artifact is produced from local Android direct probing

### Phase 38 - Managed TCP Relay Builder

- [x] Create the Phase 38 plan doc
- [x] Port native `TCPSend_MSG` wire framing to C#
- [x] Port native TCP relay CRC to C#
- [x] Build managed `TCPRlyReq` and `TCPRSLgn` packets
- [x] Verify both managed packets against native Phase 32 vectors
- [x] Send generated packets to decoded TCP `65527` relay hosts
- [x] Confirm relays still return no response bytes

---

## References

- Protocol reverse engineering: https://github.com/DavidVentura/cam-reverse
- Camera hardware: TXW817 SoC, branded as X5 / A9
- App: YsxLite (Android)
- UDP port: 32108
- Default credentials: admin/admin
- Saved enhancement prompt: ./pmpt.md
- V720/Naxclow reference: https://github.com/intx82/a9-v720
