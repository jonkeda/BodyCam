# M46 - HeyCyan C# WiFi Retry

**Status:** C# Android BLE and media path achieved; Windows media route blocked on current adapter  
**Goal:** Give the HeyCyan WiFi/media path another serious attempt, M38-style:
use the official mobile app as an oracle, recover the BLE-to-WiFi sequence, and
build a C#-only BodyCam path that connects the phone to the glasses WiFi
transport and downloads media.

## Why

The current HeyCyan integration works well enough for BLE/control/audio pieces,
but the WiFi photo/media transfer has been fragile across Android, Windows, and
firmware paths. M36 found strong evidence that the glasses expose media over
HTTP after a BLE-triggered WiFi mode, but the exact startup/connection sequence
differs between Android WiFi Direct and iOS-style hotspot behavior.

M46 reopens the problem with the successful M38 pattern:

- use the vendor/official app as a packet and log oracle;
- capture repeatable evidence before changing production code;
- create small C# probes first;
- promote only proven steps into BodyCam;
- keep real-hardware tests gated and reproducible.

## Scope

In scope:

- Android phone as the first target.
- C#/.NET MAUI implementation for BodyCam production code.
- Android platform APIs called from C# bindings, especially
  `Android.Net.Wifi.P2p.WifiP2pManager`.
- BLE command framing and notification parsing in C#.
- HTTP media listing and download from `/files/media.config` and `/files/*`.
- Real-device probe captures, logs, and reports.

Out of scope:

- Shipping the HeyCyan official app, SDK, or copied proprietary code.
- Bypassing app accounts, DRM, encryption, or access controls.
- Decompiling or instrumenting third-party binaries beyond what is needed for
  interoperability research and only where locally permitted.
- A Windows-first solution. Windows can be revisited after Android C# works.

## Current Evidence To Start From

- Android path uses WiFi Direct/P2P and `WifiP2pManager`.
- Official HeyCyan `Import` forms P2P with the phone as group owner at
  `192.168.49.1`.
- The glasses join as a P2P client, observed at `192.168.49.183`.
- iOS path uses a standard WiFi hotspot flow through
  `openWifiWithMode:QCOperatorDeviceModeTransfer`.
- Known BLE transfer command candidate: `02 01 04`.
- Known reset P2P candidate: `02 01 0F`.
- Known WiFi readiness/IP candidate from prior research: `02 03`.
- Confirmed Android media endpoint:
  `GET http://{glassesClientIp}/files/media.config` on port `80`, then likely
  `GET /files/{name}`.
- Example confirmed media listing body: `20260531184722907.mp4`.
- Known password fallback in iOS samples: `123456789`.
- Known SSID shape: `M01 Pro_<MAC-without-colons>`.
- Prior RCAs disagree on whether Windows hidden-AP joining is enough, so Android
  C# should be treated as the primary chance to win.

## Latest Result

On 2026-06-01, Phase 7d closed the current native Windows media-transfer
attempt on the Intel BE200 adapter:

- Windows BLE/control works.
- Fresh photo and video commands work.
- Windows discovers the HeyCyan WiFi Direct peer.
- WinRT WiFi Direct pairing/connection does not create endpoint pairs.
- WLAN fallback fails with `The specific network is not available` and
  `RSSI: 255`.
- Archived M36 Wi-Fi Framework results hit the same boundary, so the next
  practical Windows test is a different WiFi adapter, an unmanaged laptop, or an
  Android bridge.

Artifacts:

- `captures/phase-7c-windows-route-probe/20260601-105348/`

On 2026-05-31, Phase 8 removed the Android vendor AAR bridge and the
real-hardware xUnit harness passed against `M01 Pro_E6C9` through direct C# BLE
plus C# Wi-Fi/media transfer:

