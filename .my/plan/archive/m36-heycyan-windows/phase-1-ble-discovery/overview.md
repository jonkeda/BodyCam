# Phase 1 — BLE Protocol Discovery

**Status:** Complete
**Depends on:** None
**Sibling phases:** [Phase 2 — Windows BLE](../phase-2-windows-ble/overview.md), [Phase 3 — WiFi Transfer](../phase-3-windows-wifi/overview.md), [Phase 4 — Integration](../phase-4-integration/overview.md)

---

## Summary

GATT service and characteristic UUIDs have been extracted by decompiling the
Android AAR (`glasses_sdk_20250723_v01.aar` → `classes.jar` →
`com.oudmon.ble.base.communication.Constants`) and cross-referenced against
the iOS QCSDK.framework binary. Both SDKs use identical UUIDs.

---

## Extracted UUIDs

### Primary — Serial Port Service (used by `LargeDataHandler.GlassesControl()`)

This is the service used for all HeyCyan command traffic (photo, transfer
mode, battery queries, button events, etc.).

| Constant | UUID | Role |
|---|---|---|
| `SERIAL_PORT_SERVICE` | `de5bf728-d711-4e47-af26-65e3012a5dc7` | Service |
| `SERIAL_PORT_CHARACTER_WRITE` | `de5bf72a-d711-4e47-af26-65e3012a5dc7` | Write (TX → device) |
| `SERIAL_PORT_CHARACTER_NOTIFY` | `de5bf729-d711-4e47-af26-65e3012a5dc7` | Notify (RX ← device) |

### Secondary — Nordic UART Service (NUS)

Standard Nordic Semiconductor BLE UART. Likely used for basic data exchange
or firmware updates, not for `GlassesControl()` commands.

| Constant | UUID | Role |
|---|---|---|
| `UUID_SERVICE` | `6e40fff0-b5a3-f393-e0a9-e50e24dcca9e` | Service |
| `UUID_WRITE` | `6e400002-b5a3-f393-e0a9-e50e24dcca9e` | Write (TX → device) |
| `UUID_READ` | `6e400003-b5a3-f393-e0a9-e50e24dcca9e` | Notify (RX ← device) |

### Standard — Device Information Service (SIG)

| Constant | UUID | Role |
|---|---|---|
| `SERVICE_DEVICE_INFO` | `0000180a-0000-1000-8000-00805f9b34fb` | Service |
| `CHAR_FIRMWARE_REVISION` | `00002a26-0000-1000-8000-00805f9b34fb` | Firmware version (Read) |
| `CHAR_HW_REVISION` | `00002a27-0000-1000-8000-00805f9b34fb` | Hardware version (Read) |
| `CHAR_SOFTWARE_REVISION` | `00002a28-0000-1000-8000-00805f9b34fb` | Software version (Read) |

### Infrastructure

| Constant | UUID | Role |
|---|---|---|
| `GATT_NOTIFY_CONFIG` | `00002902-0000-1000-8000-00805f9b34fb` | CCCD (enable notifications) |

### iOS-only (unknown role)

| UUID | Notes |
|---|---|
| `7905fff0-b5ce-4e99-a40f-4b1e122d00d0` | Found only in QCSDK.framework binary; possibly a secondary service variant |

---

## Remaining open questions

These can be resolved during Phase 2 hardware testing:

- **Write mode**: Does the SDK use `Write` (with response) or
  `WriteWithoutResponse` for the serial port characteristic?
- **Envelope format**: Does `LargeDataHandler` add framing headers
  (length prefix, checksum, segmentation) around the `HeyCyanCommands`
  byte arrays, or are they sent raw?
- **MTU negotiation**: Is an explicit MTU request needed after connection?
- **Which service**: Confirm the Serial Port Service (not NUS) is used
  for `GlassesControl()` commands on the wire.

> These questions are low-risk — the Serial Port Service + Write/Notify
> pattern is clear from the `LargeDataHandler` class references. Hardware
> testing in Phase 2 will confirm the details.

---

## Source

- Android: `glasses_sdk_20250723_v01.aar` → `classes.jar` →
  `com/oudmon/ble/base/communication/Constants.class`
- iOS: `Alternative-HeyCyan-App-and-SDK/QCSDK.framework/QCSDK` binary

---

## Acceptance

- [x] Service UUID(s) extracted and documented.
- [x] Write characteristic UUID identified (`de5bf72a` serial port write).
- [x] Notify characteristic UUID identified (`de5bf729` serial port notify).
- [ ] Command wire format validated (raw vs. envelope) — deferred to Phase 2.
- [ ] Protocol reference file created — deferred to Phase 2.
