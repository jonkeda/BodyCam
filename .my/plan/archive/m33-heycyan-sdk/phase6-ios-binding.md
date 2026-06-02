# M33 Phase 6 — iOS QCSDK.framework Binding

Bind the vendor `QCSDK.framework` (Objective-C) as a .NET for iOS native
framework binding, then implement an iOS `IHeyCyanGlassesSession` that mirrors
the Android session shipped in Phase 1. Reuse the cross-platform providers
from Phases 2-5 unchanged — iOS only needs the binding library, the session,
and an iOS `HotspotHttpClient` wrapping `NEHotspotConfiguration`.

**Depends on:**
- M33 Phase 1 — `IHeyCyanGlassesSession` cross-platform contract and Android
  reference implementation (parity target).
- M33 Phases 2-5 — cross-platform `HeyCyanCameraProvider`,
  `HeyCyanAudioInputProvider` / `HeyCyanAudioOutputProvider`,
  `HeyCyanButtonProvider`, `HeyCyanMediaTransfer` (consume the iOS session
  through the shared interface).

**Reference material:**
- `Alternative-HeyCyan-App-and-SDK/QCSDK.framework/` — fat framework binary +
  public headers (`QCSDK.h`, `QCSDKManager.h`, `QCSDKCmdCreator.h`,
  `OdmBleConstants.h`, `QCDFU_Utils.h`, `QCVersionHelper.h`,
  `QCVolumeInfoModel.h`).
- `Alternative-HeyCyan-App-and-SDK/ios/QCSDK.framework/` — duplicate copy
  shipped alongside the demo.
- `Alternative-HeyCyan-App-and-SDK/ios/QCSDKDemo/` — Objective-C demo
  (BLE scan, connect, capture, AI photo, hotspot join, IP discovery).
- `Alternative-HeyCyan-App-and-SDK/examples/legacy/HeyCyanSwift/` — Swift
  demo using the same APIs.
- `Alternative-HeyCyan-App-and-SDK/WIFI_TRANSFER_ARCHITECTURE.md` —
  hotspot/HTTP transfer protocol (`NEHotspotConfiguration` join, fallback
  password `123456789`, IP discovery probe order).

---

## Wave 1: `BodyCam.HeyCyan.iOS.Bindings` Project

Create a .NET for iOS Objective-C framework binding library.

### Project Layout

```
src/BodyCam.HeyCyan.iOS.Bindings/
├── BodyCam.HeyCyan.iOS.Bindings.csproj   (net9.0-ios, IsBindingProject=true)
├── ApiDefinition.cs
├── StructsAndEnums.cs
├── NativeReferences/
│   └── QCSDK.framework/                  (fat: arm64 device + arm64 sim slice)
└── README.md
```

### `.csproj` Skeleton

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-ios</TargetFramework>
    <IsBindingProject>true</IsBindingProject>
    <NoBindingEmbedding>false</NoBindingEmbedding>
    <Nullable>enable</Nullable>
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

### Bootstrap with `objective-sharpie`

```pwsh
sharpie bind `
  --output=tmp `
  --namespace=BodyCam.HeyCyan.iOS.Bindings `
  --sdk=iphoneos18.0 `
  --scope=Alternative-HeyCyan-App-and-SDK/QCSDK.framework/Headers `
  Alternative-HeyCyan-App-and-SDK/QCSDK.framework/Headers/QCSDK.h
```

Move the generated `ApiDefinition.cs` and `StructsAndEnums.cs` into the
project, then apply manual fixups (see below). Treat sharpie output as a
starting point only — every callback signature, nullability, and protocol
member needs review.

### `StructsAndEnums.cs` (excerpt)

```csharp
namespace BodyCam.HeyCyan.iOS.Bindings;

[Native]
public enum BleConnectState : long
{
    Off = 0,
    On = 1,
    Fail = 2,
}

[Native]
public enum QCOperatorDeviceMode : long
{
    Idle = 0,
    Photo,
    Video,
    VideoStop,
    Audio,
    AudioStop,
    AiPhoto,
    Transfer,
    // …mirror OdmBleConstants.h exactly; values come from the header.
}

