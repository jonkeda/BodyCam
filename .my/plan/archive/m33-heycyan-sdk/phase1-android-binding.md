# M33 Phase 1 — Android SDK Binding & Core Session

Bind the vendor `glasses_sdk_20250723_v01.aar` as a .NET-for-Android binding
library, wrap it in a thin `HeyCyanSdkBridge`, and ship a working
`AndroidHeyCyanGlassesSession : IHeyCyanGlassesSession` that supports scan,
connect, disconnect, version, battery, time sync, media counts, and button
events. No camera/HTTP transfer (phase 2), no audio (phase 3), no
`IButtonInputProvider` wiring (phase 4), no gallery (phase 5), no iOS (phase 6),
no UI (phase 7).

**Depends on:** [overview.md](overview.md) (M33 architecture &
`IHeyCyanGlassesSession` contract).

> **Authoritative SDK API names:** see [sdk-api-reference.md](sdk-api-reference.md)
> for verified type/method names — `LargeDataHandler.GetInstance().GlassesControl(byte[], callback)`,
> `BleBaseControl` (scan/connect/pair), `BleOperateManager` (notify/operate),
> `ILargeDataResponse<T>`, `ICommandResponse`, action-type constants, and
> command byte sequences. Validate every SDK reference in this phase
> against that file.

**Reference material:**
- [`Alternative-HeyCyan-App-and-SDK/android/CyanBridge/app/libs/glasses_sdk_20250723_v01.aar`](../../../Alternative-HeyCyan-App-and-SDK/android/CyanBridge/app/libs/glasses_sdk_20250723_v01.aar)
  — the AAR we are binding.
- [`Alternative-HeyCyan-App-and-SDK/android/AGENTS.md`](../../../Alternative-HeyCyan-App-and-SDK/android/AGENTS.md)
  — `LargeDataHandler.glassesControl` payloads, notify-frame layout
  (`loadData[6]==0x08` IP, `0x09` P2P error), permissions.
- [`Alternative-HeyCyan-App-and-SDK/android/CyanBridge/app/src/main/java/com/fersaiyan/cyanbridge/MainActivity.kt`](../../../Alternative-HeyCyan-App-and-SDK/android/CyanBridge/app/src/main/java/com/fersaiyan/cyanbridge/MainActivity.kt)
  — working Kotlin reference for scan/connect, callback shapes
  (`(cmdType, resp) -> ...`), and notify-frame parsing.
- [`Alternative-HeyCyan-App-and-SDK/heycyan-core/`](../../../Alternative-HeyCyan-App-and-SDK/heycyan-core/)
  — modular Kotlin core libs (`core-ble`, `core-data`, `core-utils`)
  illustrating the SDK API surface.

---

## Wave 1: AAR Binding Library

Create `BodyCam.HeyCyan.Android.Bindings` — a .NET-for-Android **binding
library** project that produces a managed wrapper around the AAR.

### Project setup

```xml
<!-- src/BodyCam.HeyCyan.Android.Bindings/BodyCam.HeyCyan.Android.Bindings.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-android35.0</TargetFramework>
    <SupportedOSPlatformVersion>26</SupportedOSPlatformVersion>
    <IsBindingProject>true</IsBindingProject>
    <AndroidClassParser>class-parse</AndroidClassParser>
    <AndroidCodegenTarget>XAJavaInterop1</AndroidCodegenTarget>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <AndroidLibrary Include="Jars\glasses_sdk_20250723_v01.aar">
      <Bind>true</Bind>
    </AndroidLibrary>
    <TransformFile Include="Transforms\Metadata.xml" />
    <TransformFile Include="Transforms\EnumFields.xml" />
  </ItemGroup>
</Project>
```

Drop the AAR at `Jars/glasses_sdk_20250723_v01.aar` (copy from
`Alternative-HeyCyan-App-and-SDK/android/CyanBridge/app/libs/`). Add the
binding project as a `<ProjectReference>` from `BodyCam.csproj` guarded by
`Condition="$(TargetFramework.Contains('-android'))"`.

### Likely `Transforms/Metadata.xml` adjustments

The vendor AAR contains an `obfuscated.dll`-style flat package
(`com.iflytop.android.QCSDK` / `com.qiying.qcsdk.*`) with single-letter inner
classes. Expect to:

- Rename obfuscated inner classes to readable names where they are public
  API (e.g. `LargeDataHandler$a` → `LargeDataHandler.Response`).
