# M33 Phase 6 — Wave 1: `BodyCam.HeyCyan.iOS.Bindings` Project

## Goal

Stand up a `net9.0-ios` Objective-C framework binding library that wraps the
vendor `QCSDK.framework` (BLE control, command creator, delegate callbacks)
so the rest of BodyCam can consume strongly-typed managed APIs. Use
`objective-sharpie` to bootstrap, then hand-fix nullability, enum
parameters, and protocol shapes.

**Parent phase:** [`../phase6-ios-binding.md`](../phase6-ios-binding.md)
**Next:** [`wave2-ios-glasses-session.md`](wave2-ios-glasses-session.md)

## Steps

1. **Vendor the framework slice.** Copy
   `Alternative-HeyCyan-App-and-SDK/QCSDK.framework/` into
   `src/BodyCam.HeyCyan.iOS.Bindings/NativeReferences/QCSDK.framework/`.
   Verify `lipo -info QCSDK` reports both `arm64` (device) and the
   `arm64` simulator slice; if only the device slice is present, regenerate
   from the demo project's Xcode build before proceeding (the bound DLL
   cannot be linked into the simulator otherwise).

2. **Create the project.** Add `src/BodyCam.HeyCyan.iOS.Bindings/BodyCam.HeyCyan.iOS.Bindings.csproj`:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net9.0-ios</TargetFramework>
       <IsBindingProject>true</IsBindingProject>
       <NoBindingEmbedding>false</NoBindingEmbedding>
       <Nullable>enable</Nullable>
       <RootNamespace>BodyCam.HeyCyan.iOS.Bindings</RootNamespace>
     </PropertyGroup>

     <ItemGroup>
       <NativeReference Include="NativeReferences/QCSDK.framework">
         <Kind>Framework</Kind>
         <Frameworks>CoreBluetooth UIKit Foundation</Frameworks>
         <SmartLink>True</SmartLink>
         <ForceLoad>True</ForceLoad>
         <LinkerFlags>-ObjC</LinkerFlags>
       </NativeReference>
     </ItemGroup>
   </Project>
   ```

   `ForceLoad=True` and `-ObjC` are required because `QCSDKManager` registers
   its categories at load time; without them, selectors silently no-op.

3. **Add the `LinkWith` attribute.** Create `Properties/LinkWith.cs` so the
   linker keeps the framework symbols even in `Release` builds:

   ```csharp
   using ObjCRuntime;

   [assembly: LinkWith(
       "QCSDK.framework/QCSDK",
       LinkTarget.ArmV7 | LinkTarget.Arm64 | LinkTarget.Simulator64,
       ForceLoad = true,
       Frameworks = "CoreBluetooth UIKit Foundation",
       SmartLink = true,
       IsCxx = false)]
   ```

4. **Run objective-sharpie against the public headers.** From the repo root
   (macOS host required):

   ```pwsh
   sharpie bind `
     --output=tmp/sharpie `
     --namespace=BodyCam.HeyCyan.iOS.Bindings `
     --sdk=iphoneos18.0 `
     --scope=Alternative-HeyCyan-App-and-SDK/QCSDK.framework/Headers `
     Alternative-HeyCyan-App-and-SDK/QCSDK.framework/Headers/QCSDK.h
   ```

   The umbrella `QCSDK.h` re-exports `QCSDKManager.h`, `QCSDKCmdCreator.h`,
   `OdmBleConstants.h`, `QCDFU_Utils.h`, `QCVersionHelper.h`, and
   `QCVolumeInfoModel.h`, so a single sharpie pass covers the full surface.

5. **Move and clean the generated files.** Copy
   `tmp/sharpie/ApiDefinition.cs` and `tmp/sharpie/StructsAndEnums.cs` into
   the project root. Then strip every `[Verify(...)]` attribute — these are
   sharpie review markers, not real metadata, and CI must build clean
   without them.

6. **Hand-fix `StructsAndEnums.cs`.** Mirror `OdmBleConstants.h` exactly —
   sharpie often types raw `NSInteger` enums as `nint`. Re-declare them as
   `[Native]` enums:

   ```csharp
   namespace BodyCam.HeyCyan.iOS.Bindings;

   [Native] public enum BleConnectState : long { Off = 0, On = 1, Fail = 2 }

   [Native]
   public enum QCOperatorDeviceMode : long
   {
       Idle = 0, Photo, Video, VideoStop, Audio, AudioStop, AiPhoto, Transfer,
       // …copy remaining values from OdmBleConstants.h verbatim.
   }

   [Native] public enum QGAISpeakMode : long { /* from header */ }
   [Native] public enum OdmDfuFirmwareType : long { /* from header */ }
   [Native] public enum OdmDfuBandType : long { /* from header */ }
   [Native] public enum OdmDfuDeviceProcessStatus : long { /* from header */ }
   ```