[Native]
public enum QGAISpeakMode : long
{
    // values from header
}

[Native]
public enum OdmDfuFirmwareType : long { /* … */ }

[Native]
public enum OdmDfuBandType : long { /* … */ }

[Native]
public enum OdmDfuDeviceProcessStatus : long { /* … */ }
```

### `ApiDefinition.cs` (excerpt)

```csharp
using System;
using CoreBluetooth;
using Foundation;
using ObjCRuntime;

namespace BodyCam.HeyCyan.iOS.Bindings;

// QCSDKManagerDelegate — all members are @optional in the header.
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

    [Export("didUpdateWiFiUpgradeProgressWithDownload:upgrade1:upgrade2:")]
    void DidUpdateWifiUpgradeProgress(nint download, nint upgrade1, nint upgrade2);

    [Export("didReceiveWiFiUpgradeResult:")]
    void DidReceiveWifiUpgradeResult(bool success);
}

[BaseType(typeof(NSObject))]
interface QCSDKManager
{
    [Static, Export("shareInstance")]
    QCSDKManager SharedInstance { get; }

    [Wrap("WeakDelegate"), NullAllowed]
    QCSDKManagerDelegate Delegate { get; set; }

    [NullAllowed, Export("delegate", ArgumentSemantic.Weak)]
    NSObject WeakDelegate { get; set; }

    [Export("debug")]
    bool Debug { get; set; }

    [Export("addPeripheral:finished:")]
    void AddPeripheral(CBPeripheral peripheral, Action<bool> finished);

    [Export("removePeripheral:")]
    void RemovePeripheral(CBPeripheral peripheral);

    [Export("removeAllPeripheral")]
    void RemoveAllPeripherals();
}

// QCCentralManager — singleton wrapper around CBCentralManager that
// surfaces the QCSDK BLE state machine and notify-frame parsing.
[BaseType(typeof(NSObject))]
interface QCCentralManager
{
    [Static, Export("shareInstance")]
    QCCentralManager SharedInstance { get; }

    [Export("scanForPeripheralsWithTimeout:")]
    void ScanForPeripherals(double timeoutSeconds);

    [Export("stopScan")]
    void StopScan();

    [Export("connectPeripheral:")]
    void ConnectPeripheral(CBPeripheral peripheral);

    [Export("disconnectPeripheral:")]
    void DisconnectPeripheral(CBPeripheral peripheral);

    // Observe `connectState` via KVO (`OdmBleConnectState` constant).
    [Export("connectState")]
    BleConnectState ConnectState { get; }
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

    [Static, Export("getDeviceMedia:fail:")]
    void GetDeviceMedia(Action<nint, nint, nint, nint> success, Action fail);

    [Static, Export("getDeviceBattery:fail:")]
    void GetDeviceBattery(Action<nint, bool> success, Action fail);

    [Static, Export("getDeviceVersionInfoSuccess:fail:")]
    void GetDeviceVersionInfo(Action<NSString, NSString, NSString, NSString> success, Action fail);

    [Static, Export("setupDeviceDateTime:")]
    void SetupDeviceDateTime(Action<bool, NSError> finished);

    [Static, Export("isPeripheralFreeNow")]
    bool IsPeripheralFreeNow { get; }
}
```

> Manual fixups required after `sharpie`:
> - Replace `nint`/`NSInteger` enum-typed parameters with the strong enum types.
> - Mark all `void (^)(NSError * _Nullable)` callback args with
>   `[NullAllowed]` on the `NSError` parameter.
> - Convert `id _Nullable result` opaque callbacks to `NSObject` with
>   `[NullAllowed]`.
> - Split `@protocol` definitions into `[Protocol, Model] BaseType(NSObject)`.

### Verify
- [ ] `sharpie` output committed under `tmp/` (gitignored) for diffing
- [ ] `BodyCam.HeyCyan.iOS.Bindings.csproj` builds clean for
      `net9.0-ios` (device + simulator)
- [ ] `dotnet build -f net9.0-ios -r iossimulator-arm64` smoke succeeds
- [ ] No `[Verify(...)]` attributes left over from sharpie

---

## Wave 2: `IosHeyCyanGlassesSession`

Mirror `AndroidHeyCyanGlassesSession` from Phase 1 — same `IHeyCyanGlassesSession`
contract, same events, same state machine.

### Skeleton

```csharp
// Platforms/iOS/HeyCyan/IosHeyCyanGlassesSession.cs
using BodyCam.HeyCyan.iOS.Bindings;
using CoreBluetooth;
using Foundation;

