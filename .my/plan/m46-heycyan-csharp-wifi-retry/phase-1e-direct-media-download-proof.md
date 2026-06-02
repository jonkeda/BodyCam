# Phase 1e - Direct Media Download Proof

## Goal

Prove that BodyCam can obtain both image and video media from the glasses HTTP
server, not only through the official app's UI/cache.

Phase 1d confirmed `GET /files/media.config`. Phase 1e extends that to actual
`GET /files/{name}` downloads for JPEG and MP4 content while the official app
keeps the P2P window alive.

## Capture Folder

- `captures/phase-1e-20260531-media-artifacts`

## Local Official-App Storage

The official app stores imported media under:

- `/sdcard/Android/data/com.glasssutdio.wear/files/DCIM_1/`

Representative imported files pulled from that folder:

- `media/20260531183944038.jpg`
  - size: `1,931,742` bytes;
  - JPEG signature: `FF D8`;
  - dimensions: `6560x4928`.
- `media/20260531184239920.mp4`
  - size: `10,522,957` bytes;
  - MP4 signature: `ftypisom`.
- `media/first_frame_20260531184239920.jpg`
  - size: `38,834` bytes;
  - dimensions: `480x360`.
- `media/media.config`
  - body: `20260531184722907.mp4`.

## Direct HTTP Download

Fresh media was created from the official app:

- one photo;
- one short video;
- one previously pending video was still listed.

The Album screen then showed:

- `There are 3 new contents available to import to your smartphone.`

During the import P2P window, the phone executed:

- `GET http://192.168.49.183/files/media.config`
- `GET http://192.168.49.183/files/20260531184722907.mp4`
- `GET http://192.168.49.183/files/20260531190723036.jpg`
- `GET http://192.168.49.183/files/20260531190726933.mp4`

Downloaded direct HTTP artifacts:

- `direct-http/downloaded/media.config`
  - size: `66` bytes;
  - body:

```text
20260531184722907.mp4
20260531190723036.jpg
20260531190726933.mp4
```

- `direct-http/downloaded/20260531184722907.mp4`
  - size: `10,743,268` bytes;
  - MP4 signature: `ftypisom`.
- `direct-http/downloaded/20260531190723036.jpg`
  - size: `1,142,361` bytes;
  - JPEG signature: `FF D8`;
  - dimensions: `3280x2464`.
- `direct-http/downloaded/20260531190726933.mp4`
  - size: `4,810,227` bytes;
  - MP4 signature: `ftypisom`.

## HTTP Behavior

- `media.config` may require a short retry after P2P route creation.
- In this capture, the first `media.config` curl timed out after `1s`; the
  second attempt connected.
- `media.config` is newline-delimited and can contain mixed `.jpg` and `.mp4`
  names.
- JPEG responses used `Content-Type: image/jpeg`.
- MP4 responses used `Content-Type: text/plain`, despite valid MP4 `ftypisom`
  file signatures.
- `Access-Control-Allow-Origin: *` was present.
- `GET /files/` directory listing timed out in Phase 1d and should not be used.

## BLE/P2P Behavior

The direct-download import followed the same pattern as prior captures:

- Album `Import` sent a BLE GATT write with length `10`.
- A follow-up BLE GATT write with length `8` occurred before P2P became usable.
- P2P route during download:
  - `192.168.49.0/24 dev p2p-wlan0-0 src 192.168.49.1`.
- The glasses again appeared first around `192.168.49.200`, then settled at
  `192.168.49.183`.

## Conclusion

M46 now has end-to-end oracle proof for:

1. creating media;
2. triggering P2P;
3. reading the glasses media list;
4. downloading a real JPEG;
5. downloading real MP4 videos.

The next implementation step is a C# Android probe that reproduces this without
using the official app at runtime.
