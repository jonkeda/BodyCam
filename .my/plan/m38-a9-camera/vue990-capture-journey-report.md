# Vue990 Capture Journey Report

**Date:** 2026-05-30  
**Camera:** `@MC-0025644` / `BK7252N`  
**Final outcome:** Windows C# can download a real still image and MJPEG AVI
video from the camera.

## Executive Summary

We started with a camera that exposed a locked Wi-Fi network, blinked a blue
mode light, and did not respond to normal camera-stream assumptions. After a
long sequence of probing, Android experiments, native behavior mapping, and C#
transport reconstruction, we reached the practical goal:

- C# can connect directly from Windows to the camera over Wi-Fi.
- C# can complete the compact Vue990/HLP2P direct session flow.
- C# can receive media packets, extract JPEG frames, save a still image, and
  assemble an MJPEG AVI.
- The Android phone is no longer required for the normal Windows capture path.
- Runtime capture no longer depends on native Vue990/PPCS session calls.

The main remaining caveat is compatibility breadth: four encrypted setup
packets are stored as known-good scoped vectors captured from the vendor app.
They work for this camera, but they are not yet dynamically generated for every
possible Vue990/BK7252N variant.

## Final Proof

Latest repeatability proof came from Phase 49.

Run 1:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-49-final-windows-direct-2026-05-30-010401/`
- Still image:
  `managed-direct-still.jpg`, `640x480`, `8104` bytes, SHA-256
  `F36EF09D8BBFA5A8330D9BE54F46158E9AAB4B2C37E13F9CB632F39B632A498D`
- Video:
  `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `97868` bytes,
  SHA-256
  `3E1EA8F16061840F039422C6C38C5F31F4F5179C020FC87BD5CAE97FFF83E80A`

Run 2:

- Directory:
  `.my/plan/m38-a9-camera/captures/phase-49-final-windows-direct-2026-05-30-010441/`
- Still image:
  `managed-direct-still.jpg`, `640x480`, `8152` bytes, SHA-256
  `5DF8B1778937805BE84EAA86ED6CC9802CE64209908F1AC36AD9BDFD848F5516`
- Video:
  `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `98312` bytes,
  SHA-256
  `0D2825A46C5C8AA6D93FFBB60936A740A0D638C21C7E399D9B1C5435ED8D6BA2`

Both runs succeeded with different camera-side UDP ports and LAN-hole status
values. That was the repeatability check that turned the result from "lucky
capture" into "working path for this camera."

## Starting Conditions

The first reliable observations were small but important:

- The camera broadcast a Wi-Fi network matching the camera tag:
  `@MC-0025644`.
- The mode light blinked blue for a while, then stopped.
- The camera was reachable as `192.168.168.1` after joining its Wi-Fi.
- `get_status.cgi` answered on HTTP port `81`.
- Status reported:
  - alias: `BK7252N`
  - VUID / real device id: `BK0025644WBPD`
  - device id: `BKGD00000100FMQLN`
- Credentials used successfully were `admin` / `888888`.

The good news: the device was alive and identifiable.

The bad news: none of the usual media paths worked.

## What Did Not Work

These were important failures because they narrowed the search:

- Direct HTTP media URL guesses did not produce a stream.
- RTSP probing did not produce a stream.
- Classic MJPEG endpoint probing did not produce a stream.
- Generic UDP port matrices mostly produced silence.
- Early HLP2P/PPPP-style packet attempts did not open a managed session.
- TCP relay attempts using decoded DAS relay hosts did not return useful bytes.
- Phone socket snapshots did not reveal a simple media socket that could be
  copied directly.

This was the first big down: the camera was not a normal IP camera with a
hidden-but-simple stream URL.

## Turning Point 1: The Vendor App Proved The Stream

The Vue990 Android app connected to the camera and showed a live image.

That changed the investigation. The question stopped being "does the camera
stream?" and became "what exact protocol sequence does the app use before the
camera starts streaming?"

This was a major up:

- The camera hardware, Wi-Fi, credentials, and stream path were proven good.
- The issue was protocol reconstruction, not dead hardware.

But it also introduced the next hard part:

- The app used native Vue990/PPCS libraries.
- The useful behavior was hidden behind native session code.
- We needed the native app as an oracle without depending on it forever.

## Turning Point 2: Media Bytes Were JPEG Frames

The native-backed channel oracle exposed the media payload shape.

The key marker was:

```text
55 AA 15 A8
```

Once that marker appeared, the payload contained JPEG frames. That meant the
decode problem was much simpler than feared:

- No H.264 decoder was needed.
- No FFmpeg path was needed.
- C# could extract JPEG frames directly.
- C# could assemble those JPEG frames into an MJPEG AVI.

This was one of the biggest ups in the whole project. It changed the problem
from "unknown video codec in unknown tunnel" to "get channel bytes, then parse
JPEGs."

## Turning Point 3: The Live Command Was Rebuilt In C#

The next discovery was how the app requested live video after the session was
open.

It did not send plain HTTP directly to the camera. It wrote a command-channel
message:

```text
Header:
01 0A 00 00 61 00 00 00