namespace BodyCam.Services.Glasses.HeyCyan.Platforms.iOS;

internal sealed class IosHeyCyanGlassesSession : NSObject, IHeyCyanGlassesSession
{
    private readonly QCSDKManagerDelegateProxy _qcDelegate;
    private readonly CBCentralManager _cbCentral;
    private readonly QCCentralManager _qc;
    private readonly HotspotHttpClient _hotspot;
    private CBPeripheral? _peripheral;

    public HeyCyanState State { get; private set; } = HeyCyanState.Disconnected;
    public HeyCyanDeviceInfo? Device { get; private set; }

    public event EventHandler<HeyCyanState>? StateChanged;
    public event EventHandler<HeyCyanBattery>? BatteryUpdated;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    public event EventHandler<byte[]>? AiPhotoReceived;

    public IosHeyCyanGlassesSession(HotspotHttpClient hotspot)
    {
        _hotspot = hotspot;
        _cbCentral = new CBCentralManager(null, null);
        _qc = QCCentralManager.SharedInstance;
        _qcDelegate = new QCSDKManagerDelegateProxy(this);
        QCSDKManager.SharedInstance.Delegate = _qcDelegate;
    }

    public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct) =>
        // Drive `_cbCentral.ScanForPeripherals` filtered to QCSDKSERVERUUID1/2,
        // collect (Name, Identifier, RSSI), stop on timeout or `ct`.
        throw new NotImplementedException();

    public Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct) =>
        // Resolve CBPeripheral by identifier → QCCentralManager.ConnectPeripheral
        // → wait for KVO `connectState == On` → QCSDKManager.AddPeripheral.
        throw new NotImplementedException();

    public Task TakePhotoAsync(CancellationToken ct) =>
        InvokeCmd(success => QCSDKCmdCreator.SetDeviceMode(QCOperatorDeviceMode.Photo, success, _ => { }), ct);

    public Task TakeAiPhotoAsync(CancellationToken ct) =>
        InvokeCmd(success => QCSDKCmdCreator.SetDeviceMode(QCOperatorDeviceMode.AiPhoto, success, _ => { }), ct);

    public async Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
    {
        var (ssid, password) = await OpenHotspotAsync(ct).ConfigureAwait(false);
        await _hotspot.JoinAsync(ssid, password, ct).ConfigureAwait(false);
        var baseUrl = await _hotspot.DiscoverGlassesIpAsync(ct).ConfigureAwait(false);
        var files = await _hotspot.GetMediaConfigAsync(baseUrl, ct).ConfigureAwait(false);
        SetState(HeyCyanState.TransferMode);
        return new HeyCyanTransferSession(baseUrl, files);
    }

    private Task<(string ssid, string password)> OpenHotspotAsync(CancellationToken ct) =>
        // QCSDKCmdCreator.OpenWifi(Transfer, (s, p) => { … }, code => { … })
        throw new NotImplementedException();

    private void SetState(HeyCyanState s) { State = s; StateChanged?.Invoke(this, s); }

    public ValueTask DisposeAsync()
    {
        QCSDKManager.SharedInstance.Delegate = null;
        _qc.RemoveAllPeripherals();
        return default;
    }

    // Translates Obj-C delegate callbacks into managed events.
    private sealed class QCSDKManagerDelegateProxy : QCSDKManagerDelegate
    {
        private readonly IosHeyCyanGlassesSession _owner;
        public QCSDKManagerDelegateProxy(IosHeyCyanGlassesSession owner) { _owner = owner; }

        public override void DidUpdateBatteryLevel(nint battery, bool charging) =>
            _owner.BatteryUpdated?.Invoke(_owner, new HeyCyanBattery((int)battery, charging));

        public override void DidUpdateMedia(nint photo, nint video, nint audio, nint _) =>
            _owner.MediaCountUpdated?.Invoke(_owner, new HeyCyanMediaCount((int)photo, (int)video, (int)audio));

        public override void DidReceiveAiChatImageData(NSData data) =>
            _owner.AiPhotoReceived?.Invoke(_owner, data.ToArray());
    }
}
```

Button events arrive on iOS as a separate notify-frame callback (the Obj-C
demo registers an `NSNotificationCenter` observer for `OdmNotifyD2P`). Parse
the same `cmdType=2` frame layout used by the Android session in Phase 1 and
raise `ButtonPressed` with the recognised gesture.

### Verify
- [ ] Session implements every `IHeyCyanGlassesSession` member
- [ ] BLE scan filters on `QCSDKSERVERUUID1` / `QCSDKSERVERUUID2`
- [ ] Connect transitions raise `StateChanged` exactly once per state
- [ ] Battery / media-count / AI-photo callbacks raise managed events
- [ ] Button frame parser shares fixtures with Android Phase 1 tests
- [ ] `DisposeAsync` clears the QCSDK delegate and removes peripherals

---

## Wave 3: `HotspotHttpClient` (iOS)

Wraps `NEHotspotConfigurationManager` and the IP-discovery probe loop from
`QCSDKDemo`'s `discoverGlassesIP`.

```csharp
// Platforms/iOS/HeyCyan/HotspotHttpClient.cs
using NetworkExtension;

