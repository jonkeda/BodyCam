#if ANDROID
using Android.Content;
using Com.Oudmon.Ble.Base.Bluetooth;
using Com.Oudmon.Ble.Base.Communication;
using Com.Oudmon.Ble.Base.Communication.BigData.Resp;
using Microsoft.Maui.ApplicationModel;
using System.Collections.Concurrent;

namespace BodyCam.Platforms.Android.HeyCyan;

/// <summary>
/// Android-only bridge wrapping LargeDataHandler (high-level command/response),
/// BleBaseControl (scan/connect/pair), and BleOperateManager (connection lifecycle).
/// Marshals all SDK callbacks off the BLE I/O HandlerThread onto the captured dispatcher.
/// 
/// NOTE: This is a Phase 1 Wave 2 implementation that establishes the bridge structure.
/// Many SDK API details differ from sdk-api-reference.md due to binding quirks and require
/// hardware verification. Real scan/connect/notify flows will be completed in Wave 3
/// once HeyCyan glasses are available for testing.
/// </summary>
internal sealed class HeyCyanSdkBridge : Services.Glasses.HeyCyan.IHeyCyanSdkBridge
{
    // SDK singletons — see sdk-api-reference.md §A.
    // Actual API signatures differ from docs; deferred to hardware testing.
    private readonly BleBaseControl? _ble;
    private readonly BleOperateManager? _ops;

    private readonly ConcurrentDictionary<int, TaskCompletionSource<Services.Glasses.HeyCyan.HeyCyanResponse>> _pending = new();
    private readonly SynchronizationContext _dispatcher;

    // ACTION_* constants from LargeDataHandler (sdk-api-reference.md §A).
    private const int ACTION_DEVICE_NOTIFY = 100;   // multiplexed: battery / IP / button / errors
    private const int ACTION_DOWNLOAD_NOTIFY = 2;   // transfer-mode IP + P2P error

    // Listener instances kept alive for the bridge lifetime.
    private readonly BleListener? _bleListener;

    // Scan state
    private readonly ConcurrentDictionary<string, bool> _discoveredMacs = new();
    private TaskCompletionSource<bool>? _scanTcs;
    private CancellationTokenSource? _scanCts;

    // Connect state
    private TaskCompletionSource<bool>? _connectTcs;
    private string? _connectingMac;
    private bool _disposed;

    public event EventHandler<Services.Glasses.HeyCyan.HeyCyanScanResult>? DeviceDiscovered;
    public event EventHandler<Services.Glasses.HeyCyan.HeyCyanConnectionState>? ConnectionStateChanged;
    public event EventHandler<Services.Glasses.HeyCyan.HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<Services.Glasses.HeyCyan.HeyCyanRawNotify>? RawNotify;

    public HeyCyanSdkBridge()
    {
        _dispatcher = SynchronizationContext.Current
            ?? throw new InvalidOperationException("HeyCyanSdkBridge must be constructed on the main thread");

        // Get Android context from MAUI.
        var context = Platform.CurrentActivity ?? Platform.AppContext;
        if (context is null)
            throw new InvalidOperationException("Cannot get Android context — MAUI Platform not initialized");

        // Initialize SDK singletons with Android context/application.
        var app = (global::Android.App.Application?)context.ApplicationContext;
        
        // NOTE: LargeDataHandler.GetInstance() binding signature differs from docs.
        // Actual implementation will be completed in Wave 3 with hardware.
        // _ldh = LargeDataHandler.GetInstance();  // Compilation error: no such overload
        
        _ble = BleBaseControl.GetInstance(context);
        _ops = BleOperateManager.GetInstance(app);

        // BLE GATT lifecycle — connection state changes.
        // NOTE: BleBaseControl.SetListener method name/signature differs from docs.
        // Actual listener registration will be verified in Wave 3 with hardware.
        _bleListener = new BleListener(
            OnBleConnectionStateChanged,
            OnBleServiceDiscovered,
            OnBleDeviceDiscovered);
        // _ble.SetListener(_bleListener);  // Compilation error: no SetListener method

        // NOTE: LargeDataHandler.AddOutDeviceListener API differs from docs.
        // Notify-frame parsing will be completed in Wave 3 with hardware.
    }