- scanned, connected, and sent BLE commands through Android GATT from C#;
- launched the installed Android BodyCam app through `adb`;
- captured a fresh photo;
- recorded and stopped a short video;
- listed `10` media entries over the C# transfer path;
- downloaded `20260531234943012.jpg`, `371,145` bytes, valid JPEG;
- downloaded `20260531234947896.mp4`, `4,816,656` bytes, valid MP4.

Artifacts:

- `captures/phase-5-real-hardware-test-harness/20260531-234952/`

Earlier on 2026-05-31, the Android C# probe successfully:

- captured a fresh photo;
- recorded and stopped a short video;
- entered HeyCyan media transfer mode;
- formed Wi-Fi Direct with `M01 Pro_D879B87FE6C9`;
- fetched `/files/media.config`;
- downloaded a valid JPEG and MP4 from the glasses.

Artifacts:

- `captures/phase-4f-20260531-bodycam-csharp-fresh-media-success/20260531-223525/`

## Definition Of C# Only

For the current M46 result, "C# only" means the Android BLE/control,
Wi-Fi/media implementation, and probe harness are C#:

- no Java/Kotlin production service;
- no Android vendor AAR in the BodyCam app production path;
- no official HeyCyan app dependency at runtime;
- platform calls through .NET Android bindings are allowed.

Observation tools such as `adb logcat`, packet captures, or a locally installed
official app are allowed during research, but they are not part of the shipped
solution.

## Architecture Target

```text
HeyCyanGlassesSession
  -> C# BLE command sender
  -> C# transfer-mode state machine
  -> Android C# WiFi P2P connector
  -> C# HTTP media client
  -> HeyCyanCameraProvider / HeyCyanMediaTransfer
```

## Phases

Roadmap: [M46 Roadmap](./roadmap.md)

- [Phase 1 - Mobile App Oracle Capture](./phase-1-mobile-app-oracle-capture.md)
- [Phase 1a - First Android Oracle Shot](./phase-1a-first-android-oracle-shot.md)
- [Phase 1b - Logged-In Location-On Oracle Run](./phase-1b-logged-in-location-on-oracle-run.md)
- [Phase 1c - Import Transfer Observation](./phase-1c-import-transfer-observation.md)
- [Phase 1d - Single-Photo Endpoint Probe](./phase-1d-single-photo-endpoint-probe.md)
- [Phase 1e - Direct Media Download Proof](./phase-1e-direct-media-download-proof.md)
- [Phase 2 - BLE And WiFi Protocol Map](./phase-2-ble-and-wifi-protocol-map.md)
- [Phase 3 - Android C# WiFi Direct Connector](./phase-3-android-csharp-wifi-direct-connector.md)
- [Phase 4 - Media Download And Camera Provider Path](./phase-4-media-download-and-camera-provider-path.md)
- [Phase 4f - P2P Sequence And Android Routing](./phase-4f-p2p-sequence-and-routing.md)
- [Phase 5 - Real Hardware Test Harness](./phase-5-real-hardware-test-harness.md)
- [Phase 6 - BodyCam Integration And UX Gate](./phase-6-bodycam-integration-and-ux-gate.md)
- [Phase 7 - Windows C# Wi-Fi Direct Route](./phase-7-windows-csharp-wifi-direct-route.md)
- [Phase 7a - Windows Field Guide And First Implementation Slice](./phase-7a-windows-field-guide-and-first-implementation-slice.md)
- [Phase 7b - Windows Route Diagnostics And Candidate Selection](./phase-7b-windows-route-diagnostics-and-candidate-selection.md)
- [Phase 7c - Windows Artifact Probe](./phase-7c-windows-artifact-probe.md)
- [Phase 7d - Windows Route Boundary And Pivot](./phase-7d-windows-route-boundary-and-pivot.md)
- [Phase 8 - Remove Android Vendor AAR BLE Bridge](./phase-8-remove-android-vendor-aar-ble-bridge.md)

## Report

The upfront option and probability report is here:

- [Options And Chances Report](./report-options-and-chances.md)
- [High-Level Log](./high-level-log.md)
