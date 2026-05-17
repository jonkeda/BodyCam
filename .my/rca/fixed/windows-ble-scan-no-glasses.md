# RCA: Windows BLE scan finds no HeyCyan glasses

## Symptom

Scanning for glasses on Windows returns 0 devices. The same glasses are
discoverable on Android and iOS.

## Root Cause

The `WindowsHeyCyanGlassesSession.ScanAsync` filter had **two mismatches**
against the actual BLE advertisements from HeyCyan glasses:

### 1. Wrong service UUID in scan filter

The scan checked for `de5bf728-d711-4e47-af26-65e3012a5dc7` (Serial Port
Service) in the advertisement's `ServiceUuids` list. This UUID is a GATT
service discoverable only **after** connection — it is **not included in
advertisement packets**.

The glasses advertise these UUIDs instead:

| UUID | Name |
|---|---|
| `7905fff0-b5ce-4e99-a40f-4b1e122d00d0` | QCSDKSERVERUUID1 (primary) |
| `6e40fff0-b5a3-f393-e0a9-e50e24dcca9e` | QCSDKSERVERUUID2 (Nordic UART variant) |

Source: QCSDK.framework binary, iOS `QCCentralManager.m`
(`retrieveConnectedPeripherals:`), and project README.

### 2. Wrong device name filter

The scan checked for names **containing** `"Cyan"`, `"HeyCyan"`, or
`"QCSDK"`. The glasses actually advertise names starting with `"QC_"` or
`"O_"` (legacy), none of which match those substrings.

Evidence:
- Android `HeyCyanSdkBridge.cs` filters with
  `name.StartsWith("QC", OrdinalIgnoreCase)`
- CyanBridge `DeviceClassifier.kt` checks `startsWith("O_")` or
  `startsWith("Q_")`

### Why it worked on Android/iOS

- **Android**: The AAR SDK handles BLE scanning internally via
  `BleBaseControl`. The app-level code in `HeyCyanSdkBridge.cs` post-filters
  by `name.StartsWith("QC")`.
- **iOS**: `QCCentralManager` passes `nil` to
  `scanForPeripheralsWithServices:` (no service filter), then uses
  `QCSDKSERVERUUID1`/`QCSDKSERVERUUID2` for `retrieveConnectedPeripherals:`.

## Fix Applied

Updated `WindowsHeyCyanGlassesSession.cs`:

**Service UUID filter** — now checks all three UUIDs (advertised + connection):

```csharp
var hasService = serviceUuids.Contains(QcSdkServiceUuid1)    // 7905fff0-...
              || serviceUuids.Contains(QcSdkServiceUuid2)    // 6e40fff0-...
              || serviceUuids.Contains(SerialPortService);   // de5bf728-... (fallback)
```

**Name filter** — now uses prefix matching consistent with Android:

```csharp
var nameMatch = name.StartsWith("QC", StringComparison.OrdinalIgnoreCase)
             || name.StartsWith("O_", StringComparison.OrdinalIgnoreCase)
             || name.Contains("Cyan", StringComparison.OrdinalIgnoreCase);
```

## Verification

Requires hardware test. If glasses still don't appear, run a diagnostic
unfiltered scan that logs **all** BLE advertisements with their name and
service UUIDs to determine the exact advertisement payload.