    public Task StartScanAsync(TimeSpan timeout, CancellationToken ct)
    {
        // NOTE: BLE scan API (BleScannerHelper) differs from docs and requires additional setup.
        // Real scan implementation deferred to Wave 3 hardware testing.
        if (_scanTcs is not null)
            throw new InvalidOperationException("Scan already in progress");

        _discoveredMacs.Clear();
        _scanTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _scanCts = new CancellationTokenSource();

        // Stub: Real scan will use BleScannerHelper.getInstance().scanDevice(...)
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _scanCts.Token);
        linkedCts.Token.Register(() =>
        {
            _scanTcs?.TrySetCanceled(linkedCts.Token);
            _scanTcs = null;
            _scanCts = null;
        });

        _ = Task.Delay(timeout, linkedCts.Token).ContinueWith(_ =>
        {
            _scanTcs?.TrySetResult(true);
            _scanTcs = null;
            _scanCts = null;
        }, TaskScheduler.Default);

        return _scanTcs.Task;
    }

    public Task StopScanAsync()
    {
        _scanCts?.Cancel();
        _scanTcs?.TrySetResult(true);
        _scanTcs = null;
        _scanCts = null;
        return Task.CompletedTask;
    }

    public Task ConnectAsync(string macAddress, CancellationToken ct)
    {
        if (_connectTcs is not null)
            throw new InvalidOperationException("Connect already in progress");

        _connectingMac = macAddress;
        _connectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        ct.Register(() =>
        {
            _connectTcs?.TrySetCanceled(ct);
            _connectTcs = null;
            _connectingMac = null;
        });

        // Use BleOperateManager.ConnectDirectly (assumes device was already discovered).
        _ops?.ConnectDirectly(macAddress);

        return _connectTcs.Task;
    }

    public Task DisconnectAsync()
    {
        _ops?.Disconnect();
        Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Disconnecting);
        return Task.CompletedTask;
    }

    public Task<Services.Glasses.HeyCyan.HeyCyanResponse> SendAsync(byte[] payload, CancellationToken ct)
    {
        if (payload.Length == 0)
            throw new ArgumentException("Payload cannot be empty", nameof(payload));

        var cmdType = payload[0];
        var tcs = new TaskCompletionSource<Services.Glasses.HeyCyan.HeyCyanResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(cmdType, tcs))
            throw new InvalidOperationException($"A request with cmdType {cmdType} is already pending");

        using var reg = ct.Register(() =>
        {
            if (_pending.TryRemove(cmdType, out var pending))
                pending.TrySetCanceled(ct);
        });

        // NOTE: LargeDataHandler.GlassesControl API differs from docs.
        // Real request/response correlation will be completed in Wave 3 with hardware.
        // Placeholder: immediately fail for now.
        tcs.TrySetException(new NotImplementedException(
            "SendAsync requires LargeDataHandler binding fixes — deferred to Wave 3 hardware testing"));

        return tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // NOTE: Unhook listeners once binding APIs are verified.
        // _ldh.RemoveOutDeviceListener(ACTION_DEVICE_NOTIFY);
        // _ldh.RemoveOutDeviceListener(ACTION_DOWNLOAD_NOTIFY);
        // _ble.SetListener(null);

        // Complete pending requests with ObjectDisposedException.
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetException(new ObjectDisposedException(nameof(HeyCyanSdkBridge)));
        }
        _pending.Clear();

        _scanTcs?.TrySetCanceled();
        _connectTcs?.TrySetCanceled();
    }

    // Called by DeviceNotifyListener.ParseData(int cmdType, GlassesDeviceNotifyRsp rsp)
    // on the BleOperateManager HandlerThread — see Raise() for trampolining.
    private void OnNotify(byte[] loadData)
    {
        if (loadData.Length < 7) return;

        var notifyType = loadData[6]; // sdk-api-reference.md §C

        switch (notifyType)
        {
            case 0x02: // AI-photo button pressed; loadData[7] = button id
                Raise(ButtonPressed, new Services.Glasses.HeyCyan.HeyCyanButtonEvent(
                    Services.Glasses.HeyCyan.HeyCyanButtonGesture.Tap,
                    DateTimeOffset.UtcNow));
                return;

            case 0x03: // AI-voice button pressed; loadData[7] == 1
                Raise(ButtonPressed, new Services.Glasses.HeyCyan.HeyCyanButtonEvent(
                    Services.Glasses.HeyCyan.HeyCyanButtonGesture.DoubleTap,
                    DateTimeOffset.UtcNow));
                return;

            // 0x05 battery, 0x08 transfer IP, 0x09 P2P error — forwarded to Phase 2 via RawNotify.
        }

        Raise(RawNotify, new Services.Glasses.HeyCyan.HeyCyanRawNotify(loadData));
    }

    private void OnBleConnectionStateChanged(bool connected)
    {
        if (connected)
        {
            Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Connecting);
        }
        else
        {
            Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Disconnected);
            _connectTcs?.TrySetException(new InvalidOperationException("BLE connection lost"));
            _connectTcs = null;
            _connectingMac = null;
        }
    }

    private void OnBleServiceDiscovered()
    {
        // Connection is ready when GATT services are discovered.
        Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Connected);
        _connectTcs?.TrySetResult(true);
        _connectTcs = null;
        _connectingMac = null;
    }

    private void OnBleDeviceDiscovered(string name, string address, int rssi)
    {
        // Filter by manufacturer name prefix (CyanBridge uses "QC" prefix).
        if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("QC", StringComparison.OrdinalIgnoreCase))
            return;

        // Deduplicate by MAC address.
        if (!_discoveredMacs.TryAdd(address, true))
            return;

        Raise(DeviceDiscovered, new Services.Glasses.HeyCyan.HeyCyanScanResult(name, address, rssi));
    }

    /// <summary>
    /// Trampoline every SDK callback off the BLE I/O thread before raising events.
    /// BleOperateManager extends HandlerThread, and ILargeDataResponse.ParseData /
    /// IBleListener.* fire on that thread (sdk-api-reference.md §E.2).
    /// </summary>
    private void Raise<T>(EventHandler<T>? handler, T arg)
    {
        if (handler is null) return;
        _dispatcher.Post(_ => handler.Invoke(this, arg), null);
    }
}

