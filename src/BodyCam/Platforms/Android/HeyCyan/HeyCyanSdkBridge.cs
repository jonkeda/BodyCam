#if ANDROID
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Android.Util;
using Java.Util;
using Microsoft.Maui.ApplicationModel;
using System.Collections.Concurrent;

namespace BodyCam.Platforms.Android.HeyCyan;

/// <summary>
/// Android HeyCyan BLE bridge implemented directly against Android GATT APIs.
/// Keeps the historical class name while replacing the vendor AAR bridge.
/// </summary>
internal sealed class HeyCyanSdkBridge : Services.Glasses.HeyCyan.IHeyCyanSdkBridge
{
    private static readonly UUID SerialPortServiceUuid =
        UUID.FromString("de5bf728-d711-4e47-af26-65e3012a5dc7")!;
    private static readonly UUID SerialPortNotifyUuid =
        UUID.FromString("de5bf729-d711-4e47-af26-65e3012a5dc7")!;
    private static readonly UUID SerialPortWriteUuid =
        UUID.FromString("de5bf72a-d711-4e47-af26-65e3012a5dc7")!;
    private static readonly UUID ClientCharacteristicConfigUuid =
        UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    private const string LogTag = "BodyCamHeyCyanBle";

    private readonly Context _context;
    private readonly BluetoothAdapter _adapter;
    private readonly SynchronizationContext _dispatcher;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, bool> _discoveredMacs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<Services.Glasses.HeyCyan.HeyCyanResponse>> _pending = new();
    private readonly List<byte> _receiveBuffer = [];

    private BluetoothLeScanner? _scanner;
    private DirectScanCallback? _scanCallback;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenRegistration _scanRegistration;
    private TaskCompletionSource<bool>? _scanTcs;

    private BluetoothGatt? _gatt;
    private DirectGattCallback? _gattCallback;
    private BluetoothGattCharacteristic? _writeCharacteristic;
    private BluetoothGattCharacteristic? _notifyCharacteristic;
    private TaskCompletionSource<bool>? _connectTcs;
    private CancellationTokenRegistration _connectRegistration;
    private TaskCompletionSource<GattStatus>? _writeTcs;
    private DateTimeOffset? _receiveStartedAt;
    private bool _disposed;

    public event EventHandler<Services.Glasses.HeyCyan.HeyCyanScanResult>? DeviceDiscovered;
    public event EventHandler<Services.Glasses.HeyCyan.HeyCyanConnectionState>? ConnectionStateChanged;
    public event EventHandler<Services.Glasses.HeyCyan.HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<Services.Glasses.HeyCyan.HeyCyanRawNotify>? RawNotify;

    public HeyCyanSdkBridge()
    {
        _dispatcher = SynchronizationContext.Current
            ?? throw new InvalidOperationException("HeyCyanSdkBridge must be constructed on the main thread");

        _context = Platform.CurrentActivity ?? Platform.AppContext
            ?? throw new InvalidOperationException("Cannot get Android context; MAUI Platform not initialized");

        var manager = (BluetoothManager?)_context.GetSystemService(Context.BluetoothService)
            ?? throw new InvalidOperationException("Android BluetoothManager is not available");

        _adapter = manager.Adapter
            ?? throw new InvalidOperationException("Android Bluetooth adapter is not available");
    }

