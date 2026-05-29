# Phase 35 - Android Managed C# Stream Attempt

**Status:** Completed - Android C# stream code added, camera gives no local stream session

## Goal

Run the same managed C# stream code on Android that we would later run on
Windows: open the local camera path, perform the classic PPPP session steps,
send `ConnectUser` / `StartVideo`, and save image/video frames if any arrive.

## Implemented

- Added shared C# control-channel builder:
  - `A9Vue990PpcsControlCommandBuilder`
  - `ConnectUser`
  - `VideoResolution`
  - `StartVideo`
  - `StopVideo`
  - `DeviceStatus`
- Corrected `DrwAck` framing to use the `0xd2` ACK marker.
- Added Android managed classic PPPP stream attempt in
  `ManagedDirectMediaProbe`.
- Linked the new shared C# control builder into the Android probe APK.

## Live Run

Artifacts:

- `.my/plan/m38-a9-camera/captures/phase-35-android-managed-stream-2026-05-29-1530.json`
- report:
  `.my/plan/m38-a9-camera/captures/phase-33-android-managed-direct-2026-05-29-152634/a9-android-managed-direct-2026-05-29-152634.txt`

Observed:

- Android was on camera Wi-Fi as `192.168.168.101/24`.
- Camera status on `192.168.168.1:81` still worked.
- Direct HTTP/CGI still returned no JPEG/MJPEG/H264 media.
- UDP discovery still produced only self-echo packets on the phone.
- The managed C# classic PPPP stream attempt sent:
  - plain `LanSearch` `F1300000`
  - XOR `LanSearch` `2CBA5F5D`
- The camera did not return a remote `PunchPkt` or `P2pReady`.
- Because there was no session response, C# could not send `ConnectUser` /
  `StartVideo` to the camera.
- No managed image/video artifact was produced.

## Interpretation

Android is a valid place to run the future shared C# stream library, but this
specific Vue990/BK7252N unit does not expose the classic local PPPP stream
session. The working Vue990 app is therefore not using the local LAN path we
can start with `LanSearch`; it is using the Vue990/OKSMART relay/session path.

## Result

The Android C# stream code now exists and is tested at the packet/control layer,
but it cannot retrieve image/video from this camera until the Vue990 relay
second-stage request is reproduced in C#.
