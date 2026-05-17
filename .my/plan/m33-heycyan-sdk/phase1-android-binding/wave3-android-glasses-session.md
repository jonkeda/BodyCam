# Wave 3 — `AndroidHeyCyanGlassesSession`

**Parent:** [../phase1-android-binding.md](../phase1-android-binding.md)
**Previous:** [wave2-heycyan-sdk-bridge.md](wave2-heycyan-sdk-bridge.md)
**Next:** [wave4-di-and-permissions.md](wave4-di-and-permissions.md)

> **Authoritative SDK API names:** see [../sdk-api-reference.md](../sdk-api-reference.md).
> This wave consumes `IHeyCyanSdkBridge` only — it does not reference the
> raw SDK — but every `HeyCyanResponse.Payload` parsed below is the
> `GlassModelControlResponse` byte stream produced by
> `LargeDataHandler.GlassesControl(byte[], ILargeDataResponse<GlassModelControlResponse>)`,
> and bridge events arrive already marshalled off the BLE I/O
> `HandlerThread` (sdk-api-reference.md §E.2).

## Goal

Implement the cross-platform-facing `IHeyCyanGlassesSession` (defined in
[overview.md](../overview.md)) on top of `IHeyCyanSdkBridge`. This is the
single object every later phase will consume: Phase 2 calls
`EnterTransferModeAsync`, Phase 3 watches `StateChanged`, Phase 4 wraps
`ButtonPressed`, Phase 7 binds it to UI. Wave 3 lights up scan, connect,
disconnect, version, battery, media counts, time-sync, and button event
forwarding. Camera/audio/transfer methods throw `NotImplementedException`
with the deferring phase noted.

## Steps

1. **Create the session class** at
   `src/BodyCam/Services/Glasses/HeyCyan/AndroidHeyCyanGlassesSession.cs`.
   It is `internal sealed`, inherits `ViewModelBase` (so XAML can bind to
   `State`/`Device`), and is compiled only for `-android`:

   ```csharp
   #if ANDROID
   namespace BodyCam.Services.Glasses.HeyCyan;

   internal sealed class AndroidHeyCyanGlassesSession : ViewModelBase, IHeyCyanGlassesSession
   {
       private readonly IHeyCyanSdkBridge _bridge;
       private readonly ILogger<AndroidHeyCyanGlassesSession> _log;
       private readonly SemaphoreSlim _connectGate = new(1, 1);
       private HeyCyanState _state = HeyCyanState.Disconnected;
       private HeyCyanDeviceInfo? _device;

       public HeyCyanState State
       {
           get => _state;
           private set
           {
               if (SetProperty(ref _state, value))
                   StateChanged?.Invoke(this, value);
           }
       }

       public HeyCyanDeviceInfo? Device
       {
           get => _device;
           private set => SetProperty(ref _device, value);
       }

       public event EventHandler<HeyCyanState>? StateChanged;
       public event EventHandler<HeyCyanBattery>? BatteryUpdated;
       public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
       public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
       public event EventHandler<byte[]>? AiPhotoReceived;

       public AndroidHeyCyanGlassesSession(
           IHeyCyanSdkBridge bridge,
           ILogger<AndroidHeyCyanGlassesSession> log)
       {
           _bridge = bridge;
           _log = log;
           _bridge.ButtonPressed += (s, e) => ButtonPressed?.Invoke(this, e);
           _bridge.ConnectionStateChanged += OnBridgeConnectionStateChanged;
       }
   }
   #endif
   ```

2. **Implement `ScanAsync`** with MAC deduplication and timeout:

   ```csharp
   public async Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct)
   {
       await HeyCyanPermissions.RequestAsync().ConfigureAwait(false); // Wave 4
       State = HeyCyanState.Scanning;
       var seen = new Dictionary<string, HeyCyanDeviceInfo>(StringComparer.OrdinalIgnoreCase);
       void OnDiscovered(object? s, HeyCyanScanResult r) =>
           seen[r.MacAddress] = new HeyCyanDeviceInfo(r.Name, r.MacAddress, r.Rssi);

       _bridge.DeviceDiscovered += OnDiscovered;
       try
       {
           using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
           cts.CancelAfter(timeout);
           await _bridge.StartScanAsync(timeout, cts.Token).ConfigureAwait(false);
           try { await Task.Delay(timeout, cts.Token).ConfigureAwait(false); }
           catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* timeout */ }
       }
       finally
       {
           _bridge.DeviceDiscovered -= OnDiscovered;
           await _bridge.StopScanAsync().ConfigureAwait(false);
           if (State == HeyCyanState.Scanning) State = HeyCyanState.Disconnected;
       }
       return seen.Values.ToArray();
   }
   ```

