using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Platform-neutral core session logic, testable without Android dependencies.
/// AndroidHeyCyanGlassesSession wraps this with #if ANDROID and DI ctor.
/// </summary>
internal sealed class HeyCyanGlassesSessionCore : Mvvm.ViewModelBase, IHeyCyanGlassesSession
{
    private static readonly System.Net.IPAddress DefaultP2pProbeSeed = System.Net.IPAddress.Parse("192.168.49.183");

    private readonly IHeyCyanSdkBridge _bridge;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private HeyCyanState _state = HeyCyanState.Disconnected;
    private HeyCyanDeviceInfo? _device;
    private HeyCyanMediaCount? _lastMediaCount;
    private CancellationTokenSource? _transferKeepaliveCts;
    private Task? _transferKeepaliveTask;

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

    public HeyCyanMediaCount? LastMediaCount
    {
        get => _lastMediaCount;
        private set
        {
            if (SetProperty(ref _lastMediaCount, value) && value is not null)
                MediaCountUpdated?.Invoke(this, value);
        }
    }

    public event EventHandler<HeyCyanState>? StateChanged;
    public event EventHandler<HeyCyanBattery>? BatteryUpdated;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    public event EventHandler<byte[]>? AiPhotoReceived;

    public HeyCyanGlassesSessionCore(IHeyCyanSdkBridge bridge, ILogger log)
    {
        _bridge = bridge;
        _log = log;
        _bridge.ButtonPressed += (s, e) => ButtonPressed?.Invoke(this, e);
        _bridge.ConnectionStateChanged += OnBridgeConnectionStateChanged;
    }

