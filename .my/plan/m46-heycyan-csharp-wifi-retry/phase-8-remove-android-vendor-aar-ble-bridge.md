# Phase 8 - Remove Android Vendor AAR BLE Bridge

## Goal

Replace the Android HeyCyan vendor AAR dependency with a C# BLE implementation
that uses Android Bluetooth APIs directly.

The result should be:

- no `BodyCam.HeyCyan.Android.Bindings` dependency in the Android app;
- no `glasses_sdk_20250723_v01.aar` in the production Android path;
- the existing `IHeyCyanGlassesSession`, `HeyCyanMediaTransfer`, and
  `HeyCyanCameraProvider` contracts still work;
- the Phase 5 real-hardware harness passes without the AAR.

## Why This Is Separate

Phase 4f/5 proved the C# Wi-Fi/media path:

- C# enters transfer mode;
- C# forms Android Wi-Fi Direct/P2P;
- C# fetches `/files/media.config`;
- C# downloads valid JPEG and MP4 media.

But the BLE/control side still depends on the vendor AAR through
`HeyCyanSdkBridge`.

Removing that is a different risk profile. It means replacing scan, connect,
GATT service discovery, command writes, notify parsing, and lifecycle recovery.

## Current Dependency

Android currently uses:

- `src/BodyCam.HeyCyan.Android.Bindings/Jars/glasses_sdk_20250723_v01.aar`;
- `src/BodyCam/Platforms/Android/HeyCyan/HeyCyanSdkBridge.cs`;
- `src/BodyCam/Services/Glasses/HeyCyan/AndroidHeyCyanGlassesSession.cs`;
- shared command/session logic in `HeyCyanGlassesSessionCore`.

The AAR gives us the BLE transport and vendor callbacks. BodyCam wraps it in C#.

## Target Architecture

```text
AndroidHeyCyanBleTransport
  -> BluetoothLeScanner / BluetoothAdapter
  -> BluetoothGatt
  -> service + characteristic discovery
  -> write command frames
  -> receive notify frames
  -> HeyCyanGlassesSessionCore
  -> HeyCyanMediaTransfer / HeyCyanCameraProvider
```

Keep `HeyCyanGlassesSessionCore` as the protocol owner where possible. Replace
only the Android transport/bridge underneath it.

## Known Protocol Pieces

Commands already proven or strongly supported:

- take photo: `02 01 01`;
- start video: `02 01 02`;
- stop video: `02 01 03`;
- enter transfer: `02 01 04`;
- exit transfer: `02 01 09`;
- reset P2P: `02 01 0f`;
- IP poll/readiness candidate: `02 03`.

Frame/parser work already exists in:

- `HeyCyanCommands`;
- `HeyCyanFrameParser`;
- `HeyCyanGlassesSessionCore`.

Phase 8 should reuse these rather than rediscovering command semantics.

## Unknowns To Resolve

- Exact BLE service UUIDs. Resolved:
  `de5bf728-d711-4e47-af26-65e3012a5dc7`.
- Write characteristic UUID. Resolved:
  `de5bf72a-d711-4e47-af26-65e3012a5dc7`.
- Notify characteristic UUID. Resolved:
  `de5bf729-d711-4e47-af26-65e3012a5dc7`.
- Whether commands need vendor framing beyond the payload bytes. Resolved:
  BodyCam's existing serial-port frames match the vendor SDK wrapping.
- MTU requirements. Resolved for this flow: default chunking at `244` bytes is
  sufficient for current command traffic.
- Whether command writes require write-with-response or write-without-response.
  Resolved: write without response works for the HeyCyan serial-port
  characteristic.
- Whether reconnect needs pairing/bonding or can use normal GATT connect.
  Resolved for tested hardware: normal GATT connect works.
- How the AAR handles Android 12+ permission edge cases. Deferred to the
  existing app permission flow; Phase 8 did not add a new permission UI.

## Discovery Plan

### 1. Inventory The Binding Surface

Map all AAR APIs currently used by `HeyCyanSdkBridge`:

- scan start/stop;
- connect/disconnect;
- command write;
- notify callback;
- button callback;
- battery/version/media-count callbacks.

Write a table that maps each AAR call to the C# BLE replacement.

### 2. Capture GATT Facts From The Running Phone

Use the already-connected phone/glasses setup:

- enable Android Bluetooth HCI snoop logs if available;
- capture logcat around connect and command writes;
- inspect GATT services/characteristics through a small C# probe;
- record UUIDs, properties, CCCD behavior, MTU, and write type.

Save artifacts under:

```text
.my/plan/m46-heycyan-csharp-wifi-retry/captures/phase-8-android-ble-aar-removal/
```

### 3. Build A Minimal C# BLE Probe

Create an Android-only C# probe that:

- scans for `M01`/`QC`/HeyCyan devices;
- connects by BLE MAC/name;
- discovers services;
- subscribes to notifications;
- sends one low-risk command, such as battery/version;
- records raw notifications.

