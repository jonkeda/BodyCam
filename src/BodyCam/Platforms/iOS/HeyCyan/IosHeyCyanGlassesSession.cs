#if IOS
using System.Collections.Concurrent;
using BodyCam.HeyCyan.iOS.Bindings;
using BodyCam.Services.Glasses.HeyCyan;
using CoreBluetooth;
using Foundation;
using Microsoft.Extensions.Logging;

namespace BodyCam.Platforms.iOS.HeyCyan;

/// <summary>
/// iOS implementation of IHeyCyanGlassesSession using QCSDKManager and CBCentralManager.
/// Maps QCSDK delegate callbacks and NSNotificationCenter OdmNotifyD2P frames to the
/// cross-platform IHeyCyanGlassesSession events.
/// </summary>
internal sealed class IosHeyCyanGlassesSession : NSObject, IHeyCyanGlassesSession
{
    private readonly QCSDKManagerDelegateProxy _qcDelegate;
    private readonly CBCentralManager _cbCentral;
    private readonly ILogger<IosHeyCyanGlassesSession> _log;
    private readonly ILoggerFactory _logFactory;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly NSObject _d2pObserver;
    
    private CBPeripheral? _peripheral;
    private HeyCyanState _state = HeyCyanState.Disconnected;
    private HeyCyanDeviceInfo? _device;
    private HeyCyanMediaCount? _lastMediaCount;