- Hide everything not on the public API surface
  (`<remove-node path="//class[contains(@name,'$')]" />`-style with
  exclusions for the listener interfaces we need).
- Force `LargeDataHandler.getInstance()` to return the typed singleton
  (`<attr path="..." name="managedReturn">LargeDataHandler</attr>`).
- Map the Java callback functional interfaces to C# delegates where the
  default `class-parse` output is awkward (`Action<int, Response>` for
  `(cmdType, resp) -> ...`).

### Public API we **must** keep visible

| Java symbol | Used by | Notes |
|------|------|------|
| `Com.Oudmon.Ble.Base.Communication.LargeDataHandler.GetInstance()` | bridge | high-level command + parsed-response singleton |
| `LargeDataHandler.GlassesControl(byte[], ILargeDataResponse<GlassModelControlResponse>)` | bridge | the only command-send method — **callback is required** |
| `LargeDataHandler.AddOutDeviceListener(int type, ILargeDataResponse)` / `RemoveOutDeviceListener(int)` | bridge | high-level multiplexed notify (type `100` = device-notify channel: battery/IP/buttons; type `2` = transfer/download channel) |
| `Com.Oudmon.Ble.Base.Bluetooth.BleBaseControl.GetInstance()` | bridge | low-level BLE scan / `Connect(mac)` / `DisconnectDevice(mac)` / `CreateBond(...)` / `SetListener(IBleListener)` |
| `Com.Oudmon.Ble.Base.Bluetooth.BleOperateManager.GetInstance()` | bridge | connection lifecycle (`ConnectWithScan`, `ConnectDirectly`, `Disconnect`, `IsConnected`, `IsReady`), low-level `AddNotifyListener(int, ICommandResponse)` / `Execute(BaseRequest)` |
| `Com.Oudmon.Ble.Base.Communication.ILargeDataResponse<T>` (`ParseData(int cmdType, T resp)`) | bridge | high-level callback shape |
| `Com.Oudmon.Ble.Base.Bluetooth.IBleListener` | bridge | GATT lifecycle (`BleGattConnected`, `BleGattDisconnect`, `BleStatus`, `BleServiceDiscovered`, `BleCharacteristicChanged`) |
| `Com.Oudmon.Ble.Base.Bluetooth.DeviceManager.GetInstance()` | session | holds connected device MAC + name |
| Response POCOs (`DeviceInfoResponse`, `BatteryResponse`, `GlassModelControlResponse`, `GlassesDeviceNotifyRsp`, `SyncTimeResponse`, …) | bridge / session | parsed-data carriers |

### Verify

- [ ] Binding project builds with `dotnet build -f net9.0-android35.0`
  producing `BodyCam.HeyCyan.Android.Bindings.dll`
- [ ] No `BG8xxx` warnings remain (each is either fixed or explicitly
  silenced in `Transforms/Metadata.xml`)
- [ ] `LargeDataHandler.GetInstance().GlassesControl(byte[], callback)` resolves in
  IntelliSense (callback is `ILargeDataResponse<GlassModelControlResponse>`)
- [ ] `BleBaseControl.GetInstance()` and `BleOperateManager.GetInstance()`
  scan/connect APIs are reachable
- [ ] `obfuscated.dll`-style inner classes that we don't use are hidden so
  they don't pollute the public surface

---

## Wave 2: `HeyCyanSdkBridge` (Android-only)

Thin Android-only wrapper that turns SDK callbacks into `event`s and
`Task`-returning methods. Lives at `Platforms/Android/HeyCyan/`.

```csharp
namespace BodyCam.Platforms.Android.HeyCyan;

internal sealed class HeyCyanSdkBridge : IDisposable
{
    // Real SDK singletons — see sdk-api-reference.md Section A.
    private readonly LargeDataHandler _ldh = LargeDataHandler.GetInstance();
    private readonly BleBaseControl _ble = BleBaseControl.GetInstance();          // scan / connect / pair
    private readonly BleOperateManager _ops = BleOperateManager.GetInstance();    // notify / connection lifecycle

    public event EventHandler<HeyCyanScanResult>? DeviceDiscovered;
    public event EventHandler<HeyCyanConnectionState>? ConnectionStateChanged;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanRawNotify>? RawNotify; // loadData passthrough

    public Task StartScanAsync(TimeSpan timeout, CancellationToken ct);
    public Task StopScanAsync();
    public Task ConnectAsync(string macAddress, CancellationToken ct);
    public Task DisconnectAsync();

    /// <summary>
    /// Send a raw control frame and await the matching response
    /// (cmdType is matched by the SDK's request/response correlation).
    /// </summary>
    public Task<HeyCyanResponse> SendAsync(byte[] payload, CancellationToken ct);

    public void Dispose() { /* unhook listeners */ }
}
```

