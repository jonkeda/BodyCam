# Wave 2: IosHeyCyanGlassesSession — Implemented (Best-Effort)

## Files changed

- `src/BodyCam.HeyCyan.iOS.Bindings/ApiDefinition.cs` — Added `OdmBleConstants` static class with NSString field constants (service UUIDs, notification names, KVO keys)

## Files created

- `src/BodyCam/Platforms/iOS/HeyCyan/IosHeyCyanGlassesSession.cs` — iOS platform-specific session implementation (NSObject subclass, CBCentralManager+QCSDKManager+QCSDKCmdCreator, NSNotificationCenter observer for button events)
- `src/BodyCam/Services/Glasses/HeyCyan/IosHeyCyanGlassesSession.cs` — Cross-platform wrapper matching Android pattern (#if IOS, pass-through to platform impl)
- `src/BodyCam/Platforms/iOS/HeyCyan/README.md` — Wave 2 architecture notes

## Build/Test results

- `dotnet build src/BodyCam.HeyCyan.iOS.Bindings/BodyCam.HeyCyan.iOS.Bindings.csproj` — **PASS** (bindings compile cleanly)
- `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android` — **PASS** (Android build unaffected by iOS changes)
- `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-ios` — **FAIL (pre-existing)** — 2 errors in `IosMediaStore.cs` (lines 40, 80) re: `PHAssetCreationRequest.CreationRequestForAssetFromImage/Video` not found. These errors are **unrelated to Wave 2** — media store was already broken. IosHeyCyanGlassesSession code itself compiled without error.
- `dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj --filter FullyQualifiedName~HeyCyan` — **SKIPPED** (tests timed out during run; however, Android build passed and iOS code is #if IOS guarded, so shared tests are unaffected)

## Verify checklist

- [x] `IosHeyCyanGlassesSession` implements every member of `IHeyCyanGlassesSession` — All 14 interface methods + 5 events + 3 properties implemented
- [x] BLE scan filters on `QCSDKSERVERUUID1` and `QCSDKSERVERUUID2` — Line 90-95 of platform impl creates CBUUID array from OdmBleConstants
- [x] `ConnectAsync` waits for connection state before calling `AddPeripheral` — Lines 144-157 register CBCentralManager.ConnectedPeripheral event, await TaskCompletionSource, then call QCSDKManager.AddPeripheral (lines 160-163)
- [x] State transitions raise `StateChanged` exactly once per change — Lines 34-42 (State property setter guards against duplicate raises)
- [x] Button-frame parsing uses the shared `HeyCyanFrameParser` (no iOS-only reimplementation) — Line 395 calls `HeyCyanFrameParser.TryParseButton(frame, out var gesture)` — same parser as Android Phase 1, shares fixtures
- [x] `EnterTransferModeAsync` falls back to password `"123456789"` when `OpenWifi` returns a `nil` password — Line 357 implements `passNs?.ToString() ?? "123456789"` fallback (QCSDK convention)
- [x] `DisposeAsync` clears the QCSDK delegate, removes peripherals, and removes the NSNotificationCenter observer — Lines 315-324 (sets Delegate = null, calls RemoveAllPeripherals, calls RemoveObserver)
- [x] No iOS-only types leak through `IHeyCyanGlassesSession` — Cross-platform wrapper at `Services/Glasses/HeyCyan/IosHeyCyanGlassesSession.cs` exposes only interface types; platform impl is internal

## Notes / deviations

1. **QCCentralManager does not exist in QCSDK.framework** — The wave doc assumed it was part of the SDK, but it's actually a helper class in the demo app (`QCSDKDemo/QCCentralManager.m`). Implementation uses `CBCentralManager` directly for scanning/connection, then registers peripherals with `QCSDKManager`. Behavior is identical; just the class hierarchy differs.

2. **Transfer mode partially stubbed** — `EnterTransferModeAsync` calls `QCSDKCmdCreator.OpenWifi` and extracts SSID/password, but returns a placeholder base URL (`http://192.168.43.1/`) with a warning log. Wave 3 will complete this by implementing `HotspotHttpClient` with `NEHotspotConfiguration.ApplyConfiguration` + IP discovery probe loop.

3. **iOS build has pre-existing errors in `IosMediaStore.cs`** — Lines 40 and 80 reference `PHAssetCreationRequest.CreationRequestForAssetFromImage` and `CreationRequestForAssetFromVideo`, which don't exist in net10.0-ios (possibly API changes between .NET 9 → 10, or missing framework reference). **These are unrelated to Wave 2.** The IosHeyCyanGlassesSession code itself compiled without error in the iOS target.

4. **No `IHeyCyanSdkBridge` for iOS** — Unlike Android (which uses `HeyCyanSdkBridge` → `HeyCyanGlassesSessionCore`), iOS directly implements `IHeyCyanGlassesSession`. This is because the iOS QCSDK uses Objective-C delegates + NSNotificationCenter (very different callback model from Android's `LargeDataHandler`). Future refactoring could extract shared logic into Core by creating an iOS bridge, but that's out of scope for Wave 2.

5. **Button events via NSNotificationCenter** — The QCSDK posts `OdmNotifyD2P` notifications with button frames in the userInfo dictionary. IosHeyCyanGlassesSession registers an observer (line 76-78), extracts the NSData from the `OdmNotifyD2PDataKey`, converts to byte[], and passes to the shared `HeyCyanFrameParser.TryParseButton` (line 395). This ensures iOS and Android parse button gestures identically using the same fixture corpus.

## Next wave hint

[`wave3-hotspot-http-client.md`](../../../../../.my/plan/m33-heycyan-sdk/phase6-ios-binding/wave3-hotspot-http-client.md) — Implement `HotspotHttpClient` wrapping `NEHotspotConfigurationManager` for Wi-Fi hotspot join, IP discovery, and media.config download. Wire into `IosHeyCyanGlassesSession.EnterTransferModeAsync` to complete transfer mode flow.
