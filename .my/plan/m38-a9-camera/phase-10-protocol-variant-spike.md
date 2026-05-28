# Phase 10 - Protocol Variant Spike

**Status:** Planned

## Goal

Resolve the mismatch between direct-stream A9 variants, the implemented A9/X5
UDP/MJPEG path, the V720/Naxclow AP-mode protocol, and the new `pmpt.md`
requirements for PPPP/iLnk cameras that may use TCP sessions, UDP discovery on
ports `32108` and `20190`, and raw H.264 streams.

This phase should not replace the current provider. It should identify whether
the prompt describes a second A9 mini WiFi camera variant, a different PPPP
family, or a mistaken protocol assumption.

## Background

The completed M38 phase 1 provider is based on the `cam-reverse` A9/X5 flow:

- UDP port `32108`
- `LanSearch` / `PunchPkt` discovery
- `Drw` video packets
- JPEG frame reassembly
- no decoder dependency

The saved prompt adds requirements that point to another shape:

- UDP broadcast discovery on `32108` and `20190`
- PPPP/iLnk handshake over TCP
- login packet, session key negotiation, stream request command
- raw H.264 NAL units
- FFmpeg.AutoGen or LibVLCSharp decoding

The latest phase 5 guidance adds the direct-stream rule:

- probe RTSP and HTTP MJPEG first
- use PPPP/iLnk only when direct stream probes fail

The `intx82/a9-v720` reference adds a V720/Naxclow shape:

- AP IP `192.168.169.1`
- TCP port `6123`
- custom Naxclow frames carrying JSON commands
- live JPEG packets and optional G.711 A-law audio
- optional STA-mode fake-server path using DNS redirection, MQTT, and TCP/UDP
  relay channels

Treat all of these as protocol variants until hardware captures prove otherwise.

## Implementation

1. Add a small protocol-variant investigation harness in tests or tools that can:
   - probe RTSP on TCP `554`
   - probe HTTP MJPEG on `80` and `8080`
   - probe V720/Naxclow AP mode on TCP `6123`
   - send UDP discovery probes on `32108` and `20190`
   - log raw response bytes
   - attempt TCP connection probes only to discovered endpoints
   - record whether the camera emits RTSP, HTTP MJPEG, V720/Naxclow JPEG,
     PPPP JPEG frames, H.264 NAL units, or neither
2. Add packet fixtures for each response type found.
3. Create `protocol-variants.md` with a compatibility matrix:
   - model label / UID prefix
   - direct RTSP support
   - direct HTTP MJPEG support
   - V720/Naxclow AP support
   - discovery port and response format
   - session transport
   - video codec
   - implemented provider path
4. Decide the internal variant enum names used by discovery/settings.
5. Update phase 11 or close it as not applicable based on the spike result.

## Files

- `.my/plan/m38-a9-camera/protocol-variants.md`
- `src/BodyCam.Tests/Services/Camera/A9/A9DiscoveryVariantTests.cs`
- `src/BodyCam.Tests/Services/Camera/A9/Fixtures/*`
- Optional: `tools/a9-discovery-probe/*`

## Acceptance Criteria

- The plan clearly documents which detected variants are supported now and
  which belong in later phases.
- Discovery response fixtures exist for every implemented parser.
- Current UDP/MJPEG tests still pass.
- No app UI switches to TCP/H.264 or V720/Naxclow behavior until this phase
  confirms the correct variant selection rules.
