# Wave 2 — `HeyCyanSdkBridge` (Android-only)

**Parent:** [../phase1-android-binding.md](../phase1-android-binding.md)
**Previous:** [wave1-aar-binding-library.md](wave1-aar-binding-library.md)
**Next:** [wave3-android-glasses-session.md](wave3-android-glasses-session.md)

> **Authoritative SDK API names:** see [../sdk-api-reference.md](../sdk-api-reference.md).
> The bridge wraps `LargeDataHandler` (high-level command/response),
> `BleBaseControl` (scan/connect/pair), and `BleOperateManager` (connection
> lifecycle / low-level notify). All three are `Com.Oudmon.Ble.Base.*`
> singletons accessed via `GetInstance()`. Their callbacks fire on the
> `BleOperateManager` `HandlerThread` — every event raised from this bridge
> must be marshalled off that thread before reaching MAUI code.

## Goal

Wrap the raw managed AAR surface from Wave 1 in a thin Android-only bridge
that turns SDK callbacks into `event`s and request/response correlation
into `Task<HeyCyanResponse>`. The bridge knows nothing about
`IHeyCyanGlassesSession` — it just gives Wave 3 a sane, testable
abstraction (`IHeyCyanSdkBridge`) so we are not forced to mock JNI types.

## Steps

1. **Define the bridge abstraction** at
   `src/BodyCam/Services/Glasses/HeyCyan/IHeyCyanSdkBridge.cs` (cross-platform —
   referenced by both the Android impl and the fake in Wave 5):

   ```csharp
   namespace BodyCam.Services.Glasses.HeyCyan;

   internal interface IHeyCyanSdkBridge : IDisposable
   {
       event EventHandler<HeyCyanScanResult>? DeviceDiscovered;
       event EventHandler<HeyCyanConnectionState>? ConnectionStateChanged;
       event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
       event EventHandler<HeyCyanRawNotify>? RawNotify;

       Task StartScanAsync(TimeSpan timeout, CancellationToken ct);
       Task StopScanAsync();
       Task ConnectAsync(string macAddress, CancellationToken ct);
       Task DisconnectAsync();
       Task<HeyCyanResponse> SendAsync(byte[] payload, CancellationToken ct);
   }

   internal enum HeyCyanConnectionState { Disconnected, Connecting, Connected, Disconnecting }

   internal sealed record HeyCyanScanResult(string Name, string MacAddress, int Rssi);
   internal sealed record HeyCyanRawNotify(byte[] LoadData);
   internal sealed record HeyCyanResponse(int CmdType, byte[] Payload);
   ```

2. **Implement `HeyCyanSdkBridge`** at
   `src/BodyCam/Platforms/Android/HeyCyan/HeyCyanSdkBridge.cs`. It owns
   the SDK singletons and one `ConcurrentDictionary<int, TaskCompletionSource<HeyCyanResponse>>`
   keyed by `cmdType` for response correlation:

   ```csharp
   namespace BodyCam.Platforms.Android.HeyCyan;

   internal sealed class HeyCyanSdkBridge : IHeyCyanSdkBridge
   {
       // sdk-api-reference.md §A — real SDK singletons.
       private readonly LargeDataHandler _ldh = LargeDataHandler.GetInstance();
       private readonly BleBaseControl _ble = BleBaseControl.GetInstance();         // scan/connect/pair
       private readonly BleOperateManager _ops = BleOperateManager.GetInstance();   // connection lifecycle, low-level notify
       private readonly ConcurrentDictionary<int, TaskCompletionSource<HeyCyanResponse>> _pending = new();
       private readonly SynchronizationContext _dispatcher;

       // ACTION_* constants from LargeDataHandler (sdk-api-reference.md §A).
       private const int ACTION_DEVICE_NOTIFY   = 100; // multiplexed: battery / IP / button / errors
       private const int ACTION_DOWNLOAD_NOTIFY = 2;   // transfer-mode IP + P2P error
       private const int ACTION_GLASSES_BATTERY = 0x42;

       public event EventHandler<HeyCyanScanResult>? DeviceDiscovered;
       public event EventHandler<HeyCyanConnectionState>? ConnectionStateChanged;
       public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
       public event EventHandler<HeyCyanRawNotify>? RawNotify;

       public HeyCyanSdkBridge()
       {
           _dispatcher = SynchronizationContext.Current
               ?? throw new InvalidOperationException("HeyCyanSdkBridge must be constructed on the main thread");

           // High-level multiplexed device-notify channel — battery, button, IP, errors.
           _ldh.AddOutDeviceListener(ACTION_DEVICE_NOTIFY,   new DeviceNotifyListener(OnNotify));
           _ldh.AddOutDeviceListener(ACTION_DOWNLOAD_NOTIFY, new DeviceNotifyListener(OnNotify));
           // BLE GATT lifecycle — connection state changes.
           _ble.SetListener(new BleListener(OnConnectionStateChanged));
       }
       // …
   }
   ```

   `DeviceNotifyListener` is a thin C# subclass of
   `Com.Oudmon.Ble.Base.Communication.Bigdata.Resp.GlassesDeviceNotifyListener`
   (which itself implements `ILargeDataResponse<GlassesDeviceNotifyRsp>`).
   `BleListener` implements `IBleListener` (sdk-api-reference.md §A).