7. **Hand-fix `ApiDefinition.cs`.** Replace `nint` enum parameters with
   the strong types, split protocols into `[Protocol, Model]`, and mark
   `NSError*` callback args `[NullAllowed]`. Key types to expose:

   ```csharp
   [Protocol, Model]
   [BaseType(typeof(NSObject))]
   interface QCSDKManagerDelegate
   {
       [Export("didUpdateBatteryLevel:charging:")]
       void DidUpdateBatteryLevel(nint battery, bool charging);

       [Export("didUpdateMediaWithPhotoCount:videoCount:audioCount:type:")]
       void DidUpdateMedia(nint photoCount, nint videoCount, nint audioCount, nint type);

       [Export("didReceiveAIChatImageData:")]
       void DidReceiveAiChatImageData(NSData imageData);
   }

   [BaseType(typeof(NSObject))]
   interface QCSDKManager
   {
       [Static, Export("shareInstance")] QCSDKManager SharedInstance { get; }
       [Wrap("WeakDelegate"), NullAllowed] QCSDKManagerDelegate Delegate { get; set; }
       [NullAllowed, Export("delegate", ArgumentSemantic.Weak)] NSObject WeakDelegate { get; set; }
       [Export("addPeripheral:finished:")] void AddPeripheral(CBPeripheral peripheral, Action<bool> finished);
       [Export("removeAllPeripheral")] void RemoveAllPeripherals();
   }

   [BaseType(typeof(NSObject))]
   interface QCCentralManager
   {
       [Static, Export("shareInstance")] QCCentralManager SharedInstance { get; }
       [Export("scanForPeripheralsWithTimeout:")] void ScanForPeripherals(double timeoutSeconds);
       [Export("stopScan")] void StopScan();
       [Export("connectPeripheral:")] void ConnectPeripheral(CBPeripheral peripheral);
       [Export("disconnectPeripheral:")] void DisconnectPeripheral(CBPeripheral peripheral);
       [Export("connectState")] BleConnectState ConnectState { get; }
   }

   [BaseType(typeof(NSObject))]
   interface QCSDKCmdCreator
   {
       [Static, Export("setDeviceMode:success:fail:")]
       void SetDeviceMode(QCOperatorDeviceMode mode, Action success, Action<nint> fail);

       [Static, Export("openWifiWithMode:success:fail:")]
       void OpenWifi(QCOperatorDeviceMode mode, Action<NSString, NSString> success, Action<nint> fail);

       [Static, Export("getDeviceWifiIPSuccess:failed:")]
       void GetDeviceWifiIp([NullAllowed] Action<NSString> success, [NullAllowed] Action fail);

       [Static, Export("getDeviceBattery:fail:")]
       void GetDeviceBattery(Action<nint, bool> success, Action fail);
   }
   ```

8. **Add to the solution.** `dotnet sln BodyCam.sln add src/BodyCam.HeyCyan.iOS.Bindings/BodyCam.HeyCyan.iOS.Bindings.csproj`,
   then reference it from `src/BodyCam/BodyCam.csproj` inside an
   `<ItemGroup Condition="$(TargetFramework.Contains('-ios'))">` block so
   non-iOS heads do not pull the binding.

9. **Build both RIDs.**

   ```pwsh
   dotnet build src/BodyCam.HeyCyan.iOS.Bindings -f net9.0-ios
   dotnet build src/BodyCam.HeyCyan.iOS.Bindings -f net9.0-ios -r iossimulator-arm64
   ```

   Both must succeed before [`wave2`](wave2-ios-glasses-session.md) starts
   consuming these types.

## Verify

- [ ] `Alternative-HeyCyan-App-and-SDK/QCSDK.framework/` copied into
      `src/BodyCam.HeyCyan.iOS.Bindings/NativeReferences/` with both arm64
      device and arm64 simulator slices
- [ ] `objective-sharpie` output committed under `tmp/sharpie/` (gitignored)
      and diffable
- [ ] No `[Verify(...)]` attributes remain in `ApiDefinition.cs` /
      `StructsAndEnums.cs`
- [ ] `LinkWith` attribute present with `ForceLoad=true` and `-ObjC` flag
- [ ] `[Native]` enums match `OdmBleConstants.h` values byte-for-byte
- [ ] `dotnet build -f net9.0-ios` succeeds for device + simulator
- [ ] `BodyCam.HeyCyan.iOS.Bindings.csproj` referenced from `BodyCam.csproj`
      under an iOS-only condition