### Frame parsing rules (from `android/AGENTS.md` + `MainActivity.kt`)

```csharp
// Notify hook — installed once on connect.
private void OnNotify(byte[] loadData)
{
    if (loadData.Length < 7) return;
    var cmdType = loadData[0];

    if (cmdType == 0x02)
    {
        // Button event. SDK already does tap/double/long debounce.
        var gesture = loadData[6] switch
        {
            0x01 => HeyCyanButtonGesture.Tap,
            0x02 => HeyCyanButtonGesture.DoubleTap,
            0x03 => HeyCyanButtonGesture.LongPress,
            _    => (HeyCyanButtonGesture?)null,
        };
        if (gesture is { } g)
            ButtonPressed?.Invoke(this, new(g, DateTimeOffset.UtcNow));
        return;
    }

    // Transfer-mode IP/error frames are surfaced as RawNotify so phase 2 can
    // consume them without us caring about transfer here.
    //   loadData[6] == 0x08 → glasses Wi-Fi IP at [7..10]
    //   loadData[6] == 0x09 → P2P/Wi-Fi error (loadData[7] == 0xFF noisy)
    RawNotify?.Invoke(this, new(loadData));
}
```

### Common command payloads (phase 1 only)

| Command | Payload | Source |
|---|---|---|
| Get version | `glassesControl([0x01, 0x01, …])` | matches CyanBridge `getVersion()` |
| Get battery | `glassesControl([0x01, 0x02, …])` | CyanBridge `getBattery()` |
| Sync time | `glassesControl([0x03, …unix-le…])` | CyanBridge `syncTime()` |
| Get media counts | `glassesControl([0x04, 0x01, …])` | CyanBridge `getMediaCount()` |

> Exact bytes per command live in the CyanBridge sources — copy them
> verbatim during implementation; do not invent payloads.

### Verify

- [ ] `StartScanAsync` raises `DeviceDiscovered` for the manufacturer's
  known glasses name
- [ ] `ConnectAsync(mac)` transitions through `Connecting` → `Connected`
  and unsubscribes the scan listener
- [ ] Disposing the bridge unhooks every listener (no managed leak)
- [ ] `SendAsync` correlates response by `cmdType` and respects `ct`
  (cancellation throws `OperationCanceledException`)
- [ ] Button frames produce exactly one `ButtonPressed` per gesture (no
  duplicate from notify echo)
- [ ] All callbacks marshal to a known dispatcher — never raise events on
  the SDK's internal binder thread without trampolining

---

## Wave 3: `AndroidHeyCyanGlassesSession`

Cross-platform-facing implementation of `IHeyCyanGlassesSession`. Lives at
`Services/Glasses/HeyCyan/AndroidHeyCyanGlassesSession.cs` (compiled only
for `-android`).

```csharp
namespace BodyCam.Services.Glasses.HeyCyan;

internal sealed class AndroidHeyCyanGlassesSession : ViewModelBase, IHeyCyanGlassesSession
{
    private readonly HeyCyanSdkBridge _bridge;
    private readonly ILogger<AndroidHeyCyanGlassesSession> _log;
    private HeyCyanState _state = HeyCyanState.Disconnected;

    public HeyCyanState State
    {
        get => _state;
        private set { if (SetProperty(ref _state, value)) StateChanged?.Invoke(this, value); }
    }
    public HeyCyanDeviceInfo? Device { get; private set; }

    public event EventHandler<HeyCyanState>? StateChanged;
    public event EventHandler<HeyCyanBattery>? BatteryUpdated;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    public event EventHandler<byte[]>? AiPhotoReceived;

    public AndroidHeyCyanGlassesSession(HeyCyanSdkBridge bridge, ILogger<AndroidHeyCyanGlassesSession> log)
    {
        _bridge = bridge;
        _log = log;
        _bridge.ButtonPressed += (s, e) => ButtonPressed?.Invoke(this, e);
        _bridge.ConnectionStateChanged += OnConnectionStateChanged;
    }

    public async Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct) { /* … */ }
    public async Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct) { /* … */ }
    public async Task DisconnectAsync(CancellationToken ct) { /* … */ }

    public async Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct) { /* SendAsync + parse */ }
    public async Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct) { /* … */ }
    public async Task SyncTimeAsync(CancellationToken ct) { /* … */ }

    // Phase 2 placeholders — throw NotImplementedException for now.
    public Task TakePhotoAsync(CancellationToken ct) => throw new NotImplementedException("M33 Phase 2");
    public Task StartVideoAsync(CancellationToken ct) => throw new NotImplementedException("M33 Phase 2");
    public Task StopVideoAsync(CancellationToken ct) => throw new NotImplementedException("M33 Phase 2");
    public Task StartAudioAsync(CancellationToken ct) => throw new NotImplementedException("M33 Phase 5");
    public Task StopAudioAsync(CancellationToken ct) => throw new NotImplementedException("M33 Phase 5");
    public Task TakeAiPhotoAsync(CancellationToken ct) => throw new NotImplementedException("M33 Phase 2");
    public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
        => throw new NotImplementedException("M33 Phase 2");

    public async ValueTask DisposeAsync()
    {
        if (State is HeyCyanState.Connected or HeyCyanState.Connecting)
            await DisconnectAsync(CancellationToken.None);
        _bridge.Dispose();
    }
}
```

