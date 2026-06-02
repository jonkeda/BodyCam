# Phase 3 - Android C# WiFi Direct Connector

## Goal

Build the C# Android WiFi Direct connector that can discover and connect to the
glasses without Java/Kotlin production code.

## Work

- Add an Android-only C# connector around:
  - `WifiP2pManager`;
  - `WifiP2pManager.Channel`;
  - P2P broadcast receiver;
  - `WifiP2pConfig`;
  - `WpsInfo.Pbc`;
  - `WifiP2pInfo`;
  - P2P group/client-list APIs.
- Add permission and state checks:
  - WiFi enabled;
  - location services enabled where required;
  - nearby devices permissions on newer Android;
  - multicast/WiFi locks if needed.
- Add peer matching:
  - device name prefix;
  - BLE MAC suffix;
  - known `M01 Pro` naming patterns;
  - fallback manual selection in diagnostics.
- Add media-host resolution:
  - treat `WifiP2pInfo.GroupOwnerAddress` as the phone's IP when BodyCam is
    group owner;
  - prefer the P2P client list/tethering client state for the glasses IP;
  - validate the candidate with `GET /files/media.config`.
- Add cleanup:
  - cancel connect;
  - remove group;
  - stop peer discovery.
- Add bounded retries and telemetry-style diagnostics.

## Acceptance

- Hardware-gated probe can connect to the glasses P2P group.
- Probe records the phone group-owner IP and the glasses client IP separately.
- Probe confirms `http://{glassesClientIp}/files/media.config` on port `80`.
- Probe can clean up and reconnect on a second run.

## Stop Criteria

- Android denies P2P connection despite correct permissions and official app
  success on the same device.
- P2P requires private/vendor APIs not available to normal apps.
