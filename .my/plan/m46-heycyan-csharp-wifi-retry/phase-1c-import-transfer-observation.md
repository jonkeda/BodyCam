# Phase 1c - Import Transfer Observation

## Goal

Press the official HeyCyan Album `Import` action and capture the transfer path.

Phase 1b reached the logged-in Album screen and showed:

- connected glasses: `M01 Pro_E6C9`;
- `64` new contents available to import;
- visible local thumbnails;
- no active WiFi P2P group after only opening Album.

The next evidence step is to press `Import` and observe whether the official app
switches the glasses into transfer mode, forms a WiFi/P2P path, downloads media,
or uses a cached/non-P2P route.

## Plan

1. Save the pre-import Album UI state.
2. Clear logcat.
3. Tap the `Import` button.
4. Capture the screen, UI XML, logs, network interfaces, routes, connectivity,
   and WiFi/P2P service state at short intervals.
5. Look specifically for:
   - GATT writes and notifications around the button tap;
   - WiFi Direct/P2P state transitions;
   - changes to `p2p0`, `wlan0`, route table, or bound network;
   - group owner IP;
   - HTTP calls, media filenames, or Android media scanner insertions;
   - any progress/error dialog.
6. If transfer starts and is long-running, capture enough evidence, then avoid
   unnecessary repeated downloads.

## Acceptance

- A clean import logcat window is saved.
- Before/after screenshots show the UI state and any progress/error.
- Network/P2P state is captured during and after the import action.
- The high-level log records whether Import triggers BLE-only, WiFi P2P, normal
  WiFi, or a failed/blocked path.

## Notes

This phase may copy media from the glasses to the phone. It should not delete
content from the glasses.

## Result

Capture folder:

- `captures/phase-1c-20260531-182012`

User/app action:

- The Album screen showed `64` new items and an `Import` button.
- ADB tapped the `Import` button at bounds `[747,405][993,501]`.

UI evidence:

- At about `5s`, the official app showed `Connecting to device WiFi.` and the
  button changed to `Importing...`.
- At about `20s`, the app showed `There are 22/64 new contents available to
  import to your smartphone.` and a speed of `38.2 KB/s`.
- At about `60s`, the import banner was gone and the album list was visible
  again.

BLE evidence:

- Immediately after tapping `Import`, Android logged one BLE GATT write with
  length `10` to the glasses.
- The P2P setup started immediately after that write.
- Logcat did not expose the GATT payload bytes, only length and success status.

WiFi/P2P evidence:

- `Import` formed an Android WiFi Direct group.
- The phone became group owner:
  - interface: `p2p-wlan0-0`;
  - phone/group owner IP: `192.168.49.1/24`;
  - route: `192.168.49.0/24 dev p2p-wlan0-0`;
  - group: `DIRECT-Vr-daan's S25`;
  - frequency: `2437`;
  - connection type: `REINVOKE`;
  - WPS method: `PBC`.
- The glasses joined as client:
  - device: `M01 Pro_D879B87FE6C9`;
  - IP: `192.168.49.183`;
  - device address: `60:c2:2a:1a:b6:1b`;
  - interface address: `60:c2:2a:1a:36:1b`.
- Logcat also briefly reported the same client at `192.168.49.200` before
  Android tethering updated the client IP to `192.168.49.183`.
- The WiFi Direct session lasted about `49.8s`, then disconnected.
- Normal WiFi stayed connected at the same time:
  - SSID: `jobaboe`;
  - IP: `192.168.1.67`;
  - primary interface: `wlan0`.

Media evidence:

- Android media scanner events fired while the P2P session was active, which is
  consistent with imported media being written to the phone.
- The log did not reveal direct HeyCyan HTTP request URLs, `GET` paths, or
  obvious OkHttp traces.
- Samsung system file-share/UPnP services also reacted to the P2P group, but
  those logs look like platform side effects rather than proof of the HeyCyan
  transfer protocol.

Conclusion:

The official app's actual transfer path is:

1. press Album `Import`;
2. send a BLE write of length `10`;
3. form Android WiFi Direct with the phone as group owner;
4. glasses join as `192.168.49.183`;
5. media imports over the P2P network;
6. P2P disconnects after the import window.

Important correction:

Do not use `WifiP2pInfo.groupOwnerAddress` as the glasses IP for this Android
flow. It is the phone's group-owner IP (`192.168.49.1`). The glasses IP must
come from the P2P client list, tethering client state, or direct endpoint probe.

The next phase should make this repeatable with a single new photo and probe the
active P2P endpoint while the group is alive.
