# Phase 18 - Pure C# VStarcam/PPCS Replacement

**Status:** In Progress - protocol replacement foundation for Phase 22

## Goal

Replace the Vue990/VStarcam vendor native libraries with managed C# code for
the `@MC-0025644` camera path.

This is the final "C# code for everything" phase for this camera family. Success
means BodyCam can connect to the camera, open the live stream, and download at
least one image without `libOKSMARTPPCS.so`, `libOKSMARTPLAY.so`, or Java JNI
stubs.

This phase started after Phase 16 produced controlled image artifacts, Phase 21
produced a bounded video artifact, and Phase 17 moved the working vendor path
under C# orchestration. The vendor path is now the oracle used to learn the
protocol.

The runtime deliverable is tracked in
[Phase 22 - Windows-Native C# Capture](./phase-22-windows-native-csharp-capture.md).
The immediate managed-control/raw-channel slice is tracked in
[Phase 23 - Managed Vue990 PPCS Control And Raw Channel](./phase-23-managed-vue990-ppcs-control.md).

## Current Proven Facts

- Camera SSID/tag: `@MC-0025644`
- Camera/AP IP: `192.168.168.1`
- Phone IP when connected: `192.168.168.101/24`
- Direct TCP surface: only port `81`
- Status URL:
  `http://192.168.168.1:81/get_status.cgi?loginuse=admin&loginpas=888888`
- VUID / real device id: `BK0025644WBPD`
- Vue990 P2P client id: `BKGD00000100FMQLN`
- Alias/chip hint: `BK7252N`
- Firmware/app version: `21.120.101.34`
- Native PPCS connect uses:
  - `connectType=0x3F`
  - `p2pType=1`
  - the `DAS-...` server parameter returned by `get_status.cgi`
- Login works with `admin` / `888888`.
- Live-open command is:
  `livestream.cgi?streamid=10&substream=0&`
- Live-open uses PPCS channel `1`.
- After live-open, channel `1` buffers stream bytes.
- VeePai player metadata reports `640x480`.
- Windows-native C# status now works while connected to `@MC-0025644`.
- Direct Windows HTTP media probing found no snapshot/video/livestream endpoint;
  only `get_status.cgi` returns `200`.
- Managed C# can now build the known CGI-over-PPCS live-open request frame.
- `libOKSMARTPLAY.so` exposes capture-related native APIs such as
  `screenshot`, `save`, `startDown`, `stopDown`, and `saveMP4`. These are useful
  for Phase 16 capture, but they are not part of the pure-C# end state.

## Missing Facts

To remove the vendor libraries, we still need:

- The exact PPCS connection handshake.
- The meaning of the `DAS-...` server parameter.
- Direct/LAN vs relay/cloud connection behavior.
- Authentication/session key generation.
- Channel framing for command/control and live stream data.
- The binary structure behind command listener types such as `24577` and
  `24631`.
- How to read stream payloads from channel `1`.
- A readable public channel API. Strings show internal `client_read` /
  `vp_channel_read`, but the current Java wrapper exposes only `checkBuffer`.
- Stream codec/container format before VeePai player decoding.
- Whether the payload is H.264/H.265/JPEG/custom framed data.
- Keepalive and reconnect behavior.

## Strategy

Do not start by rewriting the whole library. Replace the vendor stack one
observable boundary at a time.

### Stage 1 - Instrument The Vendor Path

Use the Phase 16/17 controlled app as an oracle.

- Log every C# call into the vendor wrapper with arguments and return values.
- Record command callback types, lengths, and safe prefixes.
- Add an explicit diagnostic mode for bounded channel metadata.
- If a safe read API exists, capture one bounded raw stream sample only after
  the user approves visual capture.
- Save all observations as local artifacts.

### Stage 2 - Static Reverse Engineering

Use local analysis of the pulled Vue990 libraries and APK.

- Inspect `libOKSMARTPPCS.so` for symbols and strings around connect, auth,
  channel read/write, `writeCgi`, keepalive, and relay handling.
- Inspect `libOKSMARTPLAY.so` only enough to identify stream format and frame
  headers.
- Document packet structures with evidence.
- Avoid broad speculative protocol implementation until packet boundaries are
  clear.

### Stage 3 - Managed PPCS Control Client

Implement the smallest C# client that can reproduce non-visual control flow.

- Fetch status.
- Parse VUID/client/server values.
- Establish PPCS connection.
- Login.
- Send `get_status.cgi` or another safe control CGI.
- Receive and parse the expected ack.
- Disconnect cleanly.

### Stage 4 - Managed Live Open

Extend the C# client to open channel `1`.

- Send `livestream.cgi?streamid=10&substream=0&`.
- Receive the `type=24631`-equivalent live-open ack.
- Observe stream bytes on the managed channel.
- Keep the run bounded.

### Stage 5 - Managed Image Decode

Download one image without vendor decoding.

- Identify frame boundaries.
- Decode one frame if it is JPEG, or extract a keyframe/encoded frame if the
  codec is H.264/H.265.
- If H.264/H.265, use an explicit decoder phase or platform decoder path rather
  than hiding decoding in the PPCS client.
- Save one still image artifact with dimensions and hash.

## Implementation Checklist

- [x] Create this Phase 18 plan doc.
- [x] Require Phase 16 still-image success before starting protocol replacement
      experiments.
- [x] Require Phase 17 C# orchestration before using the vendor path as an
      oracle.
- [x] Add a protocol-notes document for PPCS packet structures.
- [ ] Add a safe diagnostic mode to the Phase 17 C# adapter.
- [ ] Identify whether a bounded raw channel read is available through the
      current native wrappers.
- [ ] Capture a user-approved raw sample only after Phase 16 image capture is
      explicitly allowed.
- [ ] Reverse-engineer PPCS connect/login enough to write a managed control
      client.
- [x] Implement status fetch and identity parsing in shared C# code.
- [x] Add managed CGI-over-PPCS request frame builder.
- [ ] Implement managed PPCS handshake.
- [ ] Implement managed login.
- [ ] Implement managed CGI-over-PPCS write.
- [ ] Implement managed command ack parsing.
- [ ] Implement managed channel `1` stream read.
- [ ] Identify stream frame format.
- [ ] Decode or extract one image.
- [ ] Add hardware-gated RealTests that run with no vendor native libraries.
- [ ] Remove vendor library dependency from the final provider path.

## RealTests

Add a separate gate from Phase 16/17:

- `A9_E2E=1`
- `A9_PURE_CSHARP_E2E=1`

Control RealTest:

- No vendor native library path is loaded.
- Managed client fetches status.
- Managed client connects/logs in.
- Managed client sends a safe CGI command and receives an ack.

Image RealTest:

- Managed client opens live channel `1`.
- Managed client receives stream bytes.
- Managed client saves one image artifact.
- Image byte count, dimensions, and hash are printed.

## Acceptance Criteria

- The working `@MC-0025644` path no longer needs Vue990 native libraries.
- The working path no longer needs Java JNI stubs.
- The camera can be connected and opened from C# code.
- One still image can be downloaded from C# code.
- RealTests prove the no-vendor-libs path with explicit hardware gates.

## Risks

- PPCS may use relay/cloud behavior that is difficult to reproduce without the
  vendor library.
- Authentication may include crypto or server-side negotiation hidden in
  `libOKSMARTPPCS.so`.
- The stream may be compressed in a format that needs a decoder phase.
- Without root or packet capture, some observations may require static native
  reverse engineering.
- Firmware replacement could expose simpler streams, but it is a separate,
  higher-risk route and not part of this phase.

## Stop Conditions

Stop and document blockers if:

- The connection requires server-side secrets not present in the APK/status
  response.
- The stream cannot be read or identified without native-only callbacks.
- Replacing the protocol becomes riskier than keeping a small contained vendor
  adapter for this hardware.
