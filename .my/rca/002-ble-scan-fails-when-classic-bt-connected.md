# RCA 002: BLE scan cannot find HeyCyan glasses when Classic BT is already connected

**Date:** 2026-05-17  
**Status:** Open  
**Symptom:** Clicking the scan button on the Glasses page returns zero HeyCyan devices when the glasses are already paired and connected via Classic Bluetooth (HFP/A2DP audio profiles active).

---

## Root Cause

When HeyCyan M01 glasses have an active Classic BT audio connection to the
Windows PC, they **stop sending BLE advertisements** (or drastically reduce
their advertising rate). This is standard dual-mode Bluetooth behavior:

1. **Radio contention** — The M01 uses a single Bluetooth radio for both BLE
   and Classic. While streaming audio over HFP/A2DP, the radio's time slots
   are dominated by Classic BT traffic, leaving little or no airtime for BLE
   advertisement packets.

2. **Firmware advertising policy** — Many BLE peripherals disable or
   throttle advertising once a Classic BT profile (HFP, A2DP, SPP) is
   connected, because the device considers itself "already connected" and
   advertising would invite redundant connections.

3. **Windows BLE stack behavior** — `BluetoothLEAdvertisementWatcher` only
   reports devices that are actively broadcasting advertisements. It does
   **not** enumerate already-known or already-paired BLE devices. If the
   glasses aren't advertising, the watcher never fires `Received` for them.

### Why the current scan misses them

```
GlassesViewModel.ScanAsync()
  → HeyCyanGlassesDeviceManager.ScanAsync()
    → WindowsHeyCyanGlassesSession.ScanAsync(8s)
      → BluetoothLEAdvertisementWatcher (Active scanning)
        → watcher.Received += filter by service UUID or name prefix
        → Task.Delay(8s)
        → watcher.Stop()
        → returns matched devices   ← glasses not advertising → empty list
```

The scan relies **exclusively** on BLE advertisements. There is no fallback
path to find glasses that are already paired/connected via Classic BT.

---

## Scenarios

| State | BLE Advertising? | Scan Result | Expected |
|-------|------------------|-------------|----------|
| Glasses on, not paired | Yes (full rate) | Found | Correct |
| Glasses on, paired but BT audio disconnected | Yes (usually) | Found | Correct |
| Glasses on, Classic BT audio connected | **No / very slow** | **Not found** | Should be found |
| Glasses on, BLE GATT also connected (app session) | No (connected = no ads) | Not found | N/A (already connected) |

---

## Impact

This affects the reconnection UX. After the app is restarted while glasses
are still connected as a BT audio device (from the previous session or from
Windows Settings), the user cannot re-establish the BLE GATT session because
the scan button doesn't find the glasses.

The user must either:
- Disconnect BT audio in Windows Settings first, then scan (unintuitive)
- Power-cycle the glasses to force them back into advertising mode

---

## Proposed Fix Options

### Option A: Supplement scan with paired-device enumeration (Implemented)

After the `BluetoothLEAdvertisementWatcher` timeout, query Windows for
**both BLE-paired and Classic BT-paired devices** that match the HeyCyan
name pattern. This uses `DeviceInformation.FindAllAsync` with pairing-state
selectors, which work regardless of advertising state.

**Key finding:** The glasses appear as **Classic BT paired** (via
`BluetoothDevice.GetDeviceSelectorFromPairingState`), not BLE-paired. Both
selectors must be checked. The device was found as "M01 Pro_E6C9" with the
Classic BT selector.

```csharp
// After watcher.Stop(), before returning results:
var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
var pairedDevices = await DeviceInformation.FindAllAsync(selector);

foreach (var di in pairedDevices)
{
    // Filter by name (same prefixes: QC, O_, M01, Cyan)
    if (!IsHeyCyanName(di.Name)) continue;

    // Get BluetoothLEDevice to read address
    using var bleDevice = await BluetoothLEDevice.FromIdAsync(di.Id);
    if (bleDevice == null) continue;

    var address = FormatBluetoothAddress(bleDevice.BluetoothAddress);
    if (!devices.ContainsKey(bleDevice.BluetoothAddress))
    {
        var info = new HeyCyanDeviceInfo(di.Name, address, rssi: 0);
        devices[bleDevice.BluetoothAddress] = info;
        _log.LogInformation("BLE scan: found paired device {Name} ({Address}) — not advertising",
            di.Name, address);
    }
}
```

**Pros:** Non-invasive addition; scanned devices and paired devices merge
into the same list; RSSI=0 signals "paired but signal unknown".  
**Cons:** `FromIdAsync` may trigger a BLE connection attempt; needs testing
to confirm it doesn't disrupt existing Classic BT.

### Option B: Auto-reconnect from saved device address

Skip the scan entirely for known glasses. If `ISettingsService` has a saved
`LastHeyCyanDeviceAddress`, construct a `HeyCyanDeviceInfo` directly and
call `ConnectAsync` — the BLE connection can be established by address
using `BluetoothLEDevice.FromBluetoothAddressAsync(address)` without needing
the device to be advertising.

This is described in [4. device-persistence-design.md](../plan/m36-heycyan-windows/phase-7-classic-bt-pairing/4.%20device-persistence-design.md)
and complements Option A (auto-reconnect for saved devices, scan with
paired-device fallback for manual connection).

### Option C: Query Classic BT paired devices

Use `BluetoothDevice.GetDeviceSelectorFromPairingState(true)` (Classic BT
selector, not BLE) to find paired Classic devices. Cross-reference the
address with the BLE address. This catches devices that may not have a BLE
pairing record but do have a Classic BT pairing.

---

## Verification

1. Pair glasses normally, establish audio, close app
2. Re-open app → glasses should appear in scan results (via paired-device
   fallback) even though they aren't advertising BLE
3. Connect from scan → BLE GATT session established alongside existing
   Classic BT audio

---

## Related

- [RCA 001: InvalidCastException on WasapiCapture](001-com-invalidcast-mmdevice-wasapicapture.md) — stale COM objects from BT endpoint churn
- [Connection flow design (doc 3)](../plan/m36-heycyan-windows/phase-7-classic-bt-pairing/3.%20connection-flow-design.md) — `EnsureClassicBtAudioAsync` handles pairing after BLE connect
- [Device persistence design (doc 4)](../plan/m36-heycyan-windows/phase-7-classic-bt-pairing/4.%20device-persistence-design.md) — auto-reconnect by saved address
- [Fixed: windows-ble-scan-no-glasses](fixed/windows-ble-scan-no-glasses.md) — prior scan filter mismatch (different root cause)