    public async Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct)
    {
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
            try
            {
                await Task.Delay(timeout, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // timeout
            }
        }
        finally
        {
            _bridge.DeviceDiscovered -= OnDiscovered;
            await _bridge.StopScanAsync().ConfigureAwait(false);
            if (State == HeyCyanState.Scanning)
                State = HeyCyanState.Disconnected;
        }
        return seen.Values.ToArray();
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

    public async Task DisconnectAsync(CancellationToken ct)
    {
        if (State is HeyCyanState.Disconnected)
            return;

        State = HeyCyanState.Disconnecting;
        await StopTransferKeepaliveAsync().ConfigureAwait(false);
        await _bridge.DisconnectAsync().ConfigureAwait(false);
        State = HeyCyanState.Disconnected;
        Device = null;
    }

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

    public async Task TakePhotoAsync(CancellationToken ct)
    {
        // Send BLE start photo mode command (0x02, 0x01, 0x01) and await response
        var resp = await _bridge.SendAsync(HeyCyanCommands.StartPhotoMode(), ct).ConfigureAwait(false);
        
        // Parse response to update media count if available
        if (resp.Payload.Length >= 12)
        {
            try
            {
                LastMediaCount = HeyCyanFrameParser.ParseMediaCounts(resp.Payload);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to parse media count from photo response");
            }
        }
    }

    public Task StartVideoAsync(CancellationToken ct)
        => _bridge.SendAsync(HeyCyanCommands.StartVideoRecording(), ct);

    public Task StopVideoAsync(CancellationToken ct)
        => _bridge.SendAsync(HeyCyanCommands.StopVideoRecording(), ct);

    public Task StartAudioAsync(CancellationToken ct)
        => throw new NotImplementedException("M33 Phase 5 — recorded OPUS, not live mic");

    public Task StopAudioAsync(CancellationToken ct)
        => throw new NotImplementedException("M33 Phase 5");

    public Task TakeAiPhotoAsync(CancellationToken ct)
        => throw new NotImplementedException("M33 Phase 2");

    public async Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
    {
        if (State != HeyCyanState.Connected)
            throw new InvalidOperationException($"Cannot enter transfer mode from state {State}");

        State = HeyCyanState.TransferMode;
        try
        {
            await StopTransferKeepaliveAsync().ConfigureAwait(false);

            // Subscribe before sending so a synchronous vendor callback cannot be missed.
            var ipTcs = new TaskCompletionSource<System.Net.IPAddress>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            void OnRawNotify(object? s, HeyCyanRawNotify e)
            {
                if (HeyCyanFrameParser.TryParseTransferIp(e.LoadData, out var ip) && ip is not null)
                {
                    ipTcs.TrySetResult(ip);
                }
                else
                {
                    var errorKind = HeyCyanFrameParser.ClassifyP2pError(e.LoadData);
                    if (errorKind == HeyCyanP2pErrorKind.Fatal)
                    {
                        var code = e.LoadData.Length >= 8 ? e.LoadData[7] : (byte)0;
                        ipTcs.TrySetException(new InvalidOperationException($"P2P error code 0x{code:X2}"));
                    }
                    // Noisy (0xFF) errors are logged and ignored
                    else if (errorKind == HeyCyanP2pErrorKind.Noisy)
                    {
                        _log.LogInformation("P2P transient noise (0x09 0xFF) during transfer mode entry");
                    }
                }
            }

            _bridge.RawNotify += OnRawNotify;
            try
            {
                // Step 1: Send BLE media transfer command (0x02, 0x01, 0x04).
                _ = await _bridge.SendAsync(HeyCyanCommands.EnterTransferMode(), ct).ConfigureAwait(false);

                // Step 2: Ask for the Wi-Fi IP. Some firmware reports it through a
                // GlassesDeviceNotifyRsp 0x08 frame; some only responds after polling.
                for (var attempt = 0; attempt < 3 && !ipTcs.Task.IsCompleted; attempt++)
                {
                    try
                    {
                        _ = await _bridge.SendAsync(HeyCyanCommands.GetWifiIP(), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "GetWifiIP poll failed during transfer mode entry");
                    }
                }

                // Step 3: Prefer the BLE-reported IP, but do not block the Wi-Fi
                // Direct HTTP path on it. The Android HTTP client validates the
                // requested host and probes the P2P subnet once the group forms.
                var delayTask = Task.Delay(TimeSpan.FromSeconds(2), ct);
                var completed = await Task.WhenAny(ipTcs.Task, delayTask).ConfigureAwait(false);
                var ip = completed == ipTcs.Task
                    ? await ipTcs.Task.ConfigureAwait(false)
                    : DefaultP2pProbeSeed;

                var baseUrl = $"http://{ip}/";
                
                _log.LogInformation("Transfer mode entered, initial glasses IP candidate: {Ip}", ip);

                await SendTransferActivationPulseAsync(ct).ConfigureAwait(false);
                StartTransferKeepalive();
                
                // Return session with base URL (filenames will be fetched via HTTP media.config by caller)
                return new HeyCyanTransferSession(baseUrl, Array.Empty<string>());
            }
            finally
            {
                _bridge.RawNotify -= OnRawNotify;
            }
        }
        catch
        {
            await StopTransferKeepaliveAsync().ConfigureAwait(false);
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
            await StopTransferKeepaliveAsync().ConfigureAwait(false);
            _ = await _bridge.SendAsync(HeyCyanCommands.ExitTransferMode(), ct).ConfigureAwait(false);
            _log.LogInformation("Transfer mode exited");
        }
        finally
        {
            State = HeyCyanState.Connected;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (State is HeyCyanState.Connected or HeyCyanState.Connecting)
        {
            try { await DisconnectAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogWarning(ex, "Dispose disconnect failed"); }
        }
        await StopTransferKeepaliveAsync().ConfigureAwait(false);
        _bridge.Dispose();
        _connectGate.Dispose();
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
        catch (Exception ex) { _log.LogWarning(ex, "Hydrate failed"); }
    }

    private void OnBridgeConnectionStateChanged(object? s, HeyCyanConnectionState e)
    {
        if (e == HeyCyanConnectionState.Disconnected)
            _transferKeepaliveCts?.Cancel();

        State = e switch
        {
            HeyCyanConnectionState.Disconnected => HeyCyanState.Disconnected,
            HeyCyanConnectionState.Connecting => HeyCyanState.Connecting,
            HeyCyanConnectionState.Connected => HeyCyanState.Connected,
            HeyCyanConnectionState.Disconnecting => HeyCyanState.Disconnecting,
            _ => State,
        };
        if (e == HeyCyanConnectionState.Disconnected) Device = null;
    }

    private async Task SendTransferActivationPulseAsync(CancellationToken ct)
    {
        try
        {
            _ = await _bridge.SendAsync(HeyCyanCommands.GetDeviceConfig(), ct).ConfigureAwait(false);
            _log.LogInformation("Transfer activation pulse sent");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Transfer activation pulse failed");
        }
    }

    private void StartTransferKeepalive()
    {
        var cts = new CancellationTokenSource();
        _transferKeepaliveCts = cts;
        _transferKeepaliveTask = RunTransferKeepaliveAsync(cts.Token);
    }

    private async Task RunTransferKeepaliveAsync(CancellationToken ct)
    {
        var pulse = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                pulse++;

                var command = pulse % 3 == 0
                    ? HeyCyanCommands.GetDeviceConfig()
                    : HeyCyanCommands.GetWifiIP();

                try
                {
                    _ = await _bridge.SendAsync(command, ct).ConfigureAwait(false);
                    _log.LogDebug("Transfer keepalive sent action 0x{Action:X2}", command[1]);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Transfer keepalive failed");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal transfer teardown.
        }
    }

    private async Task StopTransferKeepaliveAsync()
    {
        var cts = _transferKeepaliveCts;
        var task = _transferKeepaliveTask;
        _transferKeepaliveCts = null;
        _transferKeepaliveTask = null;

        if (cts is null)
            return;

        try
        {
            cts.Cancel();
            if (task is not null)
                await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal transfer teardown.
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Transfer keepalive cleanup failed");
        }
        finally
        {
            cts.Dispose();
        }
    }
}
