# RCA: HeyCyan audio providers never appear in Windows dropdowns

**Date:** 2026-05-17  
**Symptom:** After BLE + Classic BT pairing succeeds, HeyCyan Glasses Mic/Speaker never appear in the Microphone/Speaker dropdowns on the Device settings page on Windows.

## Root Cause

Two compounding bugs in the Windows Bluetooth audio enumeration pipeline prevent HeyCyan (or any BT) audio endpoints from being discovered and matched.

### Bug 1: `IsBluetoothDevice()` never matches on Windows

`WindowsBluetoothEnumerator.IsBluetoothDevice(MMDevice)` checks whether the `MMDevice.ID` contains `"BTHENUM"` or `"Bluetooth"`:

```csharp
private static bool IsBluetoothDevice(MMDevice device)
{
    var id = device.ID ?? string.Empty;
    return id.Contains("BTHENUM", ...)
        || id.Contains("Bluetooth", ...);
}
```

**Problem:** On Windows 10/11, `MMDevice.ID` is a GUID-based endpoint identifier like `{0.0.0.00000000}.{aee631d7-922d-45ef-b82e-a3e0518cb088}`. It **never** contains "BTHENUM" or "Bluetooth". Therefore `ScanAndRegister()` never registers any `bt:` providers, and the hot-plug listener never fires for BT devices.

**Evidence** (from diagnostic test app):
```
Headphones (M01 Pro_E6C9)
    ID: {0.0.0.00000000}.{aee631d7-922d-45ef-b82e-a3e0518cb088}
```

The "BTHENUM" information is stored in `MMDevice.Properties`, not in `MMDevice.ID`:
- `DEVPKEY_Device_EnumeratorName` (`{a45c254e-df1c-4efd-8020-67d146a850e0}#24`) = `"BTHENUM"`
- Device instance path (`{b3f8fa53-0004-438e-9003-51a46e139bfc}#2`) = `{1}.BTHENUM\{...}\...\D879B87FE6C9_C00000000`

### Bug 2: `ProviderId` is a GUID, not a MAC address

Even if Bug 1 were fixed, `WindowsBluetoothAudioProvider` sets:

```csharp
ProviderId = $"bt:{device.ID}";
// → "bt:{0.0.0.00000000}.{aee631d7-922d-45ef-b82e-a3e0518cb088}"
```

But `BluetoothAudioInputProvider.HasEndpointWithMac(mac)` strips the `"bt:"` prefix and compares the remainder against the HeyCyan MAC address (`D8:79:B8:7F:E6:C9`). A GUID will never match a MAC. On Android this works because `ProviderId = "bt:{btDevice.Address}"` where `Address` IS the MAC.

### Combined effect

1. `WindowsBluetoothEnumerator.ScanAndRegister()` → zero providers registered (Bug 1)
2. `HeyCyanAudioInputProvider.IsAvailable` → `HasEndpointWithMac(mac)` finds nothing → `false`
3. `HeyCyanAudioRouter.ApplyAsync()` → registers providers but `IsAvailable` is false → logs warning
4. `DeviceViewModel.AudioInputProviders` → `.Where(p => p.IsAvailable)` filters them out
5. UI dropdowns show no HeyCyan entries

## Fix approach

### Fix Bug 1: Use `DEVPKEY_Device_EnumeratorName` to detect BT devices

```csharp
private static bool IsBluetoothDevice(MMDevice device)
{
    // DEVPKEY_Device_EnumeratorName
    var enumeratorKey = new PropertyKey(
        new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);
    try
    {
        var enumerator = device.Properties[enumeratorKey]?.Value?.ToString();
        return string.Equals(enumerator, "BTHENUM", StringComparison.OrdinalIgnoreCase);
    }
    catch { return false; }
}
```

### Fix Bug 2: Extract MAC from device instance path

The MAC address is embedded in the BTHENUM instance path at property `{b3f8fa53-0004-438e-9003-51a46e139bfc}#2`. Example:

```
{1}.BTHENUM\{0000110B-...}_VID&000105D6_PID&000A\7&10C9280&0&D879B87FE6C9_C00000000
                                                              ^^^^^^^^^^^^
                                                              MAC (no separators)
```

Extract the 12-hex-digit string before `_C00000000` and format as `XX:XX:XX:XX:XX:XX`:

```csharp
ProviderId = $"bt:{ExtractMacFromInstancePath(instancePath)}";
```

## Diagnostic test app output

See `_tmp/stdout.txt` for full output. Key data points:

| Device | MMDevice.ID | Instance Path MAC | WinRT BT Address |
|--------|-------------|-------------------|------------------|
| M01 Pro_E6C9 | `{0.0.0.00000000}.{aee631d7-...}` | `D879B87FE6C9` | `D8:79:B8:7F:E6:C9` |
| 1MORE HQ51 | `{0.0.0.00000000}.{2ada9cfb-...}` | `00BB43847BE9` | `00:BB:43:84:7B:E9` |
