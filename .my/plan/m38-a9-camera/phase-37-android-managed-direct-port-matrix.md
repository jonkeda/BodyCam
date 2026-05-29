# Phase 37 - Android Managed Direct Port Matrix

**Status:** Completed - local Android C# stream path still produces no media

## Goal

Prove whether the Android phone can retrieve the Vue990/A9 camera stream with
managed C# only, while connected directly to the camera Wi-Fi, without using
`JNIApi`, `AppPlayerApi`, a relay bridge, or the Windows network stack.

The desired end state remains a shared C# implementation that can run on
Android first and then be reused from Windows.

## Implemented

- Added Android Wi-Fi process binding before managed probing.
- Added Android multicast and Wi-Fi locks around the direct probe.
- Expanded local UDP bind ports to include dynamic, `32108`, `65529`, and
  `65531`.
- Expanded remote probe ports to include `32108`, `20190`, `65529`, and
  `65531`.
- Sent multiple PPPP/HLP2P discovery/session variants:
  - classic LAN search
  - extended LAN search
  - client-id/VUID identity payloads
  - `P2pRequest`
  - `PunchTo`
  - XOR1-encoded variants
- Stopped filtering strictly on `192.168.168.1` so any non-self UDP response
  could be captured.
- Added raw UDP packet artifact saving for any non-self response.
- Added the managed channel-1 live CGI send path that will run if a PPPP
  session starts:
  `livestream.cgi?streamid=10&substream=0&`.

## Live Runs

### Vendor app still live

Artifacts:

- `.my/plan/m38-a9-camera/captures/phase-37-android-csharp-direct-matrix-2026-05-29-154636.json`
- `.my/plan/m38-a9-camera/captures/phase-33-android-managed-direct-2026-05-29-154646/`
- `.my/plan/m38-a9-camera/captures/phase-37-vue990-foreground-screenshot.png`

Observed:

- Android Wi-Fi bind succeeded.
- Multicast lock and Wi-Fi lock were acquired.
- Camera status on `192.168.168.1:81` remained reachable.
- Local TCP media candidates still returned no JPEG/MJPEG/H264 stream.
- UDP discovery produced only self-echo packets.
- No remote `PunchPkt` / `P2pReady` arrived.
- The Vue990 vendor app was visible in the foreground and showed live video.
- The Vue990 vendor app owned UDP `0.0.0.0:65529`.

### Vendor app force-stopped

Artifacts:

- `.my/plan/m38-a9-camera/captures/phase-37-android-csharp-direct-no-vendor-2026-05-29-155050.json`
- `.my/plan/m38-a9-camera/captures/phase-33-android-managed-direct-2026-05-29-155100/`

Observed:

- Android Wi-Fi bind still succeeded.
- Multicast lock and Wi-Fi lock were acquired.
- The managed probe could own the test sockets.
- UDP discovery still produced only self-echo packets.
- No remote `PunchPkt` / `P2pReady` arrived.
- No non-self raw UDP packets were captured.
- No image or video artifact was produced.

## Interpretation

The Android phone is a valid test bed for shared C# stream code, but this
camera still does not expose a usable local HTTP, MJPEG, H264, RTSP, or classic
PPPP stream session to the managed probe.

The vendor app proves the camera can stream, and UDP `65529` is an important
native-app clue, but freeing that port did not make the camera respond to the
managed local packets. The likely blocker is not Windows Firewall and not
Android Wi-Fi routing. The missing piece is the Vue990/OKSMART session opener
used by the native PPCS stack.

## Result

No C#-only image or video was downloaded in this phase.

The next useful work is to continue the C# port of the native Vue990 session
packets, especially `TCPSend_TCPRlyReq` and `TCPSend_TCPRSLgn`, then run that
shared C# code from Android first and Windows second.

## Checklist

- [x] Bind Android process to the active camera Wi-Fi network.
- [x] Acquire multicast and Wi-Fi locks.
- [x] Probe local HTTP/CGI media endpoints again.
- [x] Probe expanded local UDP port matrix.
- [x] Test while the vendor app is live.
- [x] Test again after force-stopping the vendor app.
- [x] Save artifacts and reports.
- [x] Confirm no C#-only image/video artifact was produced.
- [ ] Reproduce native Vue990 session-open packets in managed C#.
- [ ] Retrieve image/video through shared C# code.