internal sealed class HotspotHttpClient
{
    private static readonly string[] CandidateIps =
    {
        "192.168.43.1", "192.168.4.1", "192.168.1.1", "192.168.0.1", "10.0.0.1",
    };

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(800);
    private const string FallbackPassword = "123456789";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task JoinAsync(string ssid, string? password, CancellationToken ct)
    {
        var config = new NEHotspotConfiguration(ssid, password ?? FallbackPassword, isWep: false)
        {
            JoinOnce = true,
        };
        var tcs = new TaskCompletionSource<bool>();
        NEHotspotConfigurationManager.SharedManager.ApplyConfiguration(config, err =>
        {
            if (err is null) tcs.TrySetResult(true);
            else tcs.TrySetException(new IOException(err.LocalizedDescription));
        });
        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task<string> DiscoverGlassesIpAsync(CancellationToken ct)
    {
        foreach (var ip in CandidateIps)
        {
            using var probe = new CancellationTokenSource(ProbeTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, probe.Token);
            try
            {
                var resp = await _http.GetAsync($"http://{ip}/files/media.config", linked.Token)
                    .ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return $"http://{ip}";
            }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }
        throw new IOException("Glasses IP not found on any known subnet.");
    }

    public async Task<IReadOnlyList<string>> GetMediaConfigAsync(string baseUrl, CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"{baseUrl}/files/media.config", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return MediaConfigParser.ParseFileNames(text); // shared with Android
    }
}
```

### Verify
- [ ] Hotspot join honours `joinOnce = YES` (no Wi-Fi profile leaks)
- [ ] Fallback password used when `OpenWifi` returns `nil` for password
- [ ] IP discovery probes the exact list (43.1 → 4.1 → 1.1 → 0.1 → 10.0.0.1)
- [ ] Per-probe timeout ≤ 1s, total bounded by `CancellationToken`
- [ ] `GetMediaConfigAsync` delegates parsing to the shared parser used by
      Android (no duplicate parser)
- [ ] Disposes the configuration after exiting transfer mode (`RemoveConfiguration`)

---

## Wave 4: Info.plist & Entitlements

`NEHotspotConfiguration` requires the **Hotspot Configuration** capability.
Without it, `applyConfiguration` returns error code 8 silently.

### `Platforms/iOS/Entitlements.plist`

```xml
<key>com.apple.developer.networking.HotspotConfiguration</key>
<true/>
```

### `Platforms/iOS/Info.plist`

```xml
<key>NSBluetoothAlwaysUsageDescription</key>
<string>BodyCam connects to HeyCyan smart glasses over Bluetooth Low Energy.</string>

<key>NSLocalNetworkUsageDescription</key>
<string>BodyCam joins the glasses' Wi-Fi hotspot to download captured photos and audio.</string>

<key>NSCameraUsageDescription</key>
<string>BodyCam captures photos when no glasses are connected.</string>

<key>NSMicrophoneUsageDescription</key>
<string>BodyCam records audio for live conversations and dictation.</string>

<!-- Required so iOS reports the joined SSID after applyConfiguration -->
<key>NSLocationWhenInUseUsageDescription</key>
<string>BodyCam reads the current Wi-Fi SSID to confirm the glasses hotspot is active.</string>
```

### Verify
- [ ] Entitlement present and provisioning profile signs it
- [ ] All four usage strings copied into the MAUI head's `Info.plist`
- [ ] `NEHotspotConfigurationManager.ApplyConfiguration` returns `nil` error
      on a paired build (not error 8 / `unauthorized`)
- [ ] First launch shows the BLE permission prompt before the scan starts

---

## Wave 5: DI Registration & Parity Tests

### `MauiProgram.cs` (iOS conditional)

```csharp
#if IOS
builder.Services.AddSingleton<HotspotHttpClient>();
builder.Services.AddSingleton<IHeyCyanGlassesSession,
    BodyCam.Services.Glasses.HeyCyan.Platforms.iOS.IosHeyCyanGlassesSession>();
#endif
```

The Phase 2-5 providers (`HeyCyanCameraProvider`,
`HeyCyanAudioInputProvider`, `HeyCyanAudioOutputProvider`,
`HeyCyanButtonProvider`, `HeyCyanMediaTransfer`) are **already registered
cross-platform** and resolve `IHeyCyanGlassesSession` by interface — no iOS
fork needed.

### Parity Test Harness

```csharp
// BodyCam.IntegrationTests/Glasses/HeyCyanSessionParityTests.cs
public sealed class HeyCyanSessionParityTests
{
    public static IEnumerable<object[]> Sessions =>
        new[]
        {
#if ANDROID
            new object[] { typeof(AndroidHeyCyanGlassesSession) },
#elif IOS
            new object[] { typeof(IosHeyCyanGlassesSession) },
#endif
        };

