# BodyCam.HeyCyan.iOS.Bindings

.NET 10 iOS binding library for the **QCSDK.framework** — HeyCyan smart glasses SDK (Objective-C).

## Framework Source

Vendor framework located at `NativeReferences/QCSDK.framework/`, copied from
`Alternative-HeyCyan-App-and-SDK/QCSDK.framework/`.

## Architecture

The framework should contain both:
- `arm64` (device)
- `arm64` simulator slice

Verify with: `lipo -info NativeReferences/QCSDK.framework/QCSDK` (on macOS)

## Bound APIs

- **QCSDKManager** — main SDK singleton, peripheral management, delegate callbacks
- **QCSDKCmdCreator** — command creator for device operations (photo, video, audio, transfer, DFU)
- **QCVersionHelper** — framework version query
- **QCVolumeInfoModel** — volume configuration model
- **Enums** — `BleConnectState`, `QCOperatorDeviceMode`, `QGAISpeakMode`, `QCVolumeMode`, DFU enums

## Build Requirements

- macOS host (for iOS SDK and toolchain)
- .NET 10 SDK with iOS workload
- iOS 14.0+ deployment target (framework requirement)

## Usage

This binding library is referenced by `BodyCam.csproj` under an iOS-only condition:

```xml
<ItemGroup Condition="$(TargetFramework.Contains('-ios'))">
  <ProjectReference Include="..\BodyCam.HeyCyan.iOS.Bindings\BodyCam.HeyCyan.iOS.Bindings.csproj" />
</ItemGroup>
```

The iOS-specific `IosHeyCyanGlassesSession` implementation consumes these bindings
to implement `IHeyCyanGlassesSession` (M33 Phase 6 Wave 2).

## References

- M33 Phase 6: [`../../.my/plan/m33-heycyan-sdk/phase6-ios-binding/`](../../.my/plan/m33-heycyan-sdk/phase6-ios-binding/)
- SDK API Reference: [`../../.my/plan/m33-heycyan-sdk/sdk-api-reference.md`](../../.my/plan/m33-heycyan-sdk/sdk-api-reference.md)
