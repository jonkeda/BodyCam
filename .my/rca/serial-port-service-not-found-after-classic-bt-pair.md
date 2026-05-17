# RCA: Serial port service not found after Classic BT pairing added

**Date**: 2026-05-17
**Severity**: Connection failure — glasses cannot connect
**Status**: Open

## Symptom

After adding parallel Classic BT pairing (Phase 7), connecting to glasses
throws `InvalidOperationException`:

```
Serial port service not found on M01 Pro_E6C9
```

at `WindowsHeyCyanGlassesSession.ConnectAsync` line 180. This worked before
Phase 7 was implemented.

## Root Cause

`TryPairClassicAsync` runs in parallel with the BLE GATT connection. The
Classic BT pairing calls `BluetoothDevice.FromBluetoothAddressAsync(address)`
using the **same address** as the BLE device. On a dual-mode device, this may
cause Windows to treat the device as a Classic BT device instead of (or in
addition to) a BLE device, which can:

1. **Disrupt the BLE GATT connection** — Classic BT pairing may reset the
   underlying radio link or change the connection parameters, causing the
   BLE `GetGattServicesForUuidAsync` call to fail or return no services.

2. **Race on the Bluetooth stack** — both BLE and Classic BT operations
   contend for the same radio/adapter. On some Windows BT drivers, a Classic
   BT pairing operation blocks or interferes with concurrent BLE GATT
   service discovery.

3. **GATT cache invalidation** — pairing (BLE or Classic) can trigger a
   GATT service cache flush. If `GetGattServicesForUuidAsync` runs during
   the cache rebuild, it returns zero services.

## Why It Wasn't Caught

- Phase 7 was implemented and tested in the same session as the plan.
- No integration test exists for the parallel BLE+Classic BT connect flow.
- The previous BLE-only connection worked reliably because there was no
  concurrent Bluetooth operation interfering with GATT discovery.

## Fix Options

### Option A: Sequence Classic BT pairing AFTER BLE GATT setup

Move `TryPairClassicAsync` to run after GATT services are discovered and
notifications are subscribed. The pairing window may still be open at this
point (it's only a few hundred ms after BLE connect).

```csharp
// 1. BLE GATT connect + service discovery + subscribe notifications
// 2. THEN attempt Classic BT pairing
var classicPaired = await TryPairClassicAsync(address, ct);
```

**Risk:** Glasses may have left Classic BT pairing mode by this point.

### Option B: Retry GATT service discovery after failure

If `GetGattServicesForUuidAsync` returns zero services, wait briefly and
retry (the GATT cache may be rebuilding after pairing):

```csharp
var serviceResult = await _bleDevice.GetGattServicesForUuidAsync(SerialPortService)
    .AsTask(ct);
if (serviceResult.Services.Count == 0)
{
    await Task.Delay(2000, ct);
    serviceResult = await _bleDevice.GetGattServicesForUuidAsync(SerialPortService)
        .AsTask(ct);
}
```

### Option C: Sequence with delay — BLE first, then Classic BT with overlap

Start BLE GATT connect, wait for service discovery to complete, then fire
Classic BT pairing. This avoids the race while still catching the pairing
window.

**Recommended: Option A or C** — sequencing is safer than retrying.

## Files to Change

- `src/BodyCam/Platforms/Windows/HeyCyan/WindowsHeyCyanGlassesSession.cs`
