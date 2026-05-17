# RCA 001: InvalidCastException on WasapiCapture for BT audio endpoint

**Date:** 2026-05-17  
**Symptom:** `System.InvalidCastException` when `WindowsBluetoothAudioProvider.StartAsync` creates a `WasapiCapture` from a cached `MMDevice`:

```
Unable to cast COM object of type 'System.__ComObject' to interface type
'NAudio.CoreAudioApi.Interfaces.IMMDevice'. This operation failed because the
QueryInterface call on the COM component for the interface with
IID '{D666063F-1587-4E43-81F1-B948E807363F}' failed due to the following error:
No such interface supported (0x80004002 (E_NOINTERFACE)).
```

## Root Cause

`WindowsBluetoothAudioProvider` holds a reference to an `MMDevice` COM object that was captured at registration time (in `WindowsBluetoothEnumerator.ScanAndRegister`). By the time `StartAsync` is called — potentially seconds or minutes later — the underlying COM object has become **stale**:

1. **Bluetooth endpoint disconnected/reconnected** — the HFP capture endpoint was removed and re-created by Windows when the BT link cycled. The old `MMDevice` COM proxy (`System.__ComObject`) still exists in managed memory but the backing COM server has released its `IMMDevice` interface.

2. **COM apartment mismatch** — `ScanAndRegister` runs on the UI thread (`MainPage.Loaded`), but `StartAsync` may execute on a background thread (e.g. from `HeyCyanGlassesDeviceManager.ConnectAsync`). NAudio's `MMDevice` wraps an STA COM object; calling `AudioClient` from an MTA thread triggers a cross-apartment `QueryInterface` that fails with `E_NOINTERFACE` if the STA pump isn't active or the proxy wasn't marshalled.

3. **Device state transition** — the `MMDevice` may have transitioned from `Active` to `NotPresent`/`Unplugged` between `ScanAndRegister` and `StartAsync`. The `IsAvailable` property guard catches `DeviceState` but not the case where the COM proxy itself is invalidated.

### Code path

```
WindowsBluetoothEnumerator.ScanAndRegister()
  → _enumerator.EnumerateAudioEndPoints(Capture, Active)
  → new WindowsBluetoothAudioProvider(device, settings, mac)   ← stores MMDevice
  → _manager.RegisterProvider(provider)

[... later, possibly on different thread ...]

HeyCyanGlassesDeviceManager.ConnectAsync()
  → _mic.StartAsync(ct)                         // HeyCyanAudioInputProvider
    → _bt.StartAsync(ct)                         // BluetoothAudioInputProvider
      → _selectedProvider.StartAsync(ct)          // WindowsBluetoothAudioProvider
        → new WasapiCapture(_device)              ← accesses _device.AudioClient
          → MMDevice.GetAudioClient()
            → QueryInterface(IID_IMMDevice) → E_NOINTERFACE 💥
```

## Fix

Re-acquire the `MMDevice` from the `MMDeviceEnumerator` at `StartAsync` time instead of caching the object from scan time. This ensures the COM proxy is fresh and on the correct thread.

In `WindowsBluetoothAudioProvider`:

```csharp
// Before (broken): stores MMDevice at construction, uses it later
public WindowsBluetoothAudioProvider(MMDevice device, AppSettings settings, string mac)
{
    _device = device;           // ← stale by StartAsync time
    ...
}

public Task StartAsync(CancellationToken ct = default)
{
    _capture = new WasapiCapture(_device);  // ← E_NOINTERFACE
    ...
}

// After (fix): store device ID, re-acquire MMDevice at start time
public WindowsBluetoothAudioProvider(MMDevice device, AppSettings settings, string mac)
{
    _deviceId = device.ID;                  // ← immutable string ID
    DisplayName = $"BT: {device.FriendlyName}";
    ...
}

public Task StartAsync(CancellationToken ct = default)
{
    using var enumerator = new MMDeviceEnumerator();
    var device = enumerator.GetDevice(_deviceId);  // ← fresh COM proxy
    if (device.State != DeviceState.Active)
        throw new InvalidOperationException($"BT endpoint {_deviceId} is not active (state: {device.State})");

    _capture = new WasapiCapture(device);
    ...
}
```

Also wrap the `WasapiCapture` construction in a try/catch for `InvalidCastException` and `COMException` to provide a clear diagnostic instead of an unhandled crash:

```csharp
try
{
    _capture = new WasapiCapture(device) { WaveFormat = device.AudioClient.MixFormat };
}
catch (InvalidCastException ex)
{
    throw new InvalidOperationException(
        $"BT capture endpoint '{DisplayName}' COM proxy is invalid — device may have disconnected.", ex);
}
```

## Affected Files

- `src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothAudioProvider.cs` — stores stale `MMDevice`
- `src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothEnumerator.cs` — creates provider with `MMDevice` at scan time
- `src/BodyCam/Services/Audio/BluetoothAudioInputProvider.cs` — calls `StartAsync` on the stale provider

## Status

**Fixed** — Both `WindowsBluetoothAudioProvider` and `WindowsBluetoothAudioOutputProvider` now store `device.ID` and re-acquire a fresh `MMDevice` via `MMDeviceEnumerator.GetDevice()` at `StartAsync` time. `InvalidCastException` and stale-device cases are caught with clear diagnostics.
