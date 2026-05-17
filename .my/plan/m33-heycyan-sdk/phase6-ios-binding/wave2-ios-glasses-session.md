# M33 Phase 6 — Wave 2: `IosHeyCyanGlassesSession`

## Goal

Implement the iOS half of `IHeyCyanGlassesSession` (defined in M33 Phase 1)
by driving `QCCentralManager`, `QCSDKManager`, and `QCSDKCmdCreator` from the
[`wave1`](wave1-bindings-project.md) bindings. The session must reach exact
behavioural parity with `AndroidHeyCyanGlassesSession`: same state machine,
same events, same gesture enum values, same fixture inputs.

**Parent phase:** [`../phase6-ios-binding.md`](../phase6-ios-binding.md)
**Prev:** [`wave1-bindings-project.md`](wave1-bindings-project.md)
**Next:** [`wave3-hotspot-http-client.md`](wave3-hotspot-http-client.md)

## Steps

1. **Create the platform folder.** All iOS-specific code lives under
   `src/BodyCam/Platforms/iOS/HeyCyan/`. Add an empty README pointing back
   to this phase, then create:

   - `IosHeyCyanGlassesSession.cs`
   - `QCSDKManagerDelegateProxy.cs`
   - `IosBleScanner.cs` (helper around `CBCentralManager`)
   - `NotifyFrameParser.cs` (shared with Android — see step 6)

2. **Sketch the class.** Subclass `NSObject` so the QCSDK delegate model
   wrapper has a valid Obj-C base. Inject `HotspotHttpClient` (Wave 3) but
   construct `CBCentralManager` and `QCCentralManager.SharedInstance`
   directly — they are process singletons.

   ```csharp
   using BodyCam.HeyCyan.iOS.Bindings;
   using BodyCam.Services.Glasses.HeyCyan;
   using CoreBluetooth;
   using Foundation;

   namespace BodyCam.Platforms.iOS.HeyCyan;

   internal sealed class IosHeyCyanGlassesSession : NSObject, IHeyCyanGlassesSession
   {
       private readonly HotspotHttpClient _hotspot;
       private readonly QCSDKManagerDelegateProxy _qcDelegate;
       private readonly CBCentralManager _cbCentral;
       private readonly QCCentralManager _qc;
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
   }
   ```

3. **Implement `ScanAsync`.** Filter on the QCSDK service UUIDs that the
   demo's `QCScanView.m` advertises (`QCSDKSERVERUUID1` /
   `QCSDKSERVERUUID2`); collect `(Name, Identifier, RSSI)` tuples and stop
   on the first of timeout, `ct`, or N unique results.

   ```csharp
   public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct)
   {
       SetState(HeyCyanState.Scanning);
       var found = new Dictionary<NSUuid, HeyCyanDeviceInfo>();
       var tcs = new TaskCompletionSource<IReadOnlyList<HeyCyanDeviceInfo>>();

       _cbCentral.DiscoveredPeripheral += (_, e) =>
       {
           found[e.Peripheral.Identifier] = new HeyCyanDeviceInfo(
               e.Peripheral.Name ?? "(unknown)",
               e.Peripheral.Identifier.AsString(),
               e.RSSI?.Int32Value ?? 0);
       };

       _cbCentral.ScanForPeripherals(new[] { QCSDKServerUuid1, QCSDKServerUuid2 });
       Task.Delay(timeout, ct).ContinueWith(_ =>
       {
           _cbCentral.StopScan();
           tcs.TrySetResult(found.Values.ToList());
           SetState(HeyCyanState.Connected is var _ ? HeyCyanState.Disconnected : HeyCyanState.Disconnected);
       }, TaskScheduler.Default);
       return tcs.Task;
   }
   ```

4. **Implement `ConnectAsync`.** Resolve the `CBPeripheral` by
   `NSUuid.AsString()` matching the `HeyCyanDeviceInfo.Address`, hand it to
   `QCCentralManager.ConnectPeripheral`, KVO-watch `connectState` for
   `BleConnectState.On`, then call `QCSDKManager.AddPeripheral` to register
   it with the high-level command pipeline:

   ```csharp
   public async Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
   {
       SetState(HeyCyanState.Connecting);
       _peripheral = ResolvePeripheral(device.Address)
           ?? throw new InvalidOperationException($"No peripheral with id {device.Address}");

       _qc.ConnectPeripheral(_peripheral);
       await WaitForConnectStateAsync(BleConnectState.On, TimeSpan.FromSeconds(10), ct);

       var added = new TaskCompletionSource<bool>();
       QCSDKManager.SharedInstance.AddPeripheral(_peripheral, ok => added.TrySetResult(ok));
       if (!await added.Task) throw new IOException("QCSDKManager rejected the peripheral.");

       Device = device;
       SetState(HeyCyanState.Connected);
   }
   ```

