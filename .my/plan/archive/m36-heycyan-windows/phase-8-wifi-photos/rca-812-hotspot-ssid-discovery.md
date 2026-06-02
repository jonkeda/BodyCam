# RCA-812: Hotspot SSID Discovery Failure

**Date:** 2025-05-18  
**Status:** INVESTIGATING  
**Depends on:** RCA-810 (channel-based BLE fix)  
**Related:** RCA-811 (E2E test blocked by this)

## Problem

After entering transfer mode via BLE (command accepted, ACK received), WiFi scans show **zero** glasses-related SSIDs. Only corporate "Exact-*" networks appear. The `TryWiFiHotspotAsync()` strategy always returns null.

## Current SSID Matching (`IsLikelyGlassesHotspot`)

```csharp
ssid.StartsWith("QC", OrdinalIgnoreCase)
|| ssid.StartsWith("O_", OrdinalIgnoreCase)
|| ssid.StartsWith("M01", OrdinalIgnoreCase)
|| ssid.Contains("Cyan", OrdinalIgnoreCase)
|| ssid.Contains("HeyCyan", OrdinalIgnoreCase)
|| ssid.StartsWith("DIRECT-", OrdinalIgnoreCase)
```

None of these matched during test runs (28-30 "Exact-*" SSIDs visible, nothing else).

## Hypotheses

### H1: SSID uses hardware version prefix (HIGH confidence)

Firmware reports `WIFIAM01G1_V9.2`, ROM version `WIFIAM01G1_1.00.23_2510111600`. The prefix **"WIFIAM01"** or **"WIFIAM01G1"** is a very plausible hotspot SSID that does NOT match any current pattern.

**Fix:** Add `ssid.StartsWith("WIFI", OrdinalIgnoreCase)` to the pattern list.

### H2: Glasses rely on WiFi Direct P2P only (no standalone AP)

The Android SDK uses `WifiP2pManagerSingleton` exclusively — it never scans for a traditional hotspot SSID. WiFi Direct groups create an SSID like `DIRECT-xx-<name>` which requires P2P discovery (not a passive scan). Windows `WiFiAdapter.ScanAsync()` may not see P2P group SSIDs.

**Fix:** Use Windows WiFi Direct APIs (`WiFiDirectDevice`) instead of/alongside passive scanning.

### H3: BLE command variant difference

- Our code: `BuildFrame(0x41, [0x01, 0x04])` → 2-byte payload
- Android SDK: `glassesControl(byteArrayOf(0x02, 0x01, 0x04))` → 3-byte payload (0x02 prefix)
- iOS SDK: `openWifiWithMode:QCOperatorDeviceModeTransfer` → receives SSID+password in callback

The doc comment says "0x02, 0x01, 0x04" but our code sends `[0x01, 0x04]`. Our command IS accepted (ACK returned), so it may just be an SDK abstraction difference. But the missing `0x02` byte could mean "enter transfer mode without enabling hotspot AP" vs "enable hotspot AP."

**Fix:** Try sending `[0x02, 0x01, 0x04]` as the payload and see if a hotspot appears.

### H4: iOS flow receives SSID dynamically via BLE notify

The iOS SDK's `openWifiWithMode` success callback delivers `(NSString *ssid, NSString *password)` — meaning the glasses BLE-notify the exact SSID and password to the app. We may be ignoring this notification because it arrives as a different payload format within the 0x41 action.

Currently we only look for `payload[0] == 0x08` (IP bytes). There might be a preceding `0x07` or other sub-command carrying SSID text.

**Fix:** Log ALL 0x41 notifications in full hex during transfer mode to capture any SSID payload.

### H5: Corporate network interference (LOW confidence)

The corporate "Exact-*" network might suppress non-enterprise APs via management frames, or the laptop WiFi radio is locked to a band/channel that the glasses hotspot doesn't use.

**Fix:** Test on a personal network or with airplane mode + personal hotspot off.

## Proposed Investigation Steps

1. **Immediate — Add SSID pattern:** Add `"WIFI"` prefix to `IsLikelyGlassesHotspot()` (covers "WIFIAM01G1" etc.)
2. **Immediate — Full BLE logging:** Dump ALL raw 0x41 notifications (not just 0x08) during transfer mode to look for SSID text
3. **Try 3-byte command:** Change `EnterTransferMode()` to `BuildFrame(ActionGlassesControl, [0x02, 0x01, 0x04])` matching Android exactly
4. **WiFi Direct API:** Investigate using `Windows.Devices.WiFiDirect.WiFiDirectDevice` for P2P discovery as an alternative to passive scanning
5. **Non-corporate test:** Run the test on a personal/mobile hotspot network

## Key Files

- `src/BodyCam/Platforms/Windows/HeyCyan/WindowsGlassesWiFiManager.cs` — `IsLikelyGlassesHotspot()`, `DiscoverGlassesSsidAsync()`
- `src/BodyCam/Platforms/Windows/HeyCyan/WindowsHeyCyanGlassesSession.cs` — `TryWiFiHotspotAsync()`, `EnterTransferModeAsync()`
- `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanCommands.cs` — `EnterTransferMode()` (currently `[0x01, 0x04]`)
- `Alternative-HeyCyan-App-and-SDK/android/AGENTS.md` — Android trigger sequence
- `Alternative-HeyCyan-App-and-SDK/WIFI_TRANSFER_ARCHITECTURE.md` — iOS flow with dynamic SSID delivery

## Quick Wins

```csharp
// WindowsGlassesWiFiManager.IsLikelyGlassesHotspot — add:
|| ssid.StartsWith("WIFI", StringComparison.OrdinalIgnoreCase)

// HeyCyanCommands.EnterTransferMode — try matching Android exactly:
public static byte[] EnterTransferMode() => BuildFrame(ActionGlassesControl, [0x02, 0x01, 0x04]);
```