    public HeyCyanState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(this, value);
            }
        }
    }

    public HeyCyanDeviceInfo? Device
    {
        get => _device;
        private set => _device = value;
    }

    public HeyCyanMediaCount? LastMediaCount
    {
        get => _lastMediaCount;
        private set
        {
            _lastMediaCount = value;
            if (value is not null)
                MediaCountUpdated?.Invoke(this, value);
        }
    }

    public event EventHandler<HeyCyanState>? StateChanged;
    public event EventHandler<HeyCyanBattery>? BatteryUpdated;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    public event EventHandler<byte[]>? AiPhotoReceived;

    public IosHeyCyanGlassesSession(ILogger<IosHeyCyanGlassesSession> log, ILoggerFactory logFactory)
    {
        _log = log;
        _logFactory = logFactory;
        
        // Initialize CoreBluetooth central manager for scanning
        _cbCentral = new CBCentralManager(null, null);
        
        // Set up QCSDKManager delegate for battery/media/AI photo callbacks
        _qcDelegate = new QCSDKManagerDelegateProxy(this);
        QCSDKManager.SharedInstance.Delegate = _qcDelegate;

        // Register for OdmNotifyD2P device-to-phone notification frames (button events)
        _d2pObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            OdmBleConstants.OdmNotifyD2P,
            OnD2PNotification);
    }

    public async Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct)
    {
        State = HeyCyanState.Scanning;
        var found = new ConcurrentDictionary<string, HeyCyanDeviceInfo>();
        var tcs = new TaskCompletionSource<IReadOnlyList<HeyCyanDeviceInfo>>();

        void OnDiscovered(object? sender, CBDiscoveredPeripheralEventArgs args)
        {
            if (args.Peripheral?.Identifier?.AsString() is string id)
            {
                found[id] = new HeyCyanDeviceInfo(
                    args.Peripheral.Name ?? "(unknown)",
                    id,
                    args.RSSI?.Int32Value ?? 0);
            }
        }

        try
        {
            _cbCentral.DiscoveredPeripheral += OnDiscovered;

            // Scan for HeyCyan service UUIDs
            var uuids = new[]
            {
                CBUUID.FromString(OdmBleConstants.QcsdkServerUuid1),
                CBUUID.FromString(OdmBleConstants.QcsdkServerUuid2)
            };
            _cbCentral.ScanForPeripherals(uuids);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            
            await Task.Delay(timeout, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Scan timeout
        }
        finally
        {
            _cbCentral.StopScan();
            _cbCentral.DiscoveredPeripheral -= OnDiscovered;
            if (State == HeyCyanState.Scanning)
                State = HeyCyanState.Disconnected;
        }

        return found.Values.ToArray();
    }

    public async Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
    {
        if (!await _connectGate.WaitAsync(0, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Connect already in progress");

        try
        {
            if (State is HeyCyanState.Connected or HeyCyanState.Connecting)
                throw new InvalidOperationException("Already connected");

            State = HeyCyanState.Connecting;

            // Find the peripheral by identifier (UUID string from scan)
            _peripheral = ResolvePeripheral(device.Address)
                ?? throw new InvalidOperationException($"No peripheral found with identifier {device.Address}");

            // Connect via CBCentralManager
            var connectTcs = new TaskCompletionSource<bool>();
            void OnConnected(object? s, CBPeripheralEventArgs args)
            {
                if (args.Peripheral?.Identifier?.AsString() == device.Address)
                    connectTcs.TrySetResult(true);
            }
            void OnFailed(object? s, CBPeripheralErrorEventArgs args)
            {
                if (args.Peripheral?.Identifier?.AsString() == device.Address)
                    connectTcs.TrySetException(new IOException($"Connection failed: {args.Error?.LocalizedDescription}"));
            }

            _cbCentral.ConnectedPeripheral += OnConnected;
            _cbCentral.FailedToConnectPeripheral += OnFailed;
            try
            {
                _cbCentral.ConnectPeripheral(_peripheral);
                
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                using var reg = linkedCts.Token.Register(() => connectTcs.TrySetCanceled(linkedCts.Token));
                
                await connectTcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _cbCentral.ConnectedPeripheral -= OnConnected;
                _cbCentral.FailedToConnectPeripheral -= OnFailed;
            }

            // Register with QCSDKManager
            var addTcs = new TaskCompletionSource<bool>();
            QCSDKManager.SharedInstance.AddPeripheral(_peripheral, success => addTcs.TrySetResult(success));
            
            if (!await addTcs.Task.ConfigureAwait(false))
                throw new IOException("QCSDKManager rejected the peripheral");

            Device = device;
            State = HeyCyanState.Connected;
            
            // Fire-and-forget device info hydration
            _ = HydrateAsync();
        }
        catch
        {
            State = HeyCyanState.Disconnected;
            throw;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        if (State is HeyCyanState.Disconnected)
            return;

        State = HeyCyanState.Disconnecting;

        if (_peripheral is not null)
        {
            QCSDKManager.SharedInstance.RemovePeripheral(_peripheral);
            _cbCentral.CancelPeripheralConnection(_peripheral);
            _peripheral = null;
        }

        State = HeyCyanState.Disconnected;
        Device = null;
        
        await Task.CompletedTask;
    }

    public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<HeyCyanVersionInfo>();
        
        QCSDKCmdCreator.GetDeviceVersionInfo(
            success: (hw, fw, wifiHw, wifiFw) =>
            {
                tcs.TrySetResult(new HeyCyanVersionInfo(
                    hw?.ToString() ?? "unknown",
                    fw?.ToString() ?? "unknown",
                    wifiHw?.ToString() ?? "unknown",
                    wifiFw?.ToString() ?? "unknown",
                    Device?.MacAddress ?? "00:00:00:00:00:00"));
            },
            fail: () => tcs.TrySetException(new IOException("GetDeviceVersionInfo failed")));

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<HeyCyanBattery>();
        
        QCSDKCmdCreator.GetDeviceBattery(
            success: (level, charging) =>
            {
                tcs.TrySetResult(new HeyCyanBattery((int)level, charging));
            },
            fail: () => tcs.TrySetException(new IOException("GetDeviceBattery failed")));

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    public Task SyncTimeAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        QCSDKCmdCreator.SetupDeviceDateTime(
            finished: (success, error) =>
            {
                if (success)
                    tcs.TrySetResult(true);
                else
                    tcs.TrySetException(new IOException($"SetupDeviceDateTime failed: {error?.LocalizedDescription}"));
            });

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    public Task TakePhotoAsync(CancellationToken ct) =>
        InvokeModeAsync(QCOperatorDeviceMode.Photo, ct);

    public Task StartVideoAsync(CancellationToken ct) =>
        InvokeModeAsync(QCOperatorDeviceMode.Video, ct);

    public Task StopVideoAsync(CancellationToken ct) =>
        InvokeModeAsync(QCOperatorDeviceMode.VideoStop, ct);

    public Task StartAudioAsync(CancellationToken ct) =>
        InvokeModeAsync(QCOperatorDeviceMode.Audio, ct);

    public Task StopAudioAsync(CancellationToken ct) =>
        InvokeModeAsync(QCOperatorDeviceMode.AudioStop, ct);

    public Task TakeAiPhotoAsync(CancellationToken ct) =>
        InvokeModeAsync(QCOperatorDeviceMode.AiPhoto, ct);

    public async Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
    {
        if (State != HeyCyanState.Connected)
            throw new InvalidOperationException($"Cannot enter transfer mode from state {State}");

        State = HeyCyanState.TransferMode;

        try
        {
            // Open WiFi hotspot via BLE command
            var (ssid, password) = await OpenHotspotAsync(ct).ConfigureAwait(false);

            // Use HotspotHttpClient to join the glasses' hotspot and discover the IP
            var hotspot = new HotspotHttpClient(_logFactory.CreateLogger<HotspotHttpClient>());
            
            await hotspot.JoinAsync(ssid, password, ct).ConfigureAwait(false);
            var baseUrl = await hotspot.DiscoverGlassesIpAsync(ct).ConfigureAwait(false);
            var files = await hotspot.GetMediaConfigAsync(baseUrl, ct).ConfigureAwait(false);

            _log.LogInformation("EnterTransferModeAsync: discovered {Count} files at {BaseUrl}", files.Count, baseUrl);

            return new HeyCyanTransferSession(baseUrl, files);
        }
        catch
        {
            State = HeyCyanState.Connected;
            throw;
        }
    }

    public async Task ExitTransferModeAsync(CancellationToken ct)
    {
        if (State != HeyCyanState.TransferMode)
        {
            _log.LogWarning("ExitTransferModeAsync called from state {State}, ignoring", State);
            return;
        }

        try
        {
            await InvokeModeAsync(QCOperatorDeviceMode.TransferStop, ct).ConfigureAwait(false);
            _log.LogInformation("Transfer mode exited");
        }
        finally
        {
            State = HeyCyanState.Connected;
        }
    }

    public ValueTask DisposeAsync()
    {
        QCSDKManager.SharedInstance.Delegate = null;
        QCSDKManager.SharedInstance.RemoveAllPeripherals();
        
        if (_d2pObserver is not null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(_d2pObserver);

        _connectGate.Dispose();
        return default;
    }

    private Task InvokeModeAsync(QCOperatorDeviceMode mode, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        QCSDKCmdCreator.SetDeviceMode(
            mode,
            success: () => tcs.TrySetResult(true),
            fail: code => tcs.TrySetException(new IOException($"SetDeviceMode({mode}) failed with code {code}")));

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    private Task<(string ssid, string password)> OpenHotspotAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(string, string)>();
        
        QCSDKCmdCreator.OpenWifi(
            QCOperatorDeviceMode.Transfer,
            success: (ssidNs, passNs) =>
            {
                var ssid = ssidNs?.ToString() ?? throw new IOException("OpenWifi returned null SSID");
                var password = passNs?.ToString() ?? "123456789"; // QCSDK fallback convention
                tcs.TrySetResult((ssid, password));
            },
            fail: code => tcs.TrySetException(new IOException($"OpenWifi failed with code {code}")));

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    private CBPeripheral? ResolvePeripheral(string identifier)
    {
        // Try to retrieve the peripheral by UUID
        var uuid = new NSUuid(identifier);
        var peripherals = _cbCentral.RetrievePeripheralsWithIdentifiers(uuid);
        return peripherals?.FirstOrDefault();
    }

    private void OnD2PNotification(NSNotification notification)
    {
        try
        {
            // Extract data from notification userInfo
            if (notification.UserInfo?[OdmBleConstants.OdmNotifyD2PDataKey] is not NSData data)
                return;

            var frame = data.ToArray();
            
            // Parse button gesture using shared HeyCyanFrameParser
            if (HeyCyanFrameParser.TryParseButton(frame, out var gesture))
            {
                var evt = new HeyCyanButtonEvent(gesture, DateTimeOffset.UtcNow);
                ButtonPressed?.Invoke(this, evt);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse OdmNotifyD2P frame");
        }
    }

    private async Task HydrateAsync()
    {
        try
        {
            var ver = await GetVersionAsync(CancellationToken.None).ConfigureAwait(false);
            Device = Device! with
            {
                Firmware = ver.Firmware,
                Hardware = ver.Hardware,
                MacAddress = ver.MacAddress
            };

            var bat = await GetBatteryAsync(CancellationToken.None).ConfigureAwait(false);
            BatteryUpdated?.Invoke(this, bat);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Device hydration failed");
        }
    }

    /// <summary>
    /// Proxy that translates QCSDKManagerDelegate Objective-C callbacks into managed events.
    /// </summary>
    private sealed class QCSDKManagerDelegateProxy : QCSDKManagerDelegate
    {
        private readonly IosHeyCyanGlassesSession _owner;

        public QCSDKManagerDelegateProxy(IosHeyCyanGlassesSession owner)
        {
            _owner = owner;
        }

        public override void DidUpdateBatteryLevel(nint battery, bool charging)
        {
            var evt = new HeyCyanBattery((int)battery, charging);
            _owner.BatteryUpdated?.Invoke(_owner, evt);
        }

        public override void DidUpdateMedia(nint photoCount, nint videoCount, nint audioCount, nint type)
        {
            var count = new HeyCyanMediaCount((int)photoCount, (int)videoCount, (int)audioCount);
            _owner.LastMediaCount = count;
        }

        public override void DidReceiveAiChatImageData(NSData imageData)
        {
            _owner.AiPhotoReceived?.Invoke(_owner, imageData.ToArray());
        }
    }
}
#endif
