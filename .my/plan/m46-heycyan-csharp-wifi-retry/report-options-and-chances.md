# M46 Options And Chances Report

## Executive Read

Yes, it is worth giving HeyCyan WiFi another try, especially on Android. The
best chance is **Android C# using platform WiFi Direct APIs**, not Windows
hidden-network joining and not a pure cross-platform socket-only approach.

Post Phase 1d update: the Android path is now stronger than an estimate. The
official app formed WiFi Direct with the phone as group owner, the glasses joined
as client `192.168.49.183`, and Android shell successfully fetched
`http://192.168.49.183/files/media.config` on port `80`.

Estimated chance of getting a useful C# path:

| Option | Chance | Why |
| --- | ---: | --- |
| Android C# WiFi Direct/P2P | 70-85% | The existing alternative Android app already shows the right OS primitive: `WifiP2pManager`. .NET Android can call the same APIs from C#. |
| Android C# standard hotspot join | 35-55% | Possible if the glasses expose the iOS AP mode, but Android may prefer P2P and the AP may not broadcast consistently. |
| Windows C# hidden AP/profile join | 35-50% | Prior M36 evidence showed partial success and later failures. Windows WiFi Direct and hidden AP behavior are the least predictable pieces. |
| BLE-only media transfer | 5-15% | BLE is too slow and the media path appears intentionally WiFi-backed. |
| Pure cross-platform C# without platform WiFi APIs | <10% | WiFi Direct and network binding require OS-specific APIs. |

Best recommendation:

1. Use the mobile app as an oracle.
2. Build an Android C# WiFi Direct connector.
3. Download media over HTTP.
4. Integrate behind an experimental setting.
5. Revisit Windows only after Android C# is proven.

## What We Already Know

The HeyCyan media path has three layers:

1. BLE control starts transfer mode and reports connection metadata.
2. WiFi transport carries the actual media.
3. HTTP serves `/files/media.config` and file downloads.

Known or likely protocol details:

- `02 01 04` starts transfer mode or requests transfer credentials.
- `02 01 09` exits transfer mode.
- `02 01 0F` may reset P2P state.
- `02 03` may poll WiFi/IP readiness.
- The iOS path uses `openWifiWithMode` and often forces password
  `123456789`.
- The Android path uses `WifiP2pManager`, peer discovery, WPS PBC connect, and
  `WifiP2pInfo.groupOwnerAddress`.

## Main Unknowns

- Does BodyCam's current BLE command sequence exactly match the official mobile
  app sequence?
- Does the official app send any reset, config, or readiness command before
  Android P2P discovery?
- Is the glasses peer address derived from BLE MAC, WiFi MAC, or firmware state?
- Does Android require binding HTTP requests to the P2P network for this device?
- Does the current device firmware expose both P2P and standard AP modes?

## Why M46 Has Better Odds Than The Previous Attempt

M36 mixed several questions at once: Windows, hidden SSIDs, WiFi Direct,
iOS-style AP mode, BLE timing, and media download. M46 narrows the first win to
the environment where the vendor flow already works: Android.

Also, M45/M44 left us with stronger test and settings patterns:

- hardware-gated tests can be opt-in and artifact-heavy;
- provider/command diagnostics patterns can be copied for HeyCyan WiFi;
- settings can expose an experimental path without disturbing the default path.

## Legal And Safety Boundaries

M46 should stay inside interoperability research:

- use only glasses and phones we own/control;
- capture our own BLE/network/log traffic;
- do not bypass account checks, encryption, subscriptions, or access controls;
- do not ship proprietary app code, SDK code, or native libraries as the C#
  implementation;
- use static analysis only to identify command shapes needed for
  interoperability.

## Recommended Path

Start with a one-day oracle pass:

1. Pair/connect glasses using the official app.
2. Capture logs while opening the media/gallery/download flow.
3. Record BLE writes/notifies if possible.
4. Record P2P peer names, device addresses, group owner IP, and timing.
5. Save the observed sequence in an RCA-style report.

Then build the smallest possible C# probe:

1. Send the known BLE transfer command.
2. Start C# `WifiP2pManager` discovery.
3. Select the glasses peer.
4. Connect with WPS PBC.
5. Fetch `/files/media.config`.
6. Download one JPEG.

Only after that should we wire it into `HeyCyanCameraProvider`.

## Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Android permission changes block P2P | High | Add explicit runtime checks for location, nearby devices, WiFi state, and location services. |
| Official app uses hidden SDK/native behavior | Medium | Use logs and captures to prove whether platform APIs alone are enough. |
| Firmware variant changes command bytes | Medium | Keep protocol map versioned by model/firmware. |
| P2P connects but HTTP routes through another network | Medium | Bind process/socket to P2P network and probe multiple candidate IPs. |
| Vendor app leaves stale P2P groups | Medium | Add reset/cleanup phase before each test. |
| Media server starts slowly | Low/Medium | Poll readiness with bounded backoff before failing. |

## Recommendation

Proceed with M46. The chance is good enough because Android C# can use the same
class of WiFi APIs as the working mobile path. The effort should be capped by
clear stop criteria: if the official app relies on proprietary signed commands
or privileged OS access, we document that and keep the current SDK/fallback path.
