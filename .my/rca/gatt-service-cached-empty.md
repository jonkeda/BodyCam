# RCA: GATT service discovery returns empty results (cached mode)

**Date**: 2026-05-17
**Severity**: Connection failure — glasses cannot connect
**Status**: Open

## Symptom

`GetGattServicesForUuidAsync(SerialPortService)` returns zero services,
throwing `Serial port service not found on M01 Pro_E6C9`. This occurs
even without Classic BT pairing interference (that was fixed separately).

## Root Cause

`GetGattServicesForUuidAsync` defaults to `BluetoothCacheMode.Cached`.
Windows maintains a GATT service cache for paired/known BLE devices. The
cache can be empty or stale when:

1. **First connection** — device has never been enumerated, no cache exists.
2. **After pairing/unpairing** — BLE pairing invalidates the cache.
3. **After device firmware update** — services may have changed.
4. **Cache corruption** — Windows BT stack occasionally loses cached data.

The cached query returns immediately with zero results instead of performing
live GATT service discovery on the device.

## Fix

Use `BluetoothCacheMode.Uncached` to force live service discovery:

```csharp
var serviceResult = await _bleDevice.GetGattServicesForUuidAsync(
    SerialPortService, BluetoothCacheMode.Uncached)
    .AsTask(ct);
```

This performs an actual GATT read from the device, which is slower (~1-2s)
but reliable. The same change should apply to `GetCharacteristicsAsync`.

## Files Changed

- `src/BodyCam/Platforms/Windows/HeyCyan/WindowsHeyCyanGlassesSession.cs`