### Behavior contract

- `ScanAsync` debounces duplicate MAC results within the window.
- `ConnectAsync` is **not** re-entrant; concurrent calls throw
  `InvalidOperationException`.
- After `Connected`, fire-and-forget `GetVersionAsync` + `GetBatteryAsync` +
  `GetMediaCountAsync` to populate `Device`/raise `BatteryUpdated`/
  `MediaCountUpdated` so the UI lights up immediately.
- A glasses-initiated disconnect (BLE drop) transitions
  `Connected → Disconnected` and raises `StateChanged`. No exception.

### Verify

- [ ] `ScanAsync` returns deduplicated devices and respects timeout/cancel
- [ ] `ConnectAsync` throws on already-connected and on cancel
- [ ] After connect, `Device.Firmware`/`Device.MacAddress` are populated
- [ ] `BatteryUpdated` fires within ~1s of connect
- [ ] BLE drop transitions to `Disconnected` cleanly
- [ ] `DisposeAsync` is idempotent and cancels in-flight `SendAsync` calls

---

## Wave 4: DI + Manifest Permissions

### `MauiProgram.cs`

```csharp
#if ANDROID
builder.Services.AddSingleton<HeyCyanSdkBridge>();
builder.Services.AddSingleton<IHeyCyanGlassesSession, AndroidHeyCyanGlassesSession>();
#endif
```

`IHeyCyanGlassesSession` is **singleton** — there is one physical glasses
connection per app instance. Phase 7 will wrap it in
`HeyCyanGlassesDeviceManager`.

### `Platforms/Android/AndroidManifest.xml`

```xml
<uses-permission android:name="android.permission.BLUETOOTH_SCAN"
                 android:usesPermissionFlags="neverForLocation" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
<uses-permission android:name="android.permission.NEARBY_WIFI_DEVICES"
                 android:usesPermissionFlags="neverForLocation" />
<!-- Phase 1 only needs scan + connect. NEARBY_WIFI_DEVICES + ACCESS_FINE_LOCATION
     are added now so phase 2 doesn't need a re-prompt. -->
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
<uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />

<uses-feature android:name="android.hardware.bluetooth_le" android:required="false" />
```

### Runtime permission helper

Add `HeyCyanPermissions.RequestAsync()` that requests `BLUETOOTH_SCAN` +
`BLUETOOTH_CONNECT` (and on Android < 12, `ACCESS_FINE_LOCATION`). Call it
from `ScanAsync` before invoking the SDK; surface a typed
`HeyCyanPermissionException` if the user denies.

### Verify

- [ ] App resolves `IHeyCyanGlassesSession` via DI
- [ ] Permission prompt appears on first scan and not on subsequent scans
- [ ] Manifest passes Play-Console review against the SDK's documented
  permission set (`neverForLocation` flags present where applicable)

---

## Wave 5: Unit Tests with a Fake Bridge

Cross-platform tests in `BodyCam.Tests` so they run without an Android
emulator. Introduce `IHeyCyanSdkBridge` as the abstraction the session
talks to (the concrete `HeyCyanSdkBridge` in Wave 2 implements it on
Android-only). Tests inject `FakeHeyCyanSdkBridge`.

