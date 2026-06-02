# Phase 2 - BLE And WiFi Protocol Map

## Goal

Convert the oracle capture into a small protocol map that C# can implement and
unit-test.

## Work

- Compare captured BLE writes with existing `HeyCyanCommands`.
- Confirm command frame action, CRC, payload, and notification shape.
- Map transfer-related commands:
  - enter/open WiFi transfer;
  - reset P2P;
  - poll WiFi/IP readiness;
  - exit transfer;
  - take photo if needed before listing media.
- Map notification payloads:
  - ACK/status;
  - SSID/password;
  - IP address;
  - error codes.
- Write protocol notes with byte offsets.
- Add unit tests for builders/parsers before using the commands in hardware
  tests.

## Confirmed Oracle Facts

- Official app package: `com.glasssutdio.wear`.
- Connected glasses display name: `M01 Pro_E6C9`.
- P2P peer/client name: `M01 Pro_D879B87FE6C9`.
- Android transfer starts from Album `Import`, not merely opening Album.
- Take-photo/video creation actions observed so far use a BLE write of length
  `9`.
- Transfer/import starts with a BLE write of length `10`.
- Additional transfer/cleanup writes of length `8` occur around P2P connect and
  disconnect.
- Android P2P uses the phone as group owner:
  - phone/group-owner IP: `192.168.49.1`;
  - glasses client IP observed: `192.168.49.183`;
  - transient client IP observed in logcat: `192.168.49.200`;
  - route: `192.168.49.0/24 dev p2p-wlan0-0`.
- Do not use `WifiP2pInfo.groupOwnerAddress` as the media host. It is the phone
  in this flow.
- Confirmed media listing endpoint:
  - `GET http://192.168.49.183/files/media.config`;
  - port `80`;
  - response `HTTP/1.1 200 OK`;
  - body example `20260531184722907.mp4`.
- Confirmed direct downloads:
  - `GET /files/20260531190723036.jpg` returns a valid JPEG;
  - `GET /files/20260531184722907.mp4` returns a valid MP4;
  - `GET /files/20260531190726933.mp4` returns a valid MP4.
- `media.config` can list mixed image and video filenames, one per line.
- MP4 responses can advertise `Content-Type: text/plain`; validate by filename
  and file signature.
- Directory listing at `/files/` timed out.

## Acceptance

- Protocol map explains all bytes needed for the first C# probe.
- Parser tests cover known captured notifications.
- Existing commands are either confirmed or corrected.

## Open Questions

- Is `02 01 04` the actual Android transfer opener or only one command in a
  longer sequence?
- Is `02 03` active IP polling or an iOS-only command?
- Does Android need a reset before each transfer attempt?
- Which Android API path gives BodyCam the glasses client IP most reliably:
  `WifiP2pGroup.clientList`, tethering client state, ARP/neighbor table, or an
  HTTP probe after route creation?