/// <summary>
/// C# adapter for IBleListener that forwards GATT lifecycle callbacks to delegates.
/// </summary>
internal sealed class BleListener : Java.Lang.Object, IBleListener
{
    private readonly Action<bool> _onConnectionStateChanged;
    private readonly Action _onServiceDiscovered;
    private readonly Action<string, string, int>? _onDeviceDiscovered;

    public BleListener(
        Action<bool> onConnectionStateChanged,
        Action onServiceDiscovered,
        Action<string, string, int>? onDeviceDiscovered = null)
    {
        _onConnectionStateChanged = onConnectionStateChanged;
        _onServiceDiscovered = onServiceDiscovered;
        _onDeviceDiscovered = onDeviceDiscovered;
    }

    public void BleGattConnected(global::Android.Bluetooth.BluetoothDevice? p0)
    {
        _onConnectionStateChanged(true);
    }

    public void BleGattDisconnect(global::Android.Bluetooth.BluetoothDevice? p0)
    {
        _onConnectionStateChanged(false);
    }

    public void BleServiceDiscovered(int p0, string? p1)
    {
        _onServiceDiscovered();
    }

    public void BleStatus(int rssi, int p1)
    {
        // During scan, BleStatus is called with rssi and device state.
        // Device discovery info comes through other mechanisms in BleBaseControl.
        // For now, this is a no-op.
    }

    public bool IsConnected => _ops?.IsConnected ?? false;

    private static BleOperateManager? _ops;
    public static void SetOperateManager(BleOperateManager ops) => _ops = ops;

    // Unused IBleListener methods — no-op.
    public void BleCharacteristicChanged(string? p0, string? p1, byte[]? p2) { }
    public void BleCharacteristicNotification() { }
    public void BleCharacteristicRead(string? p0, string? p1, int p2, byte[]? p3) { }
    public void BleCharacteristicWrite(string? p0, string? p1, int p2, byte[]? p3) { }
    public void BleNoCallback() { }
    public bool Execute(Com.Oudmon.Ble.Base.Request.BaseRequest? p0) => false;
    public void OnDescriptorRead(global::Android.Bluetooth.BluetoothGatt? p0, global::Android.Bluetooth.BluetoothGattDescriptor? p1, int p2) { }
    public void OnDescriptorWrite(global::Android.Bluetooth.BluetoothGatt? p0, global::Android.Bluetooth.BluetoothGattDescriptor? p1, int p2) { }
    public void OnReadRemoteRssi(global::Android.Bluetooth.BluetoothGatt? p0, int p1, int p2) { }
    public void StartConnect() { }
}
#endif
