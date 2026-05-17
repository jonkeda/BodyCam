# Phase 4 — Integration & Testing

**Status:** In Progress
**Depends on:** Phase 2, Phase 3
**Sibling phases:** [Phase 1 — BLE Discovery](../phase-1-ble-discovery/overview.md), [Phase 2 — Windows BLE](../phase-2-windows-ble/overview.md), [Phase 3 — WiFi Transfer](../phase-3-windows-wifi/overview.md)

---

## Summary

Wire the Windows BLE session and WiFi transfer into the DI container,
update the app manifest, and verify end-to-end functionality with real
hardware.

---

## 4.1 — DI registration

In `src/BodyCam/ServiceExtensions.cs`, update the `#else` (Windows) branch:

```csharp
#elif WINDOWS
    services.AddSingleton<IHeyCyanGlassesSession,
        BodyCam.Platforms.Windows.HeyCyan.WindowsHeyCyanGlassesSession>();
    services.AddSingleton<IHeyCyanHttpClientFactory,
        BodyCam.Platforms.Windows.HeyCyan.WindowsHeyCyanHttpClientFactory>();
#else
    services.AddSingleton<IHeyCyanGlassesSession,
        Services.Glasses.HeyCyan.NullHeyCyanGlassesSession>();
    services.AddSingleton<IHeyCyanHttpClientFactory,
        Services.Glasses.HeyCyan.NullHeyCyanHttpClientFactory>();
#endif
```

The null stub remains for any other platform (macOS Catalyst, etc.).

---

## 4.2 — App manifest capabilities

In `src/BodyCam/Platforms/Windows/Package.appxmanifest`, add:

```xml
<Capabilities>
  <DeviceCapability Name="bluetooth" />
  <DeviceCapability Name="wifiControl" />
</Capabilities>
```

- `bluetooth` — Required for BLE scanning and GATT access.
- `wifiControl` — Required for programmatic WiFi network joining.

---

## 4.3 — UI integration

The existing glasses UI in BodyCam (connection status, battery, buttons)
should work automatically since it binds to `IHeyCyanGlassesSession` events
via the shared `HeyCyanGlassesDeviceManager`. Verify:

- Glasses appear in scan results on the Settings → Devices page.
- Connection status updates in real-time.
- Battery percentage displays after connection.
- Button press events (AI photo, AI voice) trigger the appropriate agents.
- Transfer mode downloads photos to the device.

---

## 4.4 — Testing

### Manual testing (requires hardware)

| Test | Steps | Expected |
|---|---|---|
| **Scan** | Open Devices settings, tap Scan | HeyCyan glasses appear in list |
| **Connect** | Tap glasses entry | State → Connected, battery shown |
| **Battery** | Wait 30s after connect | Battery % updates via notify |
| **AI Photo button** | Press AI photo button on glasses | App triggers AI photo flow |
| **Take Photo** | Tap Take Photo in app | Glasses capture photo |
| **Transfer** | Trigger transfer mode | WiFi joins, photos download |
| **Disconnect** | Tap Disconnect | Clean teardown, WiFi restored |

### Unit tests

- Verify `HeyCyanFrameParser` handles all frame types (already tested).
- Verify `HeyCyanCommands` byte arrays are correct (already tested).
- Mock `GattCharacteristic` write/notify for `WindowsHeyCyanGlassesSession`
  state machine tests (if feasible with WinRT mocking).

### Integration tests

- BLE scan with no glasses present → returns empty list (no crash).
- Connect to unavailable address → timeout with clean error.
- WiFi join failure → graceful fallback with error message.

---

## 4.5 — Error handling

- BLE connection lost mid-session → fire `StateChanged(Disconnected)`,
  clean up resources.
- WiFi join fails (user denies, network unavailable) → throw
  `HeyCyanTransferException` with actionable message.
- Glasses out of range during transfer → `HttpRequestException` caught
  by `HeyCyanMediaTransfer` retry logic.

---

## Acceptance

- [x] Windows DI registers `WindowsHeyCyanGlassesSession` (not null stub).
- [x] App manifest includes `bluetooth` capability (`wifiControl` deferred to Phase 5).
- [ ] End-to-end: scan → connect → take photo → transfer → disconnect works.
- [ ] Graceful error handling for connection loss and WiFi failures.
- [x] No regression on Android or iOS builds.
- [x] GlassesViewModel `IsScanning`/`StopScan` tests added and passing (37/37).
- [x] All 223 HeyCyan tests pass after changes.
