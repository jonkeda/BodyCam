# Phase 41 - Managed Live CGI And Channel Opener

**Status:** Completed for live-CGI framing - continue Phase 42 for native transport removal

## Roadmap Gate

This phase starts gate 3 from
[C#-Only Vue990 Stream Roadmap](./csharp-only-vue990-roadmap.md): replace native
session pieces with C# in a controlled order.

## Current Evidence

Phase 40 proved that the live media channel is understandable:

- Native `JNIApi.connect`, `JNIApi.login`, and `JNIApi.writeCgi` can open the
  stream.
- Native `client_read` can copy channel `1` bytes directly into C# buffers.
- The media payload is JPEG frames in a `55 AA 15 A8` Vue990 envelope.
- C# can extract still frames and assemble MJPEG AVI from those bytes.

What remained native before this phase:

- Session transport/open: `JNIApi.connect`
- Authentication: `JNIApi.login`
- Live-open control write: `JNIApi.writeCgi`
- Channel read: native `client_read`

## 2026-05-29 First Pass Result

The first managed-live-CGI oracle sent
`A9Vue990CgiCommandBuilder.BuildLiveStreamRequest()` through
`JNIApi.write(channel=1)` while keeping native `connect`, `login`, and
`client_read`.

Artifact directory:

- `.my/plan/m38-a9-camera/captures/phase-41-managed-live-cgi-2026-05-29-170844/`

Observed result:

- Native `connect` and `login` still succeeded.
- `JNIApi.write` accepted the C# payload: `bytes=69 result=69`.
- The command callback returned `type=24577 len=789` with `var result=0`.
- No follow-up stream-start callback was seen.
- `JNIApi.checkBuffer(channel=1)` stayed `[0,0,0]`.
- Native `client_read(channel=1)` returned `-3 bytes=0` for all oracle reads.
- No JPEG or AVI artifact was produced.

Interpretation:

- The current C# `D1` GET wrapper is not a byte-for-byte replacement for native
  `writeCgi`.
- This is not a decoder problem: Phase 40 already proved the channel media is
  JPEG-in-envelope and C# can save image/video once bytes are present.
- This is not a Windows firewall problem: the negative result happened on the
  Android phone while native connect/login still worked.
- The next attempts should only vary the live-CGI payload shape or disassemble
  `writeCgi`; do not return to broad HTTP/RTSP/UDP probing.

## 2026-05-29 Targeted Payload Matrix

The bounded `JNIApi.write` payload matrix was useful negative evidence:

- `d1-get-noslash`
- `raw-cgi`
- `raw-cgi-null`
- `raw-get-slash`
- `raw-get-noslash`

All five were accepted as bytes and returned the status callback, but none
produced the stream-start callback or channel bytes.

Native disassembly then showed why: `writeCgi` is not a raw write to channel
`1`. It builds a command-channel CGI body with credentials, then sends an
8-byte little-endian command header followed by that body on channel `0`.

Confirmed C# command frame:

- Header: `01 0A 00 00 61 00 00 00`
- Body:
  `GET /livestream.cgi?streamid=10&substream=0&loginuse=admin&loginpas=888888&user=admin&pwd=888888&`
- Send sequence:
  - `JNIApi.write(clientPtr, 0, header, 5000)`
  - `JNIApi.write(clientPtr, 0, body, 5000)`

Successful artifact directory:

- `.my/plan/m38-a9-camera/captures/phase-41-managed-live-cgi-command-cgi-split-2026-05-29-172506/`

Observed result:

- `JNIApi.write` sent the C# header and body on channel `0`.
- The expected callbacks arrived:
  - `type=24577 len=789`
  - `type=24631 len=33`
- `JNIApi.checkBuffer(channel=1)` reported `[0,0,37088]`.
- Native `client_read(channel=1)` produced `8` raw channel artifacts,
  `677756` bytes total.
- C# extracted `73` JPEG frames.
- First still frame:
  `native-channel-oracle-frames/channel-frame-000.jpg`, `640x480`, `9247`
  bytes, SHA-256
  `6CBF309650B4EAEC9B6712D8F679C7DA83CCDE398C5B711DC56AB757ACC90188`.
- MJPEG AVI:
  `native-channel-oracle-mjpeg.avi`, `677120` bytes, SHA-256
  `64A5607A0FEDFD0FC3510D2CBFED255192CB8C89C1867182ADFE1F9A502D8257`.

Current remaining native pieces:

- Session object and transport: `JNIApi.create`, `JNIApi.connect`
- Authentication helper: `JNIApi.login`
- Raw session write/read carrier: `JNIApi.write`, native `client_read`

The live-open protocol bytes are now C#-generated. The next phase must replace
the native session carrier, not change the CGI command again.

## Goal

Replace native pieces in small, testable slices until the Android path is C#
protocol code rather than native session code.

## Plan

1. Prove C# live-CGI frame parity.
   - Send `A9Vue990CgiCommandBuilder.BuildLiveStreamRequest()` through
     `JNIApi.write(channel=1)` instead of `JNIApi.writeCgi(...)`.
   - Keep native connect/login/read as the oracle.
   - Success means the same channel JPEG bytes arrive after the C# payload.
   - Result: native command-channel header plus credentialed CGI body works.

2. Promote channel envelope parsing.
   - Use `A9Vue990ChannelMediaExtractor` for all channel media extraction.
   - Record chunk header fields and JPEG offsets in reports.

3. Replace native channel read.
   - Use the Phase 40 envelope and CGI evidence to update the managed C#
     session opener.
   - First target is to receive the same `55 AA 15 A8` envelope from C#.

4. Replace native connect/login.
   - Only after C# can send the correct live-open payload and parse channel
     media.
   - Use the existing HLP2P/TCP relay builders plus new oracle evidence.

## Acceptance Criteria

- A run proves whether C# `JNIApi.write` payload can replace native
  `writeCgi`.
- If successful, the report saves a JPEG and AVI from channel bytes after the
  C# live-open payload.
- If unsuccessful, the report records the command callback/checkBuffer
  difference between native `writeCgi` and C# `write`.
- The next managed opener change is documented without adding another broad
  HTTP/UDP scan.

## Checklist

- [x] Phase 40 identified channel media envelope and JPEG payload.
- [x] C# channel media extractor exists and is tested.
- [x] Add a `managed_live_cgi` oracle option.
- [x] Run native transport with C# live-CGI payload.
- [x] Document first managed `D1` payload mismatch.
- [x] Add targeted managed live-CGI payload modes.
- [x] Save image/video after one C# live-CGI payload, or document all targeted
      mode mismatches.
- [x] Update the managed C# command builder with the confirmed live-open
      payload.
- [ ] Replace the remaining native session carrier in Phase 42.
