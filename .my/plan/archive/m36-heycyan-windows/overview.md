# M36 — HeyCyan Windows Connectivity (BLE + WiFi Transfer)

> **Archived on 2026-05-31.** Superseded by
> [M46 Phase 7 - Windows C# Wi-Fi Direct Route](../../m46-heycyan-csharp-wifi-retry/phase-7-windows-csharp-wifi-direct-route.md).
> Kept for Windows research and RCA history only.

**Status:** Proposed
**Depends on:** M33 (HeyCyan SDK, landed), M35 (.NET 10 update, landed)

## Context

HeyCyan glasses connectivity is fully implemented on Android (via AAR SDK)
and iOS (via QCSDK.framework), but Windows uses a `NullHeyCyanGlassesSession`
stub that throws `NotSupportedException`. Windows 10/11 has full BLE GATT
support via WinRT APIs (`Windows.Devices.Bluetooth`), and the glasses' BLE
command protocol is already documented in cross-platform code.

### Current state

| Area | Android | iOS | Windows |
|---|---|---|---|
| BLE scanning/connection | AAR SDK (`BleBaseControl`) | QCSDK.framework | None (null stub) |
| BLE commands | `LargeDataHandler.GlassesControl()` | `QCSDKCmdCreator` | None |
| Frame parsing | `HeyCyanFrameParser` (shared) | `HeyCyanFrameParser` (shared) | — |
| WiFi transfer | WiFi P2P (`WifiP2pManager`) | Hotspot join (`NEHotspotConfiguration`) | None |
| HTTP file download | `HeyCyanMediaTransfer` (shared) | `HeyCyanMediaTransfer` (shared) | — |

### What we know

**BLE command protocol** (fully documented in `HeyCyanCommands.cs`):

| Command | Bytes |
|---|---|
| Enter Transfer Mode | `0x02 0x01 0x04` |
| Exit Transfer Mode | `0x02 0x01 0x09` |
| Take Photo | `0x02 0x01 0x01` |
| Take AI Photo | `0x02 0x01 0x06 0x02 0x02` |
| Get Media Count | `0x02 0x04` |
| Sync Time | `0x03` + 4-byte LE unix timestamp |

**Notify frame format** (fully documented in `HeyCyanFrameParser.cs`):
- `loadData[6]` = type discriminator (0x02=AI-photo, 0x03=AI-voice,
  0x05=battery, 0x08=transfer IP, 0x09=P2P error)

**Known BLE service UUIDs**: `QCSDKSERVERUUID1`, `QCSDKSERVERUUID2`
(defined as extern constants in iOS SDK headers — actual UUID strings
need extraction).

**Unknown**: GATT characteristic UUIDs for write (commands) and notify
(responses). These are embedded in the proprietary SDKs and must be
discovered via GATT enumeration or BLE traffic sniffing.

---

## Goals

1. **BLE connectivity on Windows** — scan, connect, send commands, receive
   notifications from HeyCyan glasses using WinRT BLE APIs.
2. **WiFi image transfer on Windows** — join glasses WiFi hotspot and
   download photos/videos via the existing HTTP transfer protocol.
3. **Reuse cross-platform code** — leverage `HeyCyanFrameParser`,
   `HeyCyanMediaTransfer`, and the `IHeyCyanGlassesSession` interface.

---

## Phases

- [Phase 1 — BLE Protocol Discovery](phase-1-ble-discovery/overview.md)
- [Phase 2 — Windows BLE Session](phase-2-windows-ble/overview.md)
- [Phase 3 — Windows WiFi Transfer](phase-3-windows-wifi/overview.md)
- [Phase 4 — Integration & Testing](phase-4-integration/overview.md)
- [Phase 5 — Windows WiFi Hotspot Joining](phase-5-wifi-joining/overview.md)
- [Phase 6 — WiFi Photo Transfer](phase-6-wifi-photos/overview.md)
- [Phase 6b — BLE Audio Streaming](phase-6-ble-audio/overview.md)
- [Phase 7 — Classic Bluetooth Audio Pairing](phase-7-classic-bt-pairing/overview.md)
- [Phase 10 — Windows Audio Endpoint Activation](phase-10-windows-audio-activation/overview.md)
