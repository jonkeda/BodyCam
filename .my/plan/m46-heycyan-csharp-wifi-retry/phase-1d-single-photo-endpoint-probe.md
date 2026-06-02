# Phase 1d - Single-Photo Endpoint Probe

## Goal

Create one fresh media item, trigger the official HeyCyan import flow again, and
probe the live WiFi Direct endpoint while the transfer is active.

Phase 1c proved that `Import` forms P2P and gives the glasses IP
`192.168.49.183`, but it did not expose the actual HTTP endpoint or the BLE
payload bytes. This phase turns the transfer into a smaller repeatable case so
we can probe without importing a large backlog.

## Plan

1. Use the official app or glasses button to take one new photo.
2. Open Album and confirm the new-content banner appears again.
3. Start a clean capture window:
   - clear logcat;
   - save before screenshots and UI XML;
   - save `dumpsys wifip2p`, `ip addr`, `ip route`, and socket state.
4. Tap `Import`.
5. While P2P is active, capture:
   - `dumpsys wifip2p`;
   - `ip addr` and `ip route`;
   - socket state from Android shell if available;
   - `dumpsys connectivity`;
   - full logcat.
6. Probe likely glasses endpoints from the phone/network context:
   - `http://192.168.49.183/files/media.config`;
   - `http://192.168.49.183/files/`;
   - common ports such as `80`, `8080`, `8000`, and any port found in socket
     state.
7. Try to capture exact BLE bytes:
   - check whether Bluetooth HCI snoop logging can be enabled and pulled;
   - if not, document that app logcat only reveals write lengths.

## Acceptance

- One fresh-photo import run is captured.
- We either identify the HTTP endpoint/port used by the glasses or prove it is
  not visible with the current shell-level probes.
- We either capture the BLE command bytes or record the exact blocker.
- The protocol map in Phase 2 can start from evidence rather than old command
  assumptions.

## Notes

If the import is too fast to probe manually, repeat with a short video or
recording so the P2P group remains open longer.

## Result

Capture folder:

- `captures/phase-1d-20260531-183703`

Fresh media creation:

- `Take photo` sent one BLE GATT write of length `9` and showed a toast, but
  did not immediately restore the Album import banner.
- A short `Record video` action sent a BLE GATT write of length `9`.
- Returning to Album then showed `There are 1 new contents available to import
  to your smartphone.`

Single-item import timing:

- Pressing `Import` sent a BLE GATT write of length `10`.
- A second BLE GATT write of length `8` occurred while P2P was connecting.
- A final BLE GATT write of length `8` occurred as P2P disconnected.
- Single-item P2P sessions were short:
  - first one-item probe session: about `9.474s`;
  - second one-item probe session: active long enough to fetch
    `/files/media.config`, then disconnected at about `18:49:19`.

WiFi/P2P evidence:

- The phone again became group owner:
  - `groupOwnerIpAddress: 192.168.49.1`;
  - `p2p-wlan0-0`;
  - route `192.168.49.0/24 dev p2p-wlan0-0`;
  - connection type `REINVOKE`;
  - WPS method `PBC`.
- The glasses again appeared as:
  - `M01 Pro_D879B87FE6C9`;
  - device address `60:c2:2a:1a:b6:1b`;
  - interface address `60:c2:2a:1a:36:1b`;
  - initial logcat IP `192.168.49.200`;
  - settled tethering/P2P client IP `192.168.49.183`.
- The second import used channel frequency `2412`.
- The official app/package UID initiated the P2P connect.

HTTP endpoint evidence:

- During the second one-item import, Android shell could reach the glasses on
  port `80`.
- `GET http://192.168.49.183/files/media.config` returned:
  - status: `HTTP/1.1 200 OK`;
  - content type: `text/plain`;
  - content length: `22`;
  - body: `20260531184722907.mp4`.
- `GET http://192.168.49.183/files/` timed out, so directory listing should not
  be treated as available.
- A later probe to `192.168.49.183:80` timed out after the import was already
  closing.
- `192.168.49.200` did not respond during the later probe.
- Android's `nc` build does not support the `-vz` option, so the successful
  `curl` result is the useful port proof.

Conclusion:

The Android official-app import path for this device is:

1. create media over BLE;
2. open Album and press `Import`;
3. send a length-`10` BLE transfer write;
4. form WiFi Direct with the phone as group owner at `192.168.49.1`;
5. wait for the glasses client IP, normally `192.168.49.183`;
6. read `http://192.168.49.183/files/media.config` on port `80`;
7. download listed files from `/files/{name}`;
8. disconnect P2P after the import window.

Remaining blocker:

Logcat still shows only GATT write lengths, not the exact bytes. Phase 2 should
recover or confirm the BLE frame bytes from existing command builders, HCI snoop
if available, or static interoperability analysis.
