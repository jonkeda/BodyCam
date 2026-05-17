# iOS Deployment Guide

This document covers iOS-specific deployment requirements for BodyCam, particularly related to HeyCyan smart glasses integration.

## Prerequisites

- Apple Developer Account with **Paid Tier** (required for Hotspot Configuration capability)
- Xcode installed on macOS (for iOS builds and provisioning)
- Valid provisioning profile with all required capabilities enabled

## Required Capabilities

The app requires the following capabilities to be enabled in the Apple Developer portal:

### Hotspot Configuration

**CRITICAL:** This capability is **paid-tier only**. Free Apple ID profiles cannot sign apps with `com.apple.developer.networking.HotspotConfiguration`.

To enable:
1. Go to [Apple Developer Portal → Identifiers](https://developer.apple.com/account/resources/identifiers/list)
2. Select your **BodyCam** app identifier
3. Under **Capabilities**, enable **Hotspot Configuration**
4. Save changes
5. Regenerate your provisioning profile(s)
6. Download and install the updated profile(s) in Xcode

**Without this capability:**
- `NEHotspotConfigurationManager.ApplyConfiguration` will fail with **error code 8** (`unauthorized`)
- The glasses' WiFi hotspot cannot be joined programmatically
- Media transfer from glasses to phone will not work

## Required Permissions

The app requests the following runtime permissions on iOS. These prompts appear on first launch in the order listed:

### 1. Bluetooth (Always)
**When:** First BLE scan attempt  
**Why:** Required to discover and connect to HeyCyan glasses over Bluetooth Low Energy  
**User-facing string:** "BodyCam connects to HeyCyan smart glasses over Bluetooth Low Energy to control capture and read battery, firmware, and media counts."

**If denied:** The glasses will not be discoverable and all HeyCyan features will be unavailable.

### 2. Local Network
**When:** First `NEHotspotConfigurationManager.ApplyConfiguration` call  
**Why:** Required to join the glasses' WiFi hotspot for media transfer  
**User-facing string:** "BodyCam joins the glasses' Wi-Fi hotspot to download captured photos, videos, and audio recordings."

**If denied:** Media transfer will fail silently.

### 3. Microphone
**When:** First audio recording or live conversation  
**Why:** Required for live AI conversations and dictation  
**User-facing string:** "BodyCam records audio for live conversations and dictation."

**If denied:** Audio features will be unavailable.

### 4. Camera
**When:** First photo/video capture attempt  
**Why:** Fallback to phone camera when glasses are not connected  
**User-facing string:** "BodyCam captures photos with the phone camera when no glasses are connected."

**If denied:** Phone camera capture will be unavailable (glasses camera still works via BLE).

### 5. Location (When In Use)
**When:** First SSID read attempt during hotspot join verification  
**Why:** iOS 13+ requires location permission to read the current WiFi SSID via `CNCopyCurrentNetworkInfo`  
**User-facing string:** "BodyCam reads the active Wi-Fi SSID to confirm the glasses hotspot is currently joined before downloading media."

**If denied:** The app cannot verify the hotspot join succeeded and may attempt to download from the wrong network.

## Background Modes

The app declares the following background modes in `Info.plist`:

- **`bluetooth-central`** — Allows BLE scanning to continue when the app is suspended during long capture sessions. Without this, scans pause on suspend and glasses appear to "drop".
- **`audio`** — Allows live conversation audio to continue in the background.

## Troubleshooting

### Error Code 8 from `NEHotspotConfigurationManager.ApplyConfiguration`

**Symptoms:**
- Hotspot join fails with error code `8` (`unauthorized`)
- Media transfer never starts
- No system permission prompt appears

**Causes:**
1. **Hotspot Configuration capability not enabled** in the Apple Developer portal
2. **Provisioning profile is stale** — generated before the capability was enabled
3. **Free Apple ID profile** — Hotspot Configuration is paid-tier only
4. **Entitlements.plist missing or not referenced** in `BodyCam.csproj`

**Resolution:**
1. Verify the capability is enabled in the [Apple Developer portal](https://developer.apple.com/account/resources/identifiers/list)
2. Regenerate the provisioning profile (it must be created *after* enabling the capability)
3. Download and install the new profile in Xcode
4. Clean and rebuild the app: `dotnet build -t:Clean && dotnet build -f net10.0-ios`
5. Verify `Entitlements.plist` contains:
   ```xml
   <key>com.apple.developer.networking.HotspotConfiguration</key>
   <true/>
   ```
6. Verify `BodyCam.csproj` sets:
   ```xml
   <CodesignEntitlements>Platforms/iOS/Entitlements.plist</CodesignEntitlements>
   ```

### BLE Scan Returns No Results

**Symptoms:**
- `QCCentralManager.scanDevices` callback never fires
- No glasses appear in device list
- No system permission prompt appears

**Causes:**
1. **Bluetooth permission denied or not requested**
2. **`NSBluetoothAlwaysUsageDescription` missing** from `Info.plist` — causes silent denial
3. **Background mode `bluetooth-central` missing** — scans pause on suspend

**Resolution:**
1. Check iOS **Settings → BodyCam → Bluetooth** — ensure it's set to "Always"
2. If no Bluetooth entry appears, verify `Info.plist` contains `NSBluetoothAlwaysUsageDescription`
3. Uninstall and reinstall the app to trigger the permission prompt again
4. For background scanning, verify `UIBackgroundModes` includes `bluetooth-central`

### SSID Read Returns `nil`

**Symptoms:**
- `CNCopyCurrentNetworkInfo` returns `nil` even though the device is joined to the glasses' hotspot
- Hotspot join verification fails

**Causes:**
1. **Location permission denied or not requested**
2. **`NSLocationWhenInUseUsageDescription` missing** from `Info.plist`
3. iOS 13+ requires location permission to read SSID

**Resolution:**
1. Check iOS **Settings → BodyCam → Location** — ensure it's set to "While Using"
2. If no Location entry appears, verify `Info.plist` contains `NSLocationWhenInUseUsageDescription`
3. Request location permission before attempting to read SSID

### App Rejected at App Store Submission

**Symptoms:**
- Binary rejected with message about missing usage descriptions
- App Store Connect shows "Invalid Binary"

**Causes:**
- One or more `NS*UsageDescription` keys missing from `Info.plist`

**Resolution:**
Verify all required keys are present with non-empty, user-friendly strings:
- `NSBluetoothAlwaysUsageDescription`
- `NSLocalNetworkUsageDescription`
- `NSCameraUsageDescription`
- `NSMicrophoneUsageDescription`
- `NSLocationWhenInUseUsageDescription`

## Build Commands

### iOS Device Build
```pwsh
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-ios
```

### iOS Simulator Build
```pwsh
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-ios --runtime iossimulator-x64
```

### Clean Build
```pwsh
dotnet clean src/BodyCam/BodyCam.csproj -f net10.0-ios
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-ios
```

## Testing on Real Hardware

The following features **require real iOS hardware** (cannot be tested in simulator):

- HeyCyan glasses BLE connection (simulator has no Bluetooth)
- Hotspot Configuration (simulator has no WiFi hardware)
- Background BLE scanning (simulator does not suspend apps realistically)
- Live audio streaming (simulator audio behavior differs from device)

## See Also

- [M33 Phase 6 Overview](../../.my/plan/m33-heycyan-sdk/phase6-ios-binding.md) — iOS binding architecture
- [Wave 4: Info.plist & Entitlements](../../.my/plan/m33-heycyan-sdk/phase6-ios-binding/wave4-infoplist-entitlements.md) — Implementation details for this configuration
- [Apple: Hotspot Configuration](https://developer.apple.com/documentation/networkextension/nehotspotconfigurationmanager) — Official API docs
- [Apple: Provisioning Profiles](https://developer.apple.com/support/profiles/) — Managing profiles
