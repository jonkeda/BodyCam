# Phase 2 — Windows BLE Session

**Status:** Proposed
**Depends on:** Phase 1 (characteristic UUIDs — **complete**)
**Sibling phases:** [Phase 1 — BLE Discovery](../phase-1-ble-discovery/overview.md), [Phase 3 — WiFi Transfer](../phase-3-windows-wifi/overview.md), [Phase 4 — Integration](../phase-4-integration/overview.md)

---

## Summary

Implement `WindowsHeyCyanGlassesSession : IHeyCyanGlassesSession` using
WinRT BLE APIs. This replaces the `NullHeyCyanGlassesSession` stub on
Windows and enables scanning, connecting, sending commands, and receiving
notifications from HeyCyan glasses.

### GATT UUIDs (from Phase 1)

```csharp
// Serial Port Service — used by LargeDataHandler.GlassesControl()
static readonly Guid SerialPortService        = Guid.Parse("de5bf728-d711-4e47-af26-65e3012a5dc7");
static readonly Guid SerialPortCharWrite      = Guid.Parse("de5bf72a-d711-4e47-af26-65e3012a5dc7");
static readonly Guid SerialPortCharNotify     = Guid.Parse("de5bf729-d711-4e47-af26-65e3012a5dc7");

// Device Information Service (standard SIG)
static readonly Guid DeviceInfoService        = Guid.Parse("0000180a-0000-1000-8000-00805f9b34fb");
static readonly Guid CharFirmwareRevision     = Guid.Parse("00002a26-0000-1000-8000-00805f9b34fb");
static readonly Guid CharHardwareRevision     = Guid.Parse("00002a27-0000-1000-8000-00805f9b34fb");
static readonly Guid CharSoftwareRevision     = Guid.Parse("00002a28-0000-1000-8000-00805f9b34fb");

// CCCD for enabling notifications
static readonly Guid GattNotifyConfig         = Guid.Parse("00002902-0000-1000-8000-00805f9b34fb");
```

---

## 2.1 — BLE scanning

### WinRT APIs

```csharp
using Windows.Devices.Bluetooth.Advertisement;

var watcher = new BluetoothLEAdvertisementWatcher
{
    ScanningMode = BluetoothLEScanningMode.Active
};
watcher.Received += (_, args) =>
{
    // Filter by device name prefix or advertised service UUID
    // Build HeyCyanDeviceInfo from args.BluetoothAddress, args.RawSignalStrengthInDbI
};
```

### Implementation

Create `src/BodyCam/Platforms/Windows/HeyCyan/WindowsHeyCyanGlassesSession.cs`:

- `ScanAsync(timeout, ct)` — Start `BluetoothLEAdvertisementWatcher`,
  collect devices matching the HeyCyan name prefix or service UUID,
  return `IReadOnlyList<HeyCyanDeviceInfo>`.
- Filter by advertised service UUID (from Phase 1) or device name
  containing "HeyCyan" / "Cyan" / glasses model prefix.
- Convert `ulong BluetoothAddress` to hex string for `HeyCyanDeviceInfo.Address`.

---

## 2.2 — GATT connection and characteristic discovery

```csharp
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
var services = await device.GetGattServicesForUuidAsync(serviceUuid);
var service = services.Services.First();
var chars = await service.GetCharacteristicsAsync();
// Find write and notify characteristics by UUID from Phase 1
```

### Implementation

- `ConnectAsync(device, ct)`:
  1. `BluetoothLEDevice.FromBluetoothAddressAsync(address)`
  2. `GetGattServicesForUuidAsync(SERVICE_UUID)`
  3. Discover write characteristic → store as `_txCharacteristic`
  4. Discover notify characteristic → subscribe via
     `WriteClientCharacteristicConfigurationDescriptorAsync(Notify)`
  5. Register `ValueChanged` handler → pipe raw bytes through
     `HeyCyanFrameParser` (already cross-platform)
  6. Update `State` → `Connected`, fire `StateChanged`

- `DisconnectAsync(ct)`:
  1. Unsubscribe from notify characteristic
  2. Dispose `BluetoothLEDevice`
  3. Update `State` → `Disconnected`

---

## 2.3 — Command sending

### Implementation

All commands go through a single helper:

```csharp
private async Task SendCommandAsync(byte[] command, CancellationToken ct)
{
    var writer = new DataWriter();
    writer.WriteBytes(command);
    var result = await _txCharacteristic.WriteValueWithResultAsync(
        writer.DetachBuffer(),
        GattWriteOption.WriteWithResponse); // or WriteWithoutResponse per Phase 1
    if (result.Status != GattCommunicationStatus.Success)
        throw new HeyCyanException($"BLE write failed: {result.Status}");
}
```

Wire up all `IHeyCyanGlassesSession` command methods to delegate to
`SendCommandAsync` with the byte arrays from `HeyCyanCommands`:

- `TakePhotoAsync` → `HeyCyanCommands.StartPhotoMode`
- `TakeAiPhotoAsync` → `HeyCyanCommands.TakeAiPhoto`
- `SyncTimeAsync` → `HeyCyanCommands.SyncTime(DateTimeOffset.UtcNow)`
- `EnterTransferModeAsync` → `HeyCyanCommands.EnterTransferMode`,
  then await notify frame `0x08` for IP address
- etc.

---

## 2.4 — Notification handling

The `ValueChanged` event handler on the notify characteristic receives raw
bytes. Pipe them through the existing `HeyCyanFrameParser`:

```csharp
private void OnCharacteristicValueChanged(
    GattCharacteristic sender,
    GattValueChangedEventArgs args)
{
    var reader = DataReader.FromBuffer(args.CharacteristicValue);
    var bytes = new byte[reader.UnconsumedBufferLength];
    reader.ReadBytes(bytes);

    var frame = HeyCyanFrameParser.Parse(bytes);
    // Dispatch to appropriate event (BatteryUpdated, ButtonPressed, etc.)
}
```

---

## Notes

- The existing `HeyCyanFrameParser` and `HeyCyanCommands` are in the shared
  `Services/Glasses/HeyCyan/` folder — no platform-specific code needed for
  command encoding or response parsing.
- WinRT BLE APIs require the `bluetooth` device capability in the app
  manifest. MAUI's Windows manifest may need:
  ```xml
  <DeviceCapability Name="bluetooth" />
  ```
- Consider MTU negotiation: call `device.RequestPreferredConnectionParameters()`
  or `GattSession.MaxPduSize` if large frames are needed.

---

## Acceptance

- [ ] `WindowsHeyCyanGlassesSession` implements `IHeyCyanGlassesSession`.
- [ ] BLE scan discovers HeyCyan glasses by name or service UUID.
- [ ] GATT connection established and characteristics discovered.
- [ ] Commands sent successfully (verified with at least Get Battery).
- [ ] Notify events received and parsed (battery update, button press).
- [ ] Unit tests for command encoding and frame parsing (reuse existing).
