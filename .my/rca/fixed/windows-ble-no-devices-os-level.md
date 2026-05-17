# RCA: Windows BLE scan finds no devices — OS-level failure

**Date**: 2026-05-16

## Symptom

After fixing the app-level BLE scan filters (see `windows-ble-scan-no-glasses.md`),
the BodyCam app still finds no HeyCyan glasses. Additionally, **Windows Settings →
Bluetooth & devices** also cannot discover the glasses — this is not app-specific.

## Root Cause

The issue is **below the application layer**. Windows itself cannot see the BLE
advertisements. Possible causes, in order of likelihood:

### 1. Bluetooth adapter does not support BLE (LE) or has outdated drivers

Some older USB Bluetooth dongles and built-in adapters only support Classic
Bluetooth (BR/EDR) and cannot receive BLE advertisements. Even adapters that
technically support BLE may need updated drivers to function correctly with
Windows 10/11 BLE APIs.

**Check**: Run in PowerShell:
```powershell
Get-PnpDevice -Class Bluetooth | Format-Table Name, Status, InstanceId
# Also check adapter LE support:
[Windows.Devices.Bluetooth.BluetoothAdapter]::GetDefaultAsync().GetAwaiter().GetResult() | 
    Select-Object IsLowEnergySupported, IsCentralRoleSupported, BluetoothAddress
```

### 2. Bluetooth radio is off or in a bad state

The Bluetooth radio may be disabled, in airplane mode, or in a hung state
requiring a reset.

**Check**:
- Settings → Bluetooth & devices → verify toggle is ON
- Try toggling Bluetooth off and on
- Check Device Manager for warning icons on the Bluetooth adapter
- Restart the Bluetooth Support Service:
  ```powershell
  Restart-Service bthserv
  ```

### 3. Glasses are not advertising

The HeyCyan glasses may be powered off, out of range, or in a connected state
with another device (many BLE peripherals stop advertising once connected).

**Check**:
- Ensure glasses are powered on and in pairing/discoverable mode
- Disconnect glasses from any phone or other device
- Verify with a known-good BLE scanner (e.g., nRF Connect on Android)

### 4. Windows BLE driver/stack bug

Some Windows Bluetooth stacks have known issues with BLE scanning, particularly
with certain Realtek and Intel adapters. Windows may silently fail to report
advertisements.

**Check**:
- Update Bluetooth adapter driver from manufacturer (not Windows Update generic)
- Try a different USB Bluetooth 5.0+ dongle (e.g., TP-Link UB500)
- Check Event Viewer → System log for Bluetooth errors

### 5. Proximity / RF interference

BLE has limited range (~10m typical). Interference from USB 3.0 ports, Wi-Fi,
or other 2.4 GHz devices can reduce range significantly.

**Check**:
- Move glasses within 1 meter of the PC
- Try a different USB port (preferably USB 2.0, away from USB 3.0 ports)

## Diagnostic Steps

Run an unfiltered BLE scan to see if **any** BLE devices are visible:

```powershell
# Quick test — does the BLE watcher see anything at all?
# If this returns 0 devices, the problem is OS/hardware, not app code.
Add-Type -AssemblyName Windows.Devices.Bluetooth.Advertisement
$watcher = [Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher]::new()
$count = 0
$handler = Register-ObjectEvent -InputObject $watcher -EventName Received -Action { $global:count++ }
$watcher.Start()
Start-Sleep 10
$watcher.Stop()
Unregister-Event -SourceIdentifier $handler.Name
Write-Host "BLE advertisements received in 10 seconds: $count"
```

If count > 0 but glasses aren't found, the issue is back in the app-level
filters. If count = 0, the Bluetooth adapter/driver cannot receive BLE ads.

## Resolution

This is a hardware/driver/OS issue, not an application bug. The app-level BLE
scan code is correct (filters were already fixed). Next steps:

1. Confirm BLE adapter support with the diagnostic commands above
2. Update Bluetooth drivers
3. If adapter doesn't support BLE, use a BLE 5.0 USB dongle
4. Test with nRF Connect on a phone to confirm glasses are advertising
