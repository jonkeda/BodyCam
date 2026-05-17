# iOS HeyCyan Session Implementation (M33 Phase 6 Wave 2)

## Overview

This wave implements `IosHeyCyanGlassesSession` — the iOS implementation of the HeyCyan glasses connection session interface. Since the vendor QCSDK framework is Objective-C and uses a delegate callback pattern (rather than Android's request/response model), the implementation differs structurally from Android but maintains identical semantics.

## Files Created

### Bindings (completed in Wave 1, extended in Wave 2)

- `src/BodyCam.HeyCyan.iOS.Bindings/ApiDefinition.cs`
  - Added `OdmBleConstants` static class with NSString constants:
    - `QcsdkServerUuid1` / `QcsdkServerUuid2` (BLE service UUIDs for scanning)
    - `OdmNotifyD2P` (NSNotification name for device-to-phone button events)
    - `OdmNotifyD2PDataKey` (userInfo key for extracting notify frame bytes)
    - `OdmBleConnectState` (KVO key for connection state observation)

### Platform Implementation

- `src/BodyCam/Platforms/iOS/HeyCyan/IosHeyCyanGlassesSession.cs`
  - Core iOS session class implementing `IHeyCyanGlassesSession`
  - Subclasses `NSObject` for Objective-C interop
  - Uses `CBCentralManager` for BLE scanning (not `QCCentralManager` — that's a demo app helper, not part of QCSDK)
  - Uses `QCSDKManager` singleton for peripheral registration and delegate callbacks
  - Uses `QCSDKCmdCreator` static methods for mode commands (photo, video, AI photo, transfer)
  - Registers NSNotificationCenter observer for `OdmNotifyD2P` to receive button events
  - Delegates battery/media/AI photo callbacks via `QCSDKManagerDelegateProxy`
  - Reuses shared `HeyCyanFrameParser.TryParseButton` for button frame parsing (same fixtures as Android)

### Cross-Platform Wrapper

- `src/BodyCam/Services/Glasses/HeyCyan/IosHeyCyanGlassesSession.cs`
  - Thin #if IOS wrapper matching Android's pattern
  - iOS doesn't need explicit permission requests (CoreBluetooth handles them automatically)
  - Pass-through to platform-specific implementation

## Architecture Notes

### Why No `IHeyCyanSdkBridge` for iOS?

Unlike Android (which uses `HeyCyanSdkBridge` → `HeyCyanGlassesSessionCore`), iOS directly implements `IHeyCyanGlassesSession`. This is because:

1. The iOS QCSDK uses Objective-C delegates + NSNotificationCenter — very different from Android's callback-based `LargeDataHandler`
2. The wave doc explicitly shows direct iOS implementation
3. Future refactoring could introduce an iOS bridge to share `HeyCyanGlassesSessionCore` logic, but that's out of scope for Wave 2

### Button Event Flow

```
Glasses button press
  ↓
BLE notify frame (QCSDK internal)
  ↓
NSNotificationCenter posts OdmNotifyD2P
  ↓
IosHeyCyanGlassesSession.OnD2PNotification
  ↓
HeyCyanFrameParser.TryParseButton (shared with Android)
  ↓
ButtonPressed event raised
```

### Transfer Mode (Wave 3 dependency)

`EnterTransferModeAsync` currently:
1. Opens the hotspot via `QCSDKCmdCreator.OpenWifi`
2. Returns a placeholder base URL
3. Logs a warning that Wave 3 (`HotspotHttpClient`) is needed for full implementation

Wave 3 will add `NEHotspotConfiguration` join + IP discovery.

## Deviations from Wave Doc

1. **QCCentralManager**: The wave doc assumed `QCCentralManager` was part of the QCSDK framework. It's actually a demo app helper. Implementation uses `CBCentralManager` directly.
2. **Transfer mode**: Partially stubbed pending Wave 3 `HotspotHttpClient`.

## Verification Limitations

- Cannot fully test on Windows (no iOS simulator, no macOS build host)
- iOS build shows pre-existing errors in `IosMediaStore.cs`:
  - Line 40: `PHAssetCreationRequest.CreationRequestForAssetFromImage` not found
  - Line 80: `PHAssetCreationRequest.CreationRequestForAssetFromVideo` not found
  - These errors are unrelated to Wave 2 (media store was already broken)
- Android build still passes ✅
- iOS bindings build cleanly ✅
- Code is #if IOS guarded so it doesn't affect Android or tests

## Next Steps (Wave 3)

- `src/BodyCam/Platforms/iOS/HeyCyan/HotspotHttpClient.cs`
- Implement `NEHotspotConfiguration` join
- Implement IP discovery probe loop
- Wire into `IosHeyCyanGlassesSession.EnterTransferModeAsync`
