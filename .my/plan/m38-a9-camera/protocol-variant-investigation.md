# M38 A9 Protocol Variant Investigation

## Current Hardware

- SSID: `@MC-0025644`
- AP/camera IP: `192.168.168.1`
- PC Wi-Fi IP while connected: `192.168.168.100/24`
- Open TCP ports found so far: `81`
- Confirmed HTTP control API:
  - `http://192.168.168.1:81/get_status.cgi`
  - Device: `BK0025644WBPD`
  - Alias/chip hint: `BK7252N`
  - Firmware/app: `21.120.101.34`

## Ordered Possibilities To Test

1. **HTTP control API plus hidden local media endpoint**
   - Reason: port `81` is open and `get_status.cgi` works.
   - Test: more HTTP paths, methods, auth shapes, and content-type/body checks.
   - Success signal: JPEG bytes, multipart MJPEG, stream URL, or H.264-like bytes.

2. **HTTP control API only, with video unlocked by a setup/session command**
   - Reason: `get_status.cgi` works but generic stream paths return `404`.
   - Test: look for documented BK7252N/CY365/O-KAM status/control commands and
     non-mutating session/auth endpoints.
   - Success signal: login/session response or a documented stream-open command.

3. **Direct RTSP on a non-default path or port**
   - Reason: Phase 5 requires direct RTSP before custom protocols.
   - Test: port scan common RTSP ports and send `OPTIONS`/`DESCRIBE` if open.
   - Success signal: `RTSP/1.0` response.

4. **Direct HTTP MJPEG on a non-81 alternate port**
   - Reason: Phase 5 supports direct MJPEG; earlier scans used common ports but
     should be repeated while the AP is stable.
   - Test: TCP scan common web/video ports, then probe stream paths only where
     ports are open.
   - Success signal: JPEG or multipart response.

5. **cam-reverse UDP/MJPEG PPPP**
   - Reason: existing implementation supports it, but current camera did not
     answer UDP `32108`.
   - Test: rerun unicast and broadcast `LanSearch` while connected.
   - Success signal: `PunchPkt`.

6. **Prompt-listed PPPP/iLnk UDP discovery variants**
   - Reason: Phase 5/10 mention JSON discovery on UDP `32108` and binary
     discovery on UDP `20190`.
   - Test: send candidate JSON/binary probes and record raw responses.
   - Success signal: UID/IP/port in a UDP response.

7. **V720/Naxclow AP variant**
   - Reason: Phase 14 covers another A9 family, but the IP/SSID/port do not
     match this hardware.
   - Test: keep as low priority; only retry TCP `6123` if evidence changes.
   - Success signal: parseable Naxclow frame response.

8. **Cloud/app-mediated camera with no local stream**
   - Reason: BK7252N status response includes a large `server` value and no
     discovered local media endpoint so far.
   - Test: internet research for BK7252N/CY365/O-KAM local protocol details.
   - Success signal: documented local app handshake or confirmation that local
     video is unavailable without cloud/app pairing.

9. **Vue990 / VStarcam / VeePai P2P stack**
   - Reason: the vendor app APK contains VStarcam/VeePai P2P and player native
     libraries, and app strings reference the camera AP/status endpoint.
   - Test: capture focused Vue990 logcat or traffic during add/live-view, then
     build a targeted probe using the discovered DID/VUID/session parameters.
   - Success signal: a repeatable connect/login/open-stream sequence or player
     source URL/handle.

## Working Conclusion

Do not implement Phase 11 or Phase 14 yet. The current hardware evidence points
to a BK7252N camera whose stock firmware exposes a local HTTP status/control
endpoint, while Vue990 points to a VStarcam/VeePai P2P/player stack for video.

## 2026-05-28 Investigation Pass Results

| Possibility | Result | Evidence |
|-------------|--------|----------|
| HTTP control API plus hidden local media endpoint | Not found | Broad TCP scan found only port `81`; 75 non-mutating HTTP candidates on port `81` matched only `get_status.cgi`. |
| HTTP control API with stream unlocked by local setup/session command | Unproven | No non-mutating local login/session/media command was found; public notes point to CY365/SHIX behavior but not to a stock local stream URL for this exact device. |
| Direct RTSP on default or alternate port | Not found | No RTSP-like TCP port was open (`554`, `8554`, `10554`, etc.). |
| Direct HTTP MJPEG on alternate port | Not found | No alternate HTTP/video port was open; port `81` did not expose MJPEG/JPEG stream paths. |
| cam-reverse UDP/MJPEG PPPP | Not found | UDP `32108` `LanSearch` to unicast and broadcast targets received no `PunchPkt`. |
| Prompt-listed PPPP/iLnk UDP discovery variants | Not found | JSON, binary, and plain-text discovery probes on UDP `32108` and `20190` received no responses. |
| V720/Naxclow AP variant | Skipped by evidence | SSID is `@MC...`, AP IP is `192.168.168.1`, and TCP `6123` is closed. |
| Cloud/app-mediated BK7252N/CY365/SHIX variant | Plausible next branch | The status response reports `BK7252N`; public research links CY365-style cameras to SHIX JSON over PPPP/P2P rather than RTSP/MJPEG local URLs. |
| Vue990 / VStarcam / VeePai P2P stack | Confirmed app stream path, not yet reusable outside app | Vue990 uses `libOKSMARTPPCS.so` and `libOKSMARTPLAY.so`, exposes VStarcam/VeePai P2P and player APIs, and contains strings for `192.168.168.1`, `get_status.cgi`, VUID, and `clientSetVuid`. The exact Vue990 status URL returns `support_vuid=1` and `vuidResult=1`. During live view, status changes to `current_users=1`, but no direct local stream port/path appears. ADB live logcat captured `P2PClient.c` connecting to `BKGD00000100FMQLN` with the camera's `DAS-...` server parameter, followed by `app_source_live` frame reads around 9.8-10.2 KB. |

## Next Viable Options

1. Build a targeted VStarcam/VeePai/PPCS probe or JNI harness using the
   captured values: VUID `BK0025644WBPD`, client ID `BKGD00000100FMQLN`, and
   the camera's `DAS-...` server parameter.
2. Capture one more focused live-start log if needed, with logcat started
   immediately before tapping live view, to catch any login/open-stream command
   lines that were not printed in the current capture.
3. Investigate firmware/OpenCam options only if stock-firmware local streaming
   is not available and replacing firmware is acceptable.