3. **Implement `ConnectAsync`** as non-re-entrant via `_connectGate`. After
   reaching `Connected`, fire-and-forget version + battery + media-count
   queries so the UI lights up immediately:

   ```csharp
   public async Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
   {
       if (!await _connectGate.WaitAsync(0, ct).ConfigureAwait(false))
           throw new InvalidOperationException("Connect already in progress");
       try
       {
           if (State is HeyCyanState.Connected or HeyCyanState.Connecting)
               throw new InvalidOperationException("Already connected");
           State = HeyCyanState.Connecting;
           await _bridge.ConnectAsync(device.Address, ct).ConfigureAwait(false);
           Device = device;
           State = HeyCyanState.Connected;
           _ = HydrateAsync(); // fire-and-forget
       }
       catch
       {
           State = HeyCyanState.Disconnected;
           throw;
       }
       finally { _connectGate.Release(); }
   }

   private async Task HydrateAsync()
   {
       try
       {
           var ver = await GetVersionAsync(CancellationToken.None).ConfigureAwait(false);
           Device = Device! with { /* merge ver fields per HeyCyanDeviceInfo shape */ };
           var bat = await GetBatteryAsync(CancellationToken.None).ConfigureAwait(false);
           BatteryUpdated?.Invoke(this, bat);
       }
       catch (Exception ex) { _log.LogWarning(ex, "Hydrate failed"); }
   }
   ```

4. **Implement command methods** by sending the verbatim CyanBridge bytes
   (from `HeyCyanCommands` in Wave 2) and parsing the response payload.
   Keep parsers in a static `HeyCyanFrameParser` class so Wave 5 can unit-
   test them without the bridge:

   ```csharp
   public async Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)
   {
       var resp = await _bridge.SendAsync(HeyCyanCommands.GetVersion(), ct).ConfigureAwait(false);
       return HeyCyanFrameParser.ParseVersion(resp.Payload);
   }

   public async Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)
   {
       var resp = await _bridge.SendAsync(HeyCyanCommands.GetBattery(), ct).ConfigureAwait(false);
       return HeyCyanFrameParser.ParseBattery(resp.Payload);
   }

   public Task SyncTimeAsync(CancellationToken ct)
       => _bridge.SendAsync(HeyCyanCommands.SyncTime(DateTimeOffset.UtcNow), ct);
   ```

5. **Forward bridge `ConnectionStateChanged` to session `State`.** A
   glasses-initiated BLE drop must transition `Connected → Disconnected`
   without throwing:

   ```csharp
   private void OnBridgeConnectionStateChanged(object? s, HeyCyanConnectionState e)
   {
       State = e switch
       {
           HeyCyanConnectionState.Disconnected => HeyCyanState.Disconnected,
           HeyCyanConnectionState.Connecting   => HeyCyanState.Connecting,
           HeyCyanConnectionState.Connected    => HeyCyanState.Connected,
           HeyCyanConnectionState.Disconnecting=> HeyCyanState.Disconnecting,
           _ => State,
       };
       if (e == HeyCyanConnectionState.Disconnected) Device = null;
   }
   ```

6. **Stub deferred methods** with explicit phase pointers so future agents
   know exactly where the work lives:

   ```csharp
   public Task TakePhotoAsync(CancellationToken ct)
       => throw new NotImplementedException("M33 Phase 2 — file-based snapshot");
   public Task StartVideoAsync(CancellationToken ct)
       => throw new NotImplementedException("M33 Phase 2");
   public Task StopVideoAsync(CancellationToken ct)
       => throw new NotImplementedException("M33 Phase 2");
   public Task StartAudioAsync(CancellationToken ct)
       => throw new NotImplementedException("M33 Phase 5 — recorded OPUS, not live mic");
   public Task StopAudioAsync(CancellationToken ct)
       => throw new NotImplementedException("M33 Phase 5");
   public Task TakeAiPhotoAsync(CancellationToken ct)
       => throw new NotImplementedException("M33 Phase 2");
   public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
       => throw new NotImplementedException("M33 Phase 2 — WiFi-Direct + HTTP");
   ```

7. **Implement `DisposeAsync`** as idempotent. If currently connected,
   disconnect first; then dispose the bridge:

   ```csharp
   public async ValueTask DisposeAsync()
   {
       if (State is HeyCyanState.Connected or HeyCyanState.Connecting)
       {
           try { await DisconnectAsync(CancellationToken.None).ConfigureAwait(false); }
           catch (Exception ex) { _log.LogWarning(ex, "Dispose disconnect failed"); }
       }
       _bridge.Dispose();
       _connectGate.Dispose();
   }
   ```

## Verify

- [ ] `ScanAsync` returns deduplicated devices and respects timeout/cancel
- [ ] `ConnectAsync` throws `InvalidOperationException` when already connected
- [ ] `ConnectAsync` cancellation propagates as `OperationCanceledException` and resets `State` to `Disconnected`
- [ ] After connect, `Device.Firmware` / `Device.MacAddress` are populated within ~1s
- [ ] `BatteryUpdated` fires within ~1s of connect
- [ ] BLE drop transitions to `Disconnected` cleanly (no exception, `Device` cleared)
- [ ] `ButtonPressed` is forwarded one-for-one from the bridge
- [ ] Stub methods throw `NotImplementedException` with the deferring phase noted in the message
- [ ] `DisposeAsync` is idempotent and cancels in-flight `SendAsync` calls
- [ ] Class is `#if ANDROID` guarded — does not appear in non-Android TFM builds