5. **Map control commands.** Each `IHeyCyanGlassesSession` capture method
   wraps a single static call on `QCSDKCmdCreator`. Use a small helper that
   converts the success/fail callback pair into a `Task`:

   ```csharp
   public Task TakePhotoAsync(CancellationToken ct) =>
       InvokeCmd((s, f) => QCSDKCmdCreator.SetDeviceMode(QCOperatorDeviceMode.Photo, s, code => f(code)), ct);

   public Task StartVideoAsync(CancellationToken ct) =>
       InvokeCmd((s, f) => QCSDKCmdCreator.SetDeviceMode(QCOperatorDeviceMode.Video, s, code => f(code)), ct);

   public Task TakeAiPhotoAsync(CancellationToken ct) =>
       InvokeCmd((s, f) => QCSDKCmdCreator.SetDeviceMode(QCOperatorDeviceMode.AiPhoto, s, code => f(code)), ct);
   ```

6. **Parse button frames.** The QCSDK demo registers an
   `NSNotificationCenter` observer for `OdmNotifyD2P` and reads `cmdType=2`
   payloads from `notification.userInfo[@"data"]`. Reuse the **same**
   `NotifyFrameParser` the Android session in M33 Phase 1 ships:

   ```csharp
   private void OnD2PNotification(NSNotification n)
   {
       if (n.UserInfo?["data"] is not NSData data) return;
       if (NotifyFrameParser.TryParseButton(data.ToArray(), out var gesture))
           ButtonPressed?.Invoke(this, new HeyCyanButtonEvent(gesture, DateTimeOffset.UtcNow));
   }
   ```

   The parser must live in `src/BodyCam/Services/Glasses/HeyCyan/` (shared
   code) so the Phase 1 fixture corpus drives both platforms.

7. **Implement `EnterTransferModeAsync`.** This is the only call that
   crosses into Wave 3 — the BLE side opens the hotspot, then
   [`HotspotHttpClient`](wave3-hotspot-http-client.md) joins it and probes
   for the glasses' IP:

   ```csharp
   public async Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
   {
       var (ssid, password) = await OpenHotspotAsync(ct);
       await _hotspot.JoinAsync(ssid, password, ct);
       var baseUrl = await _hotspot.DiscoverGlassesIpAsync(ct);
       var files = await _hotspot.GetMediaConfigAsync(baseUrl, ct);
       SetState(HeyCyanState.TransferMode);
       return new HeyCyanTransferSession(baseUrl, files);
   }

   private Task<(string ssid, string password)> OpenHotspotAsync(CancellationToken ct)
   {
       var tcs = new TaskCompletionSource<(string, string)>();
       QCSDKCmdCreator.OpenWifi(QCOperatorDeviceMode.Transfer,
           (s, p) => tcs.TrySetResult((s.ToString(), p?.ToString() ?? "123456789")),
           code => tcs.TrySetException(new IOException($"OpenWifi failed: {code}")));
       using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
       return tcs.Task;
   }
   ```

8. **Translate delegate callbacks.** `QCSDKManagerDelegateProxy` simply
   forwards each `@optional` Obj-C callback into the matching managed event
   on the owning session — battery, media counts, AI photo bytes.

9. **Dispose cleanly.** `DisposeAsync` clears the QCSDK delegate, calls
   `RemoveAllPeripherals`, and signals `HotspotHttpClient` to remove its
   `NEHotspotConfiguration` so Wi-Fi returns to the user's previous network.

## Verify

- [ ] `IosHeyCyanGlassesSession` implements every member of
      `IHeyCyanGlassesSession`
- [ ] BLE scan filters on `QCSDKSERVERUUID1` and `QCSDKSERVERUUID2`
- [ ] `ConnectAsync` waits for `QCCentralManager.ConnectState == On` before
      calling `AddPeripheral`
- [ ] State transitions raise `StateChanged` exactly once per change and
      match the Android session's order
- [ ] Button-frame parsing uses the shared `NotifyFrameParser` (no iOS-only
      reimplementation) and shares fixtures with Android Phase 1
- [ ] `EnterTransferModeAsync` falls back to password `"123456789"` when
      `OpenWifi` returns a `nil` password (the QCSDK convention)
- [ ] `DisposeAsync` clears the QCSDK delegate, removes peripherals, and
      removes the hotspot configuration
- [ ] No iOS-only types leak through `IHeyCyanGlassesSession` (callers see
      only the cross-platform contract from Phase 1)
