# RCA-809: BLE Serial Port Protocol Fix

**Date:** 2025-05-18  
**Status:** FIXED — verified with hardware  
**Severity:** Critical (all BLE commands broken on Windows)

## Symptom

After connecting to HeyCyan M01 Pro glasses, all BLE commands (battery, sync time, enter transfer mode) produced no response. Notifications never fired on the Serial Port or NUS characteristics.

## Root Cause

Two bugs in `WindowsHeyCyanGlassesSession.SendCommandAsync` and `HeyCyanCommands`:

### Bug 1: Wrong GATT write option

```csharp
// BROKEN — glasses silently ignore unacknowledged writes
GattWriteOption.WriteWithoutResponse

// FIXED
GattWriteOption.WriteWithResponse
```

The Jieli chipset firmware drops BLE writes that don't request a link-layer ACK (except for the heartbeat command, which is handled at the radio level).

### Bug 2: Missing protocol framing

Commands were sent as raw byte arrays (e.g. `{0x02, 0x04}` for battery) directly to characteristic `de5bf72a`. The glasses expect the **Serial Port protocol frame**:

```
[0xBC][action][len_lo][len_hi][crc16_lo][crc16_hi][payload...]
```

- **0xBC** — sync byte
- **action** — command type (0x40=time, 0x41=glasses control, 0x42=battery, 0x43=info, 0x45=heartbeat)
- **length** — payload byte count, little-endian uint16
- **CRC-16** — CRC-16/ARC (poly 0xA001, init 0xFFFF) over payload only, little-endian
- **payload** — command-specific data (empty for battery/info/heartbeat)

Empty-payload commands use `0xFF 0xFF` as the CRC sentinel.

## Investigation Path

1. Found commit `c9e9890` introduced `UnpairAsync` that broke bonding → removed it. Notifications still dead.
2. Full GATT diagnostic proved 9 services discovered, all CCCDs subscribed successfully.
3. Wrote to every notify-capable characteristic — only `ae02` (Jieli radio echo) produced notifications.
4. Confirmed device is alive: HeyCyan phone app reads battery = 100%.
5. Tried RFCOMM/SPP via `StreamSocket` → `WSASERVICE_NOT_FOUND`. Tried COM ports → no response.
6. Decompiled Android SDK (`com.oudmon.ble.base`) → found the 0xBC Serial Port protocol with action IDs, CRC-16, and LE byte order.
7. Sent `BuildFrame(0x45, [])` (heartbeat) with `WriteWithResponse` → **got first response**: `BC-45-01-00-7E-E6-89`.
8. Fixed length field to little-endian → battery, device info, and sync time all responded.

## Fix Applied

**`HeyCyanCommands.cs`** — Complete rewrite. Each method now returns a properly framed 0xBC packet with CRC-16/ARC. Added `BuildFrame(byte action, ReadOnlySpan<byte> payload)` and `Crc16()`.

**`WindowsHeyCyanGlassesSession.cs`** — Three changes:
1. `SendCommandAsync`: `WriteWithoutResponse` → `WriteWithResponse`
2. `OnCharacteristicValueChanged`: Parse 0xBC frame (action at byte[1], payload at byte[6+]) instead of old byte[6] dispatch.
3. `GetBatteryAsync`: Wait for action `0x42` instead of old notify type `0x05`.
4. `EnterTransferModeAsync`: Wait for action `0x41` (GlassesControl response).

## Hardware Verification

```
[TX] Heartbeat:  BC-45-00-00-FF-FF           → RX: BC-45-01-00-7E-E6-89 (status)
[TX] Battery:    BC-42-02-00-01-B0-00-00     → RX: BC-42-02-00-EA-B0-64-01 (100%, charging)
[TX] SyncTime:   BC-40-04-00-59-0E-...       → RX: BC-40-01-00-BF-40-00 (ACK)
[TX] DeviceInfo: BC-43-00-00-FF-FF           → RX: 87 bytes (FW/HW versions)
```

DeviceInfo decoded: BT FW `AM01C_2.00.03_250718`, BT HW `AM01C_V2.0`, WiFi FW `AM01C_1.00.15_250711`.

## Build Status

0 errors, 165 unit tests passing.
