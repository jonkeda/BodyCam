# Phase 4 - Media Download And Camera Provider Path

## Goal

Use the connected WiFi/P2P transport to list media and download a frame through
C#.

## Work

- Bind HTTP traffic to the P2P network if Android routes incorrectly.
- Probe candidate IPs:
  - confirmed P2P client IP from Phase 3;
  - tethering/P2P client list IP;
  - BLE-reported IP if available.
- Do not treat `WifiP2pInfo.GroupOwnerAddress` as the media host on Android
  when BodyCam is group owner. In the official-app captures it was the phone at
  `192.168.49.1`.
- Fetch `http://{ip}/files/media.config`.
- Treat `media.config` as newline-delimited filenames.
- Parse media entries robustly:
  - JPEG/JPG photos;
  - MP4 videos;
  - OPUS voice notes.
- Download files with `GET http://{ip}/files/{name}`.
- Validate downloads by extension and file signature because MP4 responses may
  report `Content-Type: text/plain`.
- Download the newest JPEG and validate magic bytes.
- Connect the result to `HeyCyanMediaTransfer`.
- Prove `HeyCyanCameraProvider.CaptureFrameAsync()` returns the downloaded
  JPEG.

## Acceptance

- C# downloads `/files/media.config`.
- C# downloads one valid JPEG.
- C# downloads one valid MP4 when `media.config` lists one.
- The camera provider can return that JPEG from the C# path.