    public Task StartScanAsync(TimeSpan timeout, CancellationToken ct)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_scanTcs is not null)
                throw new InvalidOperationException("Scan already in progress");

            if (!_adapter.IsEnabled)
                throw new InvalidOperationException("Bluetooth is disabled");

            _scanner = _adapter.BluetoothLeScanner
                ?? throw new InvalidOperationException("Bluetooth LE scanner is not available");

            _discoveredMacs.Clear();
            _scanTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _scanCallback = new DirectScanCallback(OnScanDeviceDiscovered, OnScanFailed);

            _scanRegistration = _scanCts.Token.Register(() =>
            {
                CompleteScan(false, CancellationToken.None);
            });
        }

        var settings = new ScanSettings.Builder()
            .SetScanMode(global::Android.Bluetooth.LE.ScanMode.LowLatency)
            .Build();

        _scanner.StartScan(null, settings, _scanCallback);

        _ = Task.Delay(timeout, CancellationToken.None).ContinueWith(_ =>
        {
            CompleteScan(false, CancellationToken.None);
        }, TaskScheduler.Default);

        return _scanTcs.Task;
    }

    public Task StopScanAsync()
    {
        CompleteScan(false, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task ConnectAsync(string macAddress, CancellationToken ct)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(macAddress))
            throw new ArgumentException("MAC address is required", nameof(macAddress));

        TaskCompletionSource<bool> tcs;
        lock (_gate)
        {
            if (_connectTcs is not null)
                throw new InvalidOperationException("Connect already in progress");

            if (!_adapter.IsEnabled)
                throw new InvalidOperationException("Bluetooth is disabled");

            _connectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _connectRegistration = ct.Register(() =>
            {
                FailConnect(new System.OperationCanceledException(ct));
            });
            tcs = _connectTcs;
        }

        Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Connecting);

        try
        {
            var device = _adapter.GetRemoteDevice(macAddress)
                ?? throw new InvalidOperationException($"Bluetooth device {macAddress} is not available");

            _gattCallback = new DirectGattCallback(this);

            _gatt = OperatingSystem.IsAndroidVersionAtLeast(23)
                ? device.ConnectGatt(_context, false, _gattCallback, BluetoothTransports.Le)
                : device.ConnectGatt(_context, false, _gattCallback);

            if (_gatt is null)
                throw new InvalidOperationException($"ConnectGatt returned null for {macAddress}");
        }
        catch (Exception ex)
        {
            FailConnect(ex);
        }

        return tcs.Task;
    }

    public Task DisconnectAsync()
    {
        Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Disconnecting);
        CloseGatt();
        Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public async Task<Services.Glasses.HeyCyan.HeyCyanResponse> SendAsync(byte[] payload, CancellationToken ct)
    {
        ThrowIfDisposed();

        if (payload.Length == 0)
            throw new ArgumentException("Payload cannot be empty", nameof(payload));

        var action = Services.Glasses.HeyCyan.HeyCyanDirectBleProtocol.DecodeOutgoingAction(payload);
        var responseTcs = new TaskCompletionSource<Services.Glasses.HeyCyan.HeyCyanResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _pending[action] = responseTcs;

            foreach (var chunk in Chunk(payload, Services.Glasses.HeyCyan.HeyCyanDirectBleProtocol.DefaultWriteChunkLength))
            {
                await WriteChunkAsync(chunk, ct).ConfigureAwait(false);
            }

            try
            {
                return await responseTcs.Task
                    .WaitAsync(ResponseTimeoutFor(action), ct)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Log.Debug(LogTag, $"No BLE response for action=0x{action:X2}; returning empty response after write");
                return new Services.Glasses.HeyCyan.HeyCyanResponse(action, []);
            }
        }
        finally
        {
            _pending.TryRemove(action, out _);
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CompleteScan(false, CancellationToken.None);
        CloseGatt();

        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetException(new ObjectDisposedException(nameof(HeyCyanSdkBridge)));
        }
        _pending.Clear();

        _writeTcs?.TrySetException(new ObjectDisposedException(nameof(HeyCyanSdkBridge)));
        _connectTcs?.TrySetException(new ObjectDisposedException(nameof(HeyCyanSdkBridge)));
        _writeGate.Dispose();
    }

    private async Task WriteChunkAsync(byte[] chunk, CancellationToken ct)
    {
        BluetoothGatt? gatt;
        BluetoothGattCharacteristic? characteristic;
        TaskCompletionSource<GattStatus> writeTcs;

        lock (_gate)
        {
            gatt = _gatt;
            characteristic = _writeCharacteristic;

            if (gatt is null || characteristic is null)
                throw new InvalidOperationException("HeyCyan BLE is not connected");

            writeTcs = new TaskCompletionSource<GattStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            _writeTcs = writeTcs;
        }

        characteristic.WriteType = GattWriteType.NoResponse;
        if (!characteristic.SetValue(chunk))
            throw new InvalidOperationException("Failed to set BLE write characteristic value");

        Log.Verbose(LogTag, $"BLE write {chunk.Length} bytes {ToHex(chunk)}");
        if (!gatt.WriteCharacteristic(characteristic))
            throw new InvalidOperationException("Android rejected BLE characteristic write");

        try
        {
            var status = await writeTcs.Task
                .WaitAsync(TimeSpan.FromSeconds(2), ct)
                .ConfigureAwait(false);

            if (status != GattStatus.Success)
                throw new InvalidOperationException($"BLE characteristic write failed with status {status}");
        }
        catch (TimeoutException)
        {
            Log.Debug(LogTag, "BLE write-without-response did not raise OnCharacteristicWrite before timeout");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_writeTcs, writeTcs))
                    _writeTcs = null;
            }
        }
    }

    private void CompleteScan(bool canceled, CancellationToken token)
    {
        TaskCompletionSource<bool>? tcs;
        BluetoothLeScanner? scanner;
        DirectScanCallback? callback;

        lock (_gate)
        {
            tcs = _scanTcs;
            scanner = _scanner;
            callback = _scanCallback;

            _scanTcs = null;
            _scanCallback = null;
            _scanner = null;

            try { _scanRegistration.Dispose(); } catch { }
            _scanRegistration = default;
            try { _scanCts?.Dispose(); } catch { }
            _scanCts = null;
        }

        if (scanner is not null && callback is not null)
        {
            try { scanner.StopScan(callback); }
            catch (Exception ex) { Log.Debug(LogTag, $"StopScan failed: {ex.Message}"); }
        }

        if (canceled)
            tcs?.TrySetCanceled(token);
        else
            tcs?.TrySetResult(true);
    }

    private void OnScanDeviceDiscovered(BluetoothDevice? device, string? advertisedName, int rssi)
    {
        if (device is null)
            return;

        var name = advertisedName;
#pragma warning disable CA1422
        name ??= device.Name;
        var address = device.Address;
#pragma warning restore CA1422

        if (string.IsNullOrWhiteSpace(address) || !IsHeyCyanName(name))
            return;

        if (!_discoveredMacs.TryAdd(address, true))
            return;

        Log.Info(LogTag, $"Discovered {name} {address} rssi={rssi}");
        Raise(DeviceDiscovered, new Services.Glasses.HeyCyan.HeyCyanScanResult(name ?? "HeyCyan", address, rssi));
    }

    private void OnScanFailed(ScanFailure errorCode)
    {
        TaskCompletionSource<bool>? tcs;
        lock (_gate)
        {
            tcs = _scanTcs;
        }

        tcs?.TrySetException(new InvalidOperationException($"HeyCyan BLE scan failed with code {errorCode}"));
        CompleteScan(false, CancellationToken.None);
    }

    private void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
    {
        Log.Info(LogTag, $"Connection state change status={status} newState={newState}");

        if (newState == ProfileState.Connected && status == GattStatus.Success)
        {
            Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Connecting);
            try { gatt?.RequestMtu(247); } catch { }

            if (gatt is null || !gatt.DiscoverServices())
                FailConnect(new InvalidOperationException("BLE service discovery could not be started"));

            return;
        }

        if (newState == ProfileState.Disconnected)
        {
            var wasConnecting = _connectTcs is not null;
            CloseGatt();
            if (wasConnecting)
                FailConnect(new InvalidOperationException($"BLE disconnected while connecting, status={status}"));
            else
                Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Disconnected);
        }
    }

    private void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
    {
        Log.Info(LogTag, $"Services discovered status={status}");

        if (gatt is null || status != GattStatus.Success)
        {
            FailConnect(new InvalidOperationException($"BLE service discovery failed with status {status}"));
            return;
        }

        var service = gatt.GetService(SerialPortServiceUuid);
        var notify = service?.GetCharacteristic(SerialPortNotifyUuid);
        var write = service?.GetCharacteristic(SerialPortWriteUuid);

        if (service is null || notify is null || write is null)
        {
            LogKnownServices(gatt);
            FailConnect(new InvalidOperationException("HeyCyan serial-port BLE service/characteristics were not found"));
            return;
        }

        lock (_gate)
        {
            _gatt = gatt;
            _notifyCharacteristic = notify;
            _writeCharacteristic = write;
        }

        if (!gatt.SetCharacteristicNotification(notify, true))
        {
            FailConnect(new InvalidOperationException("Android rejected BLE notification enable"));
            return;
        }

        var descriptor = notify.GetDescriptor(ClientCharacteristicConfigUuid);
        if (descriptor is null)
        {
            CompleteConnect();
            return;
        }

        descriptor.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
        if (!gatt.WriteDescriptor(descriptor))
            FailConnect(new InvalidOperationException("Android rejected BLE notification descriptor write"));
    }

    private void OnDescriptorWrite(BluetoothGattDescriptor? descriptor, GattStatus status)
    {
        if (descriptor?.Uuid?.Equals(ClientCharacteristicConfigUuid) != true)
            return;

        if (status != GattStatus.Success)
        {
            FailConnect(new InvalidOperationException($"BLE notification descriptor write failed with status {status}"));
            return;
        }

        CompleteConnect();
    }

    private void OnCharacteristicWrite(GattStatus status)
    {
        TaskCompletionSource<GattStatus>? tcs;
        lock (_gate)
        {
            tcs = _writeTcs;
        }

        tcs?.TrySetResult(status);
    }

    private void OnCharacteristicChanged(BluetoothGattCharacteristic? characteristic, byte[]? value)
    {
        if (value is null || value.Length == 0)
            return;

        var characteristicUuid = characteristic?.Uuid?.ToString() ?? "unknown";
        Log.Verbose(LogTag, $"BLE notify {characteristicUuid} {value.Length} bytes {ToHex(value)}");

        if (characteristic?.Uuid?.Equals(SerialPortNotifyUuid) != true)
            return;

        List<byte[]> completed = [];
        lock (_receiveBuffer)
        {
            _receiveBuffer.AddRange(value);
            while (Services.Glasses.HeyCyan.HeyCyanDirectBleProtocol.TryExtractCompleteFrame(
                _receiveBuffer,
                out var frame,
                DateTimeOffset.UtcNow,
                ref _receiveStartedAt))
            {
                completed.Add(frame);
            }
        }

        foreach (var frame in completed)
            ProcessFrame(frame);
    }

    private void ProcessFrame(byte[] frame)
    {
        if (!Services.Glasses.HeyCyan.HeyCyanDirectBleProtocol.TryDecodeFrame(
            frame,
            out var action,
            out _))
        {
            return;
        }

        if (Services.Glasses.HeyCyan.HeyCyanDirectBleProtocol.TryBuildRawNotify(frame, out var loadData))
            ProcessRawNotify(loadData);

        var responsePayload = Services.Glasses.HeyCyan.HeyCyanDirectBleProtocol.BuildSessionPayload(frame);
        if (_pending.TryGetValue(action, out var tcs))
            tcs.TrySetResult(new Services.Glasses.HeyCyan.HeyCyanResponse(action, responsePayload));
    }

    private void ProcessRawNotify(byte[] loadData)
    {
        if (Services.Glasses.HeyCyan.HeyCyanDirectBleProtocol.TryBuildButtonEvent(loadData, out var gesture))
        {
            Raise(ButtonPressed, new Services.Glasses.HeyCyan.HeyCyanButtonEvent(
                gesture,
                DateTimeOffset.UtcNow));
            return;
        }

        Raise(RawNotify, new Services.Glasses.HeyCyan.HeyCyanRawNotify(loadData));
    }

    private void CompleteConnect()
    {
        TaskCompletionSource<bool>? tcs;
        lock (_gate)
        {
            tcs = _connectTcs;
            _connectTcs = null;
            try { _connectRegistration.Dispose(); } catch { }
            _connectRegistration = default;
        }

        Log.Info(LogTag, "HeyCyan BLE serial-port notifications enabled");
        Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Connected);
        tcs?.TrySetResult(true);
    }

    private void FailConnect(Exception ex)
    {
        TaskCompletionSource<bool>? tcs;
        lock (_gate)
        {
            tcs = _connectTcs;
            _connectTcs = null;
            try { _connectRegistration.Dispose(); } catch { }
            _connectRegistration = default;
        }

        Log.Warn(LogTag, $"BLE connect failed: {ex.Message}");
        tcs?.TrySetException(ex);
        CloseGatt();
        Raise(ConnectionStateChanged, Services.Glasses.HeyCyan.HeyCyanConnectionState.Disconnected);
    }

    private void CloseGatt()
    {
        BluetoothGatt? gatt;
        lock (_gate)
        {
            gatt = _gatt;
            _gatt = null;
            _gattCallback = null;
            _notifyCharacteristic = null;
            _writeCharacteristic = null;
            _writeTcs?.TrySetException(new InvalidOperationException("BLE disconnected"));
            _writeTcs = null;
            _receiveBuffer.Clear();
            _receiveStartedAt = null;
        }

        if (gatt is null)
            return;

        try { gatt.Disconnect(); } catch { }
        try { gatt.Close(); } catch { }
    }

    private void LogKnownServices(BluetoothGatt gatt)
    {
        try
        {
            foreach (var service in gatt.Services ?? [])
            {
                Log.Warn(LogTag, $"GATT service {service.Uuid}");
                foreach (var characteristic in service.Characteristics ?? [])
                    Log.Warn(LogTag, $"  characteristic {characteristic.Uuid} props={characteristic.Properties}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Could not log GATT services: {ex.Message}");
        }
    }

    private static TimeSpan ResponseTimeoutFor(byte action) =>
        action switch
        {
            Services.Glasses.HeyCyan.HeyCyanCommands.ActionBattery => TimeSpan.FromSeconds(2),
            Services.Glasses.HeyCyan.HeyCyanCommands.ActionDeviceInfo => TimeSpan.FromSeconds(2),
            Services.Glasses.HeyCyan.HeyCyanCommands.ActionDeviceConfig => TimeSpan.FromSeconds(2),
            Services.Glasses.HeyCyan.HeyCyanCommands.ActionGlassesControl => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromMilliseconds(500)
        };

    private static IEnumerable<byte[]> Chunk(byte[] payload, int length)
    {
        for (var offset = 0; offset < payload.Length; offset += length)
        {
            var count = Math.Min(length, payload.Length - offset);
            var chunk = new byte[count];
            Buffer.BlockCopy(payload, offset, chunk, 0, count);
            yield return chunk;
        }
    }

    private static bool IsHeyCyanName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (name.StartsWith("QC", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("O_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("M01", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cyan", StringComparison.OrdinalIgnoreCase));

    private static string ToHex(byte[] bytes) =>
        Convert.ToHexString(bytes);

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HeyCyanSdkBridge));
    }

    private void Raise<T>(EventHandler<T>? handler, T arg)
    {
        if (handler is null)
            return;

        _dispatcher.Post(_ => handler.Invoke(this, arg), null);
    }

    private sealed class DirectScanCallback(
        Action<BluetoothDevice?, string?, int> onDevice,
        Action<ScanFailure> onFailed) : ScanCallback
    {
        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            if (result is null)
                return;

            onDevice(result.Device, result.ScanRecord?.DeviceName, result.Rssi);
        }

        public override void OnBatchScanResults(IList<ScanResult>? results)
        {
            if (results is null)
                return;

            foreach (var result in results)
            {
                if (result is null)
                    continue;

                onDevice(result.Device, result.ScanRecord?.DeviceName, result.Rssi);
            }
        }

        public override void OnScanFailed(ScanFailure errorCode)
        {
            onFailed(errorCode);
        }
    }

    private sealed class DirectGattCallback(HeyCyanSdkBridge owner) : BluetoothGattCallback
    {
        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
            => owner.OnConnectionStateChange(gatt, status, newState);

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
            => owner.OnServicesDiscovered(gatt, status);

        public override void OnDescriptorWrite(
            BluetoothGatt? gatt,
            BluetoothGattDescriptor? descriptor,
            GattStatus status)
            => owner.OnDescriptorWrite(descriptor, status);

        public override void OnCharacteristicWrite(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic,
            GattStatus status)
            => owner.OnCharacteristicWrite(status);

        public override void OnCharacteristicChanged(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic)
            => owner.OnCharacteristicChanged(characteristic, characteristic?.GetValue());

        public override void OnCharacteristicChanged(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic,
            byte[]? value)
            => owner.OnCharacteristicChanged(characteristic, value);
    }
}
#endif