Do not touch the production provider until this probe works.

### 4. Implement `AndroidHeyCyanBleBridge`

Create a C# implementation that satisfies the existing bridge/session needs:

- scan;
- connect;
- disconnect;
- send command payload;
- publish raw notifications;
- publish state changes.

Prefer keeping `IHeyCyanSdkBridge` temporarily as the seam, then rename it after
the AAR is gone.

### 5. Run The Existing Session Tests Against The New Bridge

Add fakes/unit tests for:

- command write payloads;
- notify frame parsing;
- state transitions;
- cancellation/timeout behavior;
- disconnect cleanup.

Keep Phase 5 real-hardware test as the end-to-end acceptance test.

### 6. Remove The AAR Dependency

After the C# BLE bridge passes:

- remove the Android binding project reference from `BodyCam.csproj`;
- remove or archive `BodyCam.HeyCyan.Android.Bindings`;
- remove AAR-generated namespaces from production code;
- update docs so "C# only" means BLE plus Wi-Fi/media.

## Acceptance

- [x] Android app builds without `BodyCam.HeyCyan.Android.Bindings`.
- [x] Android app contains no production reference to
  `glasses_sdk_20250723_v01.aar`.
- [x] App scans and connects to `M01 Pro_E6C9`.
- [x] App reads version and battery through direct C# BLE.
- [x] App sends photo/video/transfer commands through direct C# BLE.
- [x] Phase 5 real-hardware harness passes:
  - fresh photo;
  - fresh video;
  - C# Wi-Fi transfer;
  - valid JPEG;
  - valid MP4.
- [x] Normal test suite remains green.

## Implementation Result

Phase 8 is implemented in the production Android path.

- Replaced `Platforms/Android/HeyCyan/HeyCyanSdkBridge.cs` with a direct C#
  Android BLE bridge using `BluetoothLeScanner`, `BluetoothGatt`, service
  discovery, CCCD notification enablement, direct writes, and notify parsing.
- Kept the existing `IHeyCyanSdkBridge`, `HeyCyanGlassesSessionCore`,
  `HeyCyanMediaTransfer`, and `HeyCyanCameraProvider` path intact.
- Added `HeyCyanDirectBleProtocol` so direct BLE frames map back into the same
  response and raw-notify shapes the old session code expected.
- Removed the Android production app reference to
  `BodyCam.HeyCyan.Android.Bindings`; the old binding project can stay on disk
  as reference material, but it is not part of the Android app build.
- Added the SDK-matching `0x47` device-config/support pulse with payload
  `01 00`, then used it as a transfer activation pulse and periodic transfer
  keepalive alongside `GetWifiIP`.

## Validation

Validation run on 2026-05-31:

- Focused HeyCyan BLE/session tests passed: `18` passed.
- Full unit test suite passed: `1171` passed, `2` skipped.
- Android build passed for `net10.0-android`: `0` errors, known warnings only.
- Installed APK on `SM-S931B`.
- Gated real-hardware test passed:
  `HeyCyanRealHardwareWifiTests.Android_probe_downloads_fresh_photo_and_video_through_csharp_wifi_transfer`.

Hardware artifact:

```text
.my/plan/m46-heycyan-csharp-wifi-retry/captures/phase-5-real-hardware-test-harness/20260531-234952/
```

Observed run:

- Device: `M01 Pro_E6C9` / `D8:79:B8:7F:E6:C9`.
- Version: `AM01C_V2.0/AM01C_2.00.03_250718`.
- WiFi firmware: `WIFIAM01C_1.00.15_2507111740`.
- Direct BLE transfer command succeeded with `02 01 04`.
- `GetWifiIP` returned `192.168.49.183`.
- `GetDeviceConfig` sent `BC47020000200100` and returned
  `BC4710003CA001030100000000000000000000000000`.
- Android Wi-Fi Direct formed with the glasses peer
  `M01 Pro_D879B87FE6C9/60:c2:2a:1a:b6:1b`.
- `/files/media.config` succeeded at `192.168.49.183`.
- Downloaded fresh JPEG `20260531234943012.jpg`, `371145` bytes,
  signature `FFD8`.
- Downloaded fresh MP4 `20260531234947896.mp4`, `4816656` bytes,
  signature `ftypisom`.

## Stop Or Pivot Criteria

Pivot or defer if:

- the BLE link requires encrypted/vendor-authenticated payloads not visible from
  the AAR boundary;
- Android hides required GATT details behind pairing behavior we cannot
  reproduce reliably;
- direct C# BLE works only after the vendor AAR has initialized private state;
- removing the AAR breaks audio/control behavior that has no known GATT
  equivalent.

## Probability

Moderate.

The command payloads are now well understood, and the existing session core is
already separated from the Android bridge. The main risk is not protocol
semantics; it is discovering and reproducing the exact BLE GATT transport
behavior that the AAR currently hides.