```csharp
public sealed class FakeHeyCyanSdkBridge : IHeyCyanSdkBridge
{
    public List<HeyCyanScanResult> ScriptedScan { get; } = new();
    public Func<byte[], HeyCyanResponse>? OnSend { get; set; }

    public event EventHandler<HeyCyanScanResult>? DeviceDiscovered;
    public event EventHandler<HeyCyanConnectionState>? ConnectionStateChanged;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanRawNotify>? RawNotify;

    public Task StartScanAsync(TimeSpan _, CancellationToken __) { /* push ScriptedScan */ }
    public void RaiseButton(HeyCyanButtonGesture g)
        => ButtonPressed?.Invoke(this, new(g, DateTimeOffset.UtcNow));
    public void RaiseDisconnect()
        => ConnectionStateChanged?.Invoke(this, HeyCyanConnectionState.Disconnected);
    // …
}
```

### Test coverage

```csharp
public class AndroidHeyCyanGlassesSessionTests
{
    [Fact] public async Task ScanAsync_returns_deduplicated_devices();
    [Fact] public async Task ConnectAsync_transitions_through_Connecting_to_Connected();
    [Fact] public async Task ConnectAsync_when_already_connected_throws();
    [Fact] public async Task ConnectAsync_cancellation_propagates();
    [Fact] public async Task BLE_drop_transitions_to_Disconnected_and_raises_StateChanged();
    [Fact] public async Task GetVersionAsync_parses_firmware_response();
    [Fact] public async Task GetBatteryAsync_parses_percentage_and_charging_flag();
    [Fact] public async Task SyncTimeAsync_sends_unix_LE_payload();
    [Fact] public async Task ButtonPressed_forwards_each_gesture_exactly_once();
    [Fact] public async Task DisposeAsync_is_idempotent();
}

public class HeyCyanFrameParserTests
{
    [Theory]
    [InlineData(new byte[] { 0x02, 0,0,0,0,0, 0x01 }, HeyCyanButtonGesture.Tap)]
    [InlineData(new byte[] { 0x02, 0,0,0,0,0, 0x02 }, HeyCyanButtonGesture.DoubleTap)]
    [InlineData(new byte[] { 0x02, 0,0,0,0,0, 0x03 }, HeyCyanButtonGesture.LongPress)]
    public void Button_frames_decoded_correctly(byte[] frame, HeyCyanButtonGesture expected);

    [Fact] public void Ip_frame_loadData6_eq_08_extracts_ipv4_from_bytes_7_to_10();
    [Fact] public void P2p_error_frame_loadData6_eq_09_value_FF_is_classified_noisy();
}
```

```powershell
# Run from repo root
dotnet test src/BodyCam.Tests --filter "FullyQualifiedName~HeyCyan" -c Debug
```

### Verify

- [ ] `BodyCam.Tests` builds without referencing any `-android` TFM
- [ ] All `HeyCyan*Tests` pass on the dev box (no glasses required)
- [ ] No test depends on real timing — all use a `TimeProvider`/awaitable
  hook on the fake bridge
- [ ] Frame-parser tests cover both button gestures and the
  `0x08`/`0x09` notify branches

---

## Phase 1 Exit Criteria (subset of overview.md)

- [ ] Android AAR bound; project compiles cleanly
- [ ] `IHeyCyanGlassesSession` (Android impl) registered in DI
- [ ] BLE scan/connect/disconnect proven on real glasses
- [ ] Battery + firmware + media counts populate `Device` after connect
- [ ] Time sync command round-trips (`SyncTimeAsync` returns without
  throwing and the glasses RTC is updated — verifiable via subsequent
  media-file timestamps in phase 2)
- [ ] Button tap/double/long events surface through
  `IHeyCyanGlassesSession.ButtonPressed` (raw — no `IButtonInputProvider`
  wiring yet, that is phase 4)
- [ ] Unit tests with `FakeHeyCyanSdkBridge` green in CI

Items deliberately deferred:
- Camera / `TakePhotoAsync` / `EnterTransferModeAsync` → **phase 2**
- BT-classic A2DP/HFP audio routing → **phase 3**
- `IButtonInputProvider` adapter into `ButtonInputManager` → **phase 4**
- Recorded `.opus` / `.mp4` retrieval and gallery → **phase 5**
- iOS `QCSDK.framework` binding → **phase 6**
- `HeyCyanGlassesDeviceManager` + connection UI → **phase 7**