3. **Trampoline every SDK callback** off the BLE I/O thread before raising
   events. `BleOperateManager` extends `HandlerThread`, and
   `ILargeDataResponse.ParseData` / `IBleListener.*` fire on that thread
   (sdk-api-reference.md §E.2). Surfacing them directly to MAUI crashes on
   the main-thread guard. Use the captured `_dispatcher`:

   ```csharp
   private void Raise<T>(EventHandler<T>? h, T arg) where T : class
       => _dispatcher.Post(_ => h?.Invoke(this, arg), null);
   ```

4. **Implement notify-frame parsing** verbatim against
   [`android/AGENTS.md`](../../../../Alternative-HeyCyan-App-and-SDK/android/AGENTS.md),
   `MainActivity.kt`, and sdk-api-reference.md §C. The high-level SDK
   already reassembles fragmented BLE frames via `LargeDataParser`, so we
   receive a single `GlassesDeviceNotifyRsp.LoadData` per logical event.
   Button events are debounced by the SDK — we must not re-debounce:

   ```csharp
   // Called by DeviceNotifyListener.ParseData(int cmdType, GlassesDeviceNotifyRsp rsp)
   // on the BleOperateManager HandlerThread — see Raise() for trampolining.
   private void OnNotify(byte[] loadData)
   {
       if (loadData.Length < 8) return;
       var notifyType = loadData[6]; // sdk-api-reference.md §C

       switch (notifyType)
       {
           case 0x02: // AI-photo button pressed; loadData[7] = button id
               Raise(ButtonPressed, new HeyCyanButtonEvent(HeyCyanButtonGesture.Tap, DateTimeOffset.UtcNow));
               return;
           case 0x03: // AI-voice button pressed; loadData[7] == 1
               Raise(ButtonPressed, new HeyCyanButtonEvent(HeyCyanButtonGesture.DoubleTap, DateTimeOffset.UtcNow));
               return;
           // 0x05 battery, 0x08 transfer IP, 0x09 P2P error — forwarded to phase 2 via RawNotify.
       }

       Raise(RawNotify, new HeyCyanRawNotify(loadData));
   }
   ```

   > **Gesture mapping note:** the QCSDK Android build surfaces *which*
   > button (AI-photo at `0x02` vs voice at `0x03`), not tap/double/long.
   > sdk-api-reference.md §E.1 lists this as an open item; the
   > tap/double/long mapping above is provisional and will be revisited
   > when hardware is available.

