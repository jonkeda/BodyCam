# RCA: BLE scan returns 0 devices despite glasses advertising

**Date**: 2026-05-16

## Symptom

The `_tmp` diagnostic scanner finds `M01 Pro_E6C9` (839 total BLE
advertisements, device at RSSI -68), but `WindowsHeyCyanGlassesSession.ScanAsync`
returns 0 devices in the running BodyCam app.

Log output:
```
WindowsHeyCyanGlassesSession: Information: BLE scan complete: 0 device(s) found
```

## Differences: `_tmp` scanner vs app scanner

| Aspect | `_tmp` diagnostic | App (`ScanAsync`) |
|---|---|---|
| Filter | **None** — accepts all devices | Name prefix + service UUID filter |
| Total ad count | Logged (839) | **Not logged** — no visibility |
| Watcher status check | None (but it works) | **None** — silent failure possible |
| Rejected device logging | N/A | **None** — can't tell if ads arrive |
| Runtime context | Console app, fresh process | MAUI/WinUI3, long-lived singleton |

## Possible Root Causes

### 1. App not restarted after rebuild (running old binary)
The M01 name filter was added in this session. If the app process was still
running when the build completed, it uses the old code without the M01 filter.
`/p:WindowsPackageType=None` builds an unpackaged exe — the old process must be
killed before the new binary takes effect.

### 2. Watcher silently fails to start
`BluetoothLEAdvertisementWatcher.Start()` does not throw on failure. It
transitions `Status` to `Aborted` instead of `Started`. The app never checked
`watcher.Status` after calling `Start()`, so a failed watcher would silently
produce 0 results.

### 3. Watcher conflict with other Bluetooth activity
The app runs `WindowsBluetoothEnumerator` and `WindowsBluetoothOutputEnumerator`
singletons that may also interact with the Bluetooth stack. A concurrent BLE
watcher or device enumeration could interfere.

## Fix Applied

Added diagnostic logging to `WindowsHeyCyanGlassesSession.ScanAsync`:

1. **Total advertisement counter** (`Interlocked.Increment`) — mirrors `_tmp` behavior
2. **Watcher status check** after `Start()` — logs warning if status != `Started`
3. **Rejected device logging** (at Trace level) — shows ads received but filtered out
4. **Matched device logging** promoted to Information — immediately visible
5. **Completion log** now includes total ads: `"BLE scan complete: {Count} device(s)
   matched, {Total} total advertisements received"`

## Next Steps

Restart the app and press Scan. The new logs will reveal which scenario it is:

- `"BLE watcher failed to start — status: Aborted"` → watcher failure (cause 2)
- `"BLE scan complete: 0 matched, 0 total"` → no ads received at all (cause 2 or 3)
- `"BLE scan complete: 0 matched, N total"` → ads received but all filtered out (filter bug)
- `"BLE scan matched: M01 Pro_E6C9 ..."` → it works, previous run was stale binary (cause 1)