    [Theory, MemberData(nameof(Sessions))]
    public void Implements_full_contract(Type t) =>
        typeof(IHeyCyanGlassesSession).IsAssignableFrom(t).Should().BeTrue();

    [Theory, MemberData(nameof(Sessions))]
    public async Task Scan_then_connect_then_disconnect_round_trip(Type t)
    {
        // Same script the Android Phase 1 tests use, against the resolved
        // platform session. Real-device test (skipped without
        // BODYCAM_HEYCYAN_REAL_DEVICE=1).
    }
}
```

### Verify (iOS exit criteria — parity with Android Phase 1)
- [ ] `dotnet build -f net9.0-ios` succeeds with no binding warnings
- [ ] BLE scan finds real glasses on iPhone hardware
- [ ] Connect → `StateChanged(Connected)` within 5s
- [ ] `GetVersionAsync`, `GetBatteryAsync`, `SyncTimeAsync` all return
- [ ] `TakePhotoAsync` increments the photo counter via `MediaCountUpdated`
- [ ] `TakeAiPhotoAsync` raises `AiPhotoReceived` with non-empty bytes
- [ ] `EnterTransferModeAsync` joins the hotspot and returns a working
      base URL (verified by HTTP `GET /files/media.config`)
- [ ] `DisconnectAsync` returns the iPhone to its previous Wi-Fi network
- [ ] Button gestures (tap / double / long) raise `ButtonPressed` with the
      same enum values the Android tests assert
- [ ] All Phase 2-5 cross-platform providers run unmodified against
      `IosHeyCyanGlassesSession`
- [ ] Same fixture data drives `HeyCyanSessionParityTests` on both platforms