5. **Implement `SendAsync` with `cmdType` correlation.**
   `LargeDataHandler.GlassesControl(byte[], ILargeDataResponse<GlassModelControlResponse>)`
   **requires** a callback (sdk-api-reference.md §D) and invokes
   `ParseData(int cmdType, GlassModelControlResponse resp)` once per send.
   Map the request's leading `cmdType` byte to a `TaskCompletionSource`,
   register cancellation, then invoke the SDK. Reject with
   `OperationCanceledException` if `ct` fires:

   ```csharp
   public Task<HeyCyanResponse> SendAsync(byte[] payload, CancellationToken ct)
   {
       if (payload.Length == 0) throw new ArgumentException(null, nameof(payload));
       var cmdType = payload[0];
       var tcs = new TaskCompletionSource<HeyCyanResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
       _pending[cmdType] = tcs;

       using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

       // ILargeDataResponse<GlassModelControlResponse>.ParseData(cmd, resp) fires on the BLE I/O thread.
       _ldh.GlassesControl(payload, new ControlResponseListener((cmd, resp) =>
       {
           if (_pending.TryRemove(cmd, out var pending))
               pending.TrySetResult(new HeyCyanResponse(cmd, resp?.Payload ?? Array.Empty<byte>()));
       }));
       return tcs.Task;
   }
   ```

   `ControlResponseListener` is a thin C# class implementing
   `ILargeDataResponse<GlassModelControlResponse>` (`Com.Oudmon.Ble.Base.Communication`)
   and forwarding `ParseData` to the supplied delegate.

6. **Wire scan & connect** through `BleBaseControl` and `BleOperateManager`
   (sdk-api-reference.md §A). There is no single `QCCentralManager` on
   Android — scan/discovery and pairing live on `BleBaseControl`, while
   connection lifecycle / `IsConnected` / `IsReady` live on
   `BleOperateManager`. `StartScanAsync` registers an `IBleListener` via
   `BleBaseControl.SetListener(...)`, filters by manufacturer name
   (`MainActivity.kt` uses a prefix match), debounces duplicate MACs, and
   pushes `DeviceDiscovered`. `ConnectAsync` calls
   `BleOperateManager.ConnectDirectly(mac)` (or `ConnectWithScan(mac)` if
   we did not already discover the device), forwards
   `IBleListener.BleGattConnected` → `Connecting` and
   `IBleListener.BleServiceDiscovered` (or `IsReady() == true`) →
   `Connected`, and unhooks the scan listener on the first connect
   callback.

7. **Document command payloads in code** rather than scattered constants.
   Add `HeyCyanCommands.cs` with the verbatim CyanBridge payload bytes —
   never invent them:

   ```csharp
   internal static class HeyCyanCommands
   {
       // CyanBridge MainActivity.kt :: getVersion()
       public static byte[] GetVersion() => new byte[] { 0x01, 0x01, /* … */ };

       // CyanBridge MainActivity.kt :: getBattery()
       public static byte[] GetBattery() => new byte[] { 0x01, 0x02, /* … */ };

       // CyanBridge MainActivity.kt :: syncTime()
       public static byte[] SyncTime(DateTimeOffset now)
       {
           Span<byte> b = stackalloc byte[5];
           b[0] = 0x03;
           BinaryPrimitives.WriteUInt32LittleEndian(b[1..], (uint)now.ToUnixTimeSeconds());
           return b.ToArray();
       }

       // CyanBridge MainActivity.kt :: getMediaCount()
       public static byte[] GetMediaCounts() => new byte[] { 0x04, 0x01, /* … */ };
   }
   ```

8. **Implement `Dispose`** to unhook every SDK listener (paired
   `LargeDataHandler.RemoveOutDeviceListener(int)` for every
   `AddOutDeviceListener` registered in step 2 — the SDK stores them in a
   `ConcurrentHashMap`; failing to remove leaks across reconnects per
   sdk-api-reference.md §E.3) and complete every pending TCS with
   `ObjectDisposedException`. Idempotent.

## Verify

- [ ] `StartScanAsync` raises `DeviceDiscovered` for the manufacturer's known glasses name
- [ ] Duplicate MACs within the scan window are deduplicated
- [ ] `ConnectAsync(mac)` transitions through `Connecting` → `Connected` and unsubscribes the scan listener
- [ ] `SendAsync` correlates response by `cmdType` and respects `ct` (cancellation throws `OperationCanceledException`)
- [ ] Button frames produce exactly one `ButtonPressed` per gesture (no duplicate from notify echo)
- [ ] All callbacks marshal to `_dispatcher` — never raise events on the BLE I/O `HandlerThread` (sdk-api-reference.md §E.2)
- [ ] Disposing the bridge unhooks every listener (no managed leak; verified with a follow-up scan call throwing `ObjectDisposedException`)
- [ ] `HeyCyanCommands` payload bytes match the CyanBridge sources verbatim (no invented bytes)