Body:
GET /livestream.cgi?streamid=10&substream=0&loginuse=admin&loginpas=888888&user=admin&pwd=888888&
```

C# rebuilt that command successfully. At that stage, native code still carried
the session, but C# could now send the live-open command and process the media
that came back.

This was the first point where C# owned meaningful protocol behavior beyond
simple probing.

## The Hardest Part: Replacing The Session Transport

The remaining wall was the session carrier.

The native app used a compact Vue990/HLP2P direct path:

1. LAN-hole probe.
2. LAN-hole response.
3. LAN-hole ACK.
4. Ready packet.
5. Alive probe / ACK.
6. Direct `0D` command and data packets.
7. Media packets containing the `55 AA 15 A8` channel stream.

Early C# attempts got close but failed to open media. This was frustrating
because the camera would sometimes answer enough to prove we were near the
right door, then stop before sending the stream.

The missing detail was pacing and order.

The working native-paced order was:

1. Send `initial-short-request`.
2. Send `initial-long-request`.
3. Wait.
4. Send `media-short-request`.
5. Send `media-long-request`.
6. Wait.
7. Repeat `initial-long-request`.
8. ACK the large command response.
9. Repeat `media-long-request`.

Once Android C# followed that order, the camera produced the missing command
response, then the media header, then JPEG fragments.

That was the Phase 47 breakthrough:

- Android C# runtime capture saved a real still image.
- Android C# runtime capture saved a real MJPEG AVI.
- No native Vue990/PPCS session calls were needed in that runtime path.

## Windows Port

After Android proved the managed direct sequence, the next test was Windows.

The laptop joined `@MC-0025644` directly and reached the camera at
`192.168.168.1`.

There was a reasonable suspicion that Windows Firewall might block some UDP
traffic, but the successful run showed it was not the blocker:

- Windows sent the compact LAN-hole sequence.
- The camera answered.
- Windows sent ACKs.
- Windows received direct `0D` media packets.
- Windows saved channel bytes.
- Windows extracted JPEG frames.
- Windows wrote the still image and MJPEG AVI.

The first Windows run got raw channel bytes but did not extract frames during
the live receive loop. The fix was to add fallback extraction from the saved
channel dump. The next run produced the image and video artifacts.

That was Phase 48:

- Windows C# direct capture worked.
- The Android phone was no longer required for normal capture.

## Final Hardening

Phase 49 cleaned up the result so it could be treated as an implementation
rather than a one-off experiment.

Changes made:

- Added `A9Vue990PostHoleControlProvider`.
- Named the four scoped post-hole controls.
- Shared the provider between Windows direct capture and the Android probe.
- Removed normal report wording that called them "replay controls."
- Added tests for control order, lengths, packet fields, and defensive byte
  copies.
- Re-ran Windows capture twice as proof.
- Updated the roadmap, status report, phase docs, and realtests log.

This turned the project state from "we made it work once" into "we have a
repeatable current-camera solution with a documented compatibility caveat."

## Current Code Surface

Primary Windows command:

```powershell
dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-direct-capture --host 192.168.168.1 --output-dir .my\plan\m38-a9-camera\captures\vue990-direct-capture-latest --stream-seconds 18 --max-frames 12 --json --output .my\plan\m38-a9-camera\captures\vue990-direct-capture-latest\result.json
```

Main implementation files:

- `tools/BodyCam.A9Probe/Program.cs`
- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990DirectCaptureClient.cs`
- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990Hlp2pDirectPacket.cs`
- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990PostHoleControlProvider.cs`
- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990ChannelMediaExtractor.cs`
- `src/BodyCam/Services/Camera/A9/Vue990/A9MjpegAviWriter.cs`

Useful validation:

```powershell
dotnet build tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj
dotnet build tools\BodyCam.A9PhoneProbe\BodyCam.A9PhoneProbe.csproj
dotnet test src\BodyCam.Tests\BodyCam.Tests.csproj --filter "Hlp2pDirect|PostHole"
```

## Ups And Downs

Ups:

- The camera reliably exposed status on `192.168.168.1:81`.
- The Vue990 app proved the camera could stream.
- Native-backed capture revealed JPEG media rather than a harder codec problem.
- C# rebuilt the live-open command.
- Android C# proved the managed direct session.
- Windows C# proved direct image/video capture.
- Phase 49 proved repeatability with two fresh Windows runs.

Downs:

- Normal HTTP, RTSP, and MJPEG guesses went nowhere.
- Broad UDP probing created a lot of negative evidence.
- Relay work consumed time without producing stream bytes.
- Android permission and app-crash issues slowed early probing.
- The post-hole controls were sensitive to order and timing.
- The final four encrypted control payloads are still scoped vectors rather
  than fully derived dynamic C# payloads.

## What We Should Not Repeat

Do not spend time on these again unless new firmware or packet evidence changes
the situation:

- Broad HTTP endpoint scans.
- RTSP probing.
- Generic UDP port matrices.
- TCP relay retries as the primary path.
- Phone socket snapshots as the main strategy.
- H.264/FFmpeg work before H.264 bytes are actually observed.

## Remaining Caveat

The runtime capture path is C#, but the four post-hole controls are stored as
known-good compatibility vectors:

- `initial-short-request`
- `initial-long-request`
- `media-short-request`
- `media-long-request`

That means:

- Good: no native Vue990/PPCS session library is needed at runtime for this
  camera.
- Good: Windows C# can download image and video directly.
- Caveat: broader support for other Vue990/BK7252N cameras may require deriving
  those encrypted controls dynamically.

## Recommended Next Step

Treat the current camera goal as complete.

Only create a new phase if one of these becomes necessary:

- support a second Vue990/BK7252N camera;
- support a firmware variant where the scoped vectors fail;
- derive the post-hole control encryption/key schedule in C#;
- package the direct capture path into a cleaner public API.
