# Phase 6 — Windows Audio from HeyCyan Glasses

**Status:** In Progress  
**Depends on:** Phase 2 (BLE session — **complete**)  
**Research:** [research.md](research.md)  
**Sibling phases:** [Phase 1](../phase-1-ble-discovery/overview.md), [Phase 2](../phase-2-windows-ble/overview.md), [Phase 3](../phase-3-windows-wifi/overview.md), [Phase 4](../phase-4-integration/overview.md), [Phase 5](../phase-5-wifi-joining/overview.md)

---

## Research Outcome

**Audio does NOT flow over BLE GATT.** The BLE connection is only for
commands and notifications. There are two audio pathways:

1. **Live microphone** — BT Classic HFP/SCO (real-time, used by Android/iOS)
2. **On-glasses recording** — Opus files, triggered via BLE, downloaded via WiFi

The M01 glasses are BLE-only for **advertising/discovery**, which is why they
don't appear in Windows "Add Bluetooth device." But they may support Classic
BT audio profiles (HFP/SCO) once paired — Android/iOS pair them via the SDK,
then route audio through OS Bluetooth audio APIs.

See [research.md](research.md) for full analysis with code references.

---

## Implementation

### 6.1 — Programmatic BLE pairing after GATT connect ✅

Added `DeviceInformation.Pairing.PairAsync()` to `ConnectAsync` in
`WindowsHeyCyanGlassesSession`. After GATT services are discovered and
notifications subscribed, the session attempts to pair the device so
Windows can create a BT audio endpoint.

### 6.2 — Graceful audio fallback ✅

`HeyCyanGlassesDeviceManager.ConnectAsync` catches the
`InvalidOperationException` from `HeyCyanAudioInputProvider.StartAsync`
when no BT audio endpoint exists, logging a warning instead of crashing.

### 6.3 — Verify pairing creates audio endpoint

After pairing, check if Windows recognizes the glasses as an audio device.
The log will show one of:
- `"Paired successfully — BT audio endpoint may now be available"` → check
  if mic input appears in Windows Sound settings
- `"Pairing returned {Status}"` → pairing failed, audio unavailable

### 6.4 — Future: On-glasses recording fallback

If programmatic pairing doesn't produce an audio endpoint, implement
Pathway B (BLE command → opus recording → WiFi transfer). This requires
Phase 5 (WiFi joining) to be complete.

---

## Acceptance

- [x] Research doc written with audio pathway analysis
- [x] Programmatic BLE pairing added to connect flow
- [x] Graceful fallback when no audio endpoint available
- [ ] Verified: pairing creates audio endpoint (hardware test)
- [ ] Live mic audio flows from glasses to app on Windows
