# Vue990 C# Capture Solution

**Status:** Working for the current `@MC-0025644` / `BK7252N` camera

## What This Solves

The repo now has a C# runtime path that downloads both:

- a still JPEG image: `managed-direct-still.jpg`
- a short MJPEG AVI video: `managed-direct-video-mjpeg.avi`

The working Windows path does not require the Android phone or the vendor
Vue990 app during capture. The laptop must be connected to the camera Wi-Fi so
it can reach the camera at `192.168.168.1`.

## Supported Scope

Confirmed device:

- SSID / tag: `@MC-0025644`
- Camera host: `192.168.168.1`
- Camera alias: `BK7252N`
- VUID / real device id: `BK0025644WBPD`
- Device id: `BKGD00000100FMQLN`

The runtime transport is C#. The remaining compatibility caveat is narrow but
important: four encrypted post-hole control payloads are still scoped
native-observed vectors, centralized in `A9Vue990PostHoleControlProvider`. They
are sent by C# and work repeatably for this camera, but they are not yet
generated from first principles in C# for unknown Vue990/BK7252N variants.

## How To Run On Windows

1. Connect the laptop Wi-Fi to `@MC-0025644`.
2. Confirm the camera is reachable:

```powershell
dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-status --host 192.168.168.1
```

3. Capture a still image and MJPEG AVI:

```powershell
dotnet run --project tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj -- vue990-direct-capture --host 192.168.168.1 --output-dir .my\plan\m38-a9-camera\captures\vue990-direct-capture-latest --stream-seconds 18 --max-frames 12 --json --output .my\plan\m38-a9-camera\captures\vue990-direct-capture-latest\result.json
```

Expected successful outputs in the output directory:

- `managed-direct-still.jpg`
- `managed-direct-video-mjpeg.avi`
- `managed-hlp2p-direct-channel.bin`
- `hlp2p-direct-*.bin` diagnostic packet samples
- `result.json` if `--json --output ...` was used

## Solution Flow

The working path is implemented by `A9Vue990DirectCaptureClient`.

High-level sequence:

1. Open a UDP socket on Windows.
2. Optionally fetch `http://192.168.168.1:81/get_status.cgi` for diagnostics.
3. Send the legacy discovery preambles on UDP `65529`.
4. Send compact LAN-hole probes to broadcast, `192.168.168.255`, and
   `192.168.168.1` on UDP `65530` and `65531`.
5. Parse the compact `0x11` LAN-hole response from the camera.
6. Send the compact `0x11` LAN-hole ACK.
7. Wait for compact `0x15` ready.
8. Complete the compact alive exchange with `0B` / `0C` packets.
9. Send the scoped post-hole controls in native-paced order:
   `initial-short-request`, `initial-long-request`, `media-short-request`,
   `media-long-request`, repeat `initial-long-request`, then repeat
   `media-long-request` after the large command response.
10. Parse direct `0D` packets from the camera and ACK each data packet with a
    C#-built direct ACK.
11. Accumulate channel payload bytes once the `55 AA 15 A8` media marker is
    seen.
12. Extract JPEG frames and save the first frame plus an MJPEG AVI.

## Code Map

- `tools/BodyCam.A9Probe/Program.cs`
  - CLI command: `vue990-direct-capture`
  - Parses host, output directory, stream duration, frame limit, image/video
    flags, and JSON output.
- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990DirectCaptureClient.cs`
  - Main Windows C# direct capture implementation.
  - Owns socket setup, LAN-hole flow, direct ACKs, frame saving, and AVI
    writing.
- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990Hlp2pDirectPacket.cs`
  - Compact LAN-hole packet builders/parsers.
  - Direct `0D` data packet parser and ACK builder.
- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990PostHoleControlProvider.cs`
  - Names and scopes the four post-hole control vectors shared by Windows and
    Android.
- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990ChannelMediaExtractor.cs`
  - Extracts JPEG frames from channel bytes.
- `src/BodyCam/Services/Camera/A9/Vue990/A9Vue990PpcsPacket.cs`
  - Contains `A9Vue990VideoFrameAssembler`, which reassembles Vue990 video
    chunks before JPEG extraction.
- `src/BodyCam/Services/Camera/A9/Vue990/A9MjpegAviWriter.cs`
  - Writes extracted JPEG frames as an MJPEG AVI.
- `tools/BodyCam.A9PhoneProbe/ManagedDirectMediaProbe.cs`
  - Android proof harness for the same managed direct sequence.

## Regression Tests

Useful focused checks:

```powershell
dotnet build tools\BodyCam.A9Probe\BodyCam.A9Probe.csproj
dotnet build tools\BodyCam.A9PhoneProbe\BodyCam.A9PhoneProbe.csproj
dotnet test src\BodyCam.Tests\BodyCam.Tests.csproj --filter "Hlp2pDirect|PostHole"
```

Relevant tests:

- `A9Vue990Hlp2pDirectPacketTests`
- `A9Vue990PostHoleControlProviderTests`
- `A9Vue990ChannelMediaExtractorTests`
- `A9Vue990PpcsPacketTests`

## Proof Artifacts

Latest repeatability proof:

- Run 1:
  `.my/plan/m38-a9-camera/captures/phase-49-final-windows-direct-2026-05-30-010401/`
  saved `managed-direct-still.jpg`, `640x480`, `8104` bytes, SHA-256
  `F36EF09D8BBFA5A8330D9BE54F46158E9AAB4B2C37E13F9CB632F39B632A498D`,
  and `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `97868`
  bytes, SHA-256
  `3E1EA8F16061840F039422C6C38C5F31F4F5179C020FC87BD5CAE97FFF83E80A`.
- Run 2:
  `.my/plan/m38-a9-camera/captures/phase-49-final-windows-direct-2026-05-30-010441/`
  saved `managed-direct-still.jpg`, `640x480`, `8152` bytes, SHA-256
  `5DF8B1778937805BE84EAA86ED6CC9802CE64209908F1AC36AD9BDFD848F5516`,
  and `managed-direct-video-mjpeg.avi`, `12` frames, `640x480`, `98312`
  bytes, SHA-256
  `0D2825A46C5C8AA6D93FFBB60936A740A0D638C21C7E399D9B1C5435ED8D6BA2`.

These two runs used different camera-side UDP ports and LAN-hole status values,
so the solution is repeatable for the current camera session shape.

## Troubleshooting

- If `vue990-status` cannot reach `192.168.168.1`, reconnect the laptop to the
  camera Wi-Fi and confirm the camera is powered.
- If LAN-hole receives no camera response, close the Vue990 mobile app so it is
  not owning the session, then power-cycle the camera.
- If frames are received but the video is not saved, inspect
  `managed-hlp2p-direct-channel.bin` and the per-frame JPEG files in the output
  directory.
- Windows Firewall is not the current known blocker. The Phase 48 and Phase 49
  runs proved Windows can send, receive, ACK, and save media on this laptop.

## What Not To Repeat

Do not restart broad HTTP URL scans, RTSP probing, generic UDP port matrices, or
TCP relay retries unless the firmware or network evidence changes. Those paths
were already tested and did not produce media. The working path is the compact
Vue990/HLP2P direct transport described above.

## Future Work

Only create a new phase if broader compatibility is needed. The next meaningful
phase would derive the four post-hole controls in C# or prove the scoped vectors
against another Vue990/BK7252N camera or firmware.
