using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using BodyCam.Services.Glasses.HeyCyan;

namespace BodyCam.Platforms.Windows.HeyCyan;

/// <summary>
/// Windows implementation of <see cref="IHeyCyanGlassesSession"/> using WinRT BLE APIs.
/// Replaces <see cref="NullHeyCyanGlassesSession"/> on Windows 10/11.
/// </summary>
internal sealed class WindowsHeyCyanGlassesSession : IHeyCyanGlassesSession
{
    // Serial Port Service (HeyCyan proprietary — from M36 Phase 1 extraction)
    // Used post-connection for GATT commands (NOT advertised in scan)
    private static readonly Guid SerialPortService = Guid.Parse("de5bf728-d711-4e47-af26-65e3012a5dc7");
    private static readonly Guid SerialPortCharWrite = Guid.Parse("de5bf72a-d711-4e47-af26-65e3012a5dc7");
    private static readonly Guid SerialPortCharNotify = Guid.Parse("de5bf729-d711-4e47-af26-65e3012a5dc7");

    // Advertised BLE service UUIDs (used for scan filtering)
    private static readonly Guid QcSdkServiceUuid1 = Guid.Parse("7905fff0-b5ce-4e99-a40f-4b1e122d00d0");
    private static readonly Guid QcSdkServiceUuid2 = Guid.Parse("6e40fff0-b5a3-f393-e0a9-e50e24dcca9e");

    // Device Information Service (standard SIG)
    private static readonly Guid DeviceInfoService = Guid.Parse("0000180a-0000-1000-8000-00805f9b34fb");
    private static readonly Guid CharFirmwareRevision = Guid.Parse("00002a26-0000-1000-8000-00805f9b34fb");
    private static readonly Guid CharHardwareRevision = Guid.Parse("00002a27-0000-1000-8000-00805f9b34fb");

    private readonly ILogger<WindowsHeyCyanGlassesSession> _log;
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    private BluetoothLEDevice? _bleDevice;
    private GattCharacteristic? _txCharacteristic;
    private GattCharacteristic? _rxCharacteristic;

    // Pending one-shot response waiters keyed by notify type byte
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<byte[]>> _pendingResponses = new();

    private HeyCyanState _state = HeyCyanState.Disconnected;
    private HeyCyanMediaCount? _lastMediaCount;

    public WindowsHeyCyanGlassesSession(ILogger<WindowsHeyCyanGlassesSession> log)
    {
        _log = log;
    }

    public HeyCyanState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            StateChanged?.Invoke(this, value);
        }
    }

    public HeyCyanDeviceInfo? Device { get; private set; }
    public HeyCyanMediaCount? LastMediaCount => _lastMediaCount;

    public event EventHandler<HeyCyanState>? StateChanged;
    public event EventHandler<HeyCyanBattery>? BatteryUpdated;
    public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
    public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
    public event EventHandler<byte[]>? AiPhotoReceived;

    // ── Scan ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan timeout, CancellationToken ct)
    {
        var prev = State;
        State = HeyCyanState.Scanning;

        var devices = new ConcurrentDictionary<ulong, HeyCyanDeviceInfo>();
        // Track names across multiple advertisements (name may arrive in scan response, not initial ad)
        var deviceNames = new ConcurrentDictionary<ulong, string>();
        int totalAds = 0;
        int namedAds = 0;
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };

        watcher.Received += (_, args) =>
        {
            Interlocked.Increment(ref totalAds);
            var name = args.Advertisement.LocalName;

            // Accumulate names across advertisements — a device may advertise
            // without a name initially, then include it in a scan response.
            if (!string.IsNullOrEmpty(name))
            {
                Interlocked.Increment(ref namedAds);
                deviceNames[args.BluetoothAddress] = name;
            }
            else if (deviceNames.TryGetValue(args.BluetoothAddress, out var cached))
            {
                name = cached;
            }
            else
            {
                return; // No name yet for this device — skip until we learn it
            }

            // Accept devices by advertised service UUID or device name prefix.
            // Glasses advertise QcSdkServiceUuid1/2; device names vary by model:
            // "QC_xxxx", "O_xxxx" (legacy), "M01 Pro_xxxx" / "M01_xxxx" (M01 model).
            var serviceUuids = args.Advertisement.ServiceUuids;
            var hasService = serviceUuids.Contains(QcSdkServiceUuid1)
                          || serviceUuids.Contains(QcSdkServiceUuid2)
                          || serviceUuids.Contains(SerialPortService);
            var nameMatch = name.StartsWith("QC", StringComparison.OrdinalIgnoreCase)
                         || name.StartsWith("O_", StringComparison.OrdinalIgnoreCase)
                         || name.StartsWith("M01", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("Cyan", StringComparison.OrdinalIgnoreCase);

            if (!hasService && !nameMatch) return;

            if (!devices.ContainsKey(args.BluetoothAddress))
                _log.LogInformation("BLE scan matched: {Name} ({Address}) RSSI={Rssi}",
                    name, FormatBluetoothAddress(args.BluetoothAddress), args.RawSignalStrengthInDBm);

            var address = FormatBluetoothAddress(args.BluetoothAddress);
            var info = new HeyCyanDeviceInfo(name, address, (int)args.RawSignalStrengthInDBm);
            devices[args.BluetoothAddress] = info;
        };

        watcher.Start();
        try
        {
            await Task.Delay(timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Scan was stopped early — return whatever we found so far
        }
        finally
        {
            watcher.Stop();
            State = HeyCyanState.Disconnected;
        }

        var results = devices.Values.ToList();
        _log.LogInformation("BLE scan complete: {Count} device(s) matched, {Total} ads received ({Named} with names, {UniqueNames} unique named devices)",
            results.Count, totalAds, namedAds, deviceNames.Count);
        return results;
    }

    // ── Connect ─────────────────────────────────────────────────────────────

    public async Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
    {
        State = HeyCyanState.Connecting;
        Device = device;

        try
        {
            var address = ParseBluetoothAddress(device.Address);

            _bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(ct).ConfigureAwait(false);
            if (_bleDevice is null)
                throw new InvalidOperationException($"Could not connect to BLE device {device.Address}");

            _bleDevice.ConnectionStatusChanged += OnConnectionStatusChanged;

            // Discover Serial Port Service (Uncached — cached mode can return empty on first connect)
            var serviceResult = await _bleDevice.GetGattServicesForUuidAsync(
                SerialPortService, BluetoothCacheMode.Uncached)
                .AsTask(ct).ConfigureAwait(false);
            if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
                throw new InvalidOperationException($"Serial port service not found on {device.Name}");

            var service = serviceResult.Services[0];
            var charsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached)
                .AsTask(ct).ConfigureAwait(false);
            if (charsResult.Status != GattCommunicationStatus.Success)
                throw new InvalidOperationException("Failed to discover GATT characteristics");

            foreach (var c in charsResult.Characteristics)
            {
                if (c.Uuid == SerialPortCharWrite) _txCharacteristic = c;
                if (c.Uuid == SerialPortCharNotify) _rxCharacteristic = c;
            }

            if (_txCharacteristic is null)
                throw new InvalidOperationException("Write characteristic not found");
            if (_rxCharacteristic is null)
                throw new InvalidOperationException("Notify characteristic not found");

            // Subscribe to notifications
            var notifyResult = await _rxCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct).ConfigureAwait(false);
            if (notifyResult != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"Failed to enable notifications: {notifyResult}");

            _rxCharacteristic.ValueChanged += OnCharacteristicValueChanged;

            // Attempt BLE-level pairing (for GATT bonding, separate from Classic BT audio)
            var pairing = _bleDevice.DeviceInformation.Pairing;
            if (!pairing.IsPaired && pairing.CanPair)
            {
                _log.LogInformation("BLE pairing with {Name}…", device.Name);
                var pairResult = await pairing.PairAsync().AsTask(ct).ConfigureAwait(false);
                if (pairResult.Status == global::Windows.Devices.Enumeration.DevicePairingResultStatus.Paired
                    || pairResult.Status == global::Windows.Devices.Enumeration.DevicePairingResultStatus.AlreadyPaired)
                {
                    _log.LogInformation("BLE paired successfully with {Name}", device.Name);
                }
                else
                {
                    _log.LogWarning("BLE pairing with {Name} returned {Status}",
                        device.Name, pairResult.Status);
                }
            }

            // Classic BT pairing AFTER BLE GATT setup is complete — running in parallel
            // disrupts GATT service discovery (see RCA: serial-port-service-not-found).
            var classicPaired = await TryPairClassicAsync(address, ct).ConfigureAwait(false);
            if (classicPaired)
                _log.LogInformation("Classic BT paired with {Name} — audio endpoint should appear", device.Name);
            else
                _log.LogWarning("Classic BT pairing failed for {Name} — live audio may be unavailable", device.Name);

            State = HeyCyanState.Connected;
            _log.LogInformation("Connected to {Name} ({Address})", device.Name, device.Address);
        }
        catch
        {
            await CleanupDeviceAsync().ConfigureAwait(false);
            State = HeyCyanState.Disconnected;
            throw;
        }
    }

    /// <summary>
    /// Attempt to discover and pair the glasses as a Classic Bluetooth device (HFP/A2DP)
    /// so Windows creates an audio endpoint. Returns true if paired or already paired.
    /// </summary>
    private async Task<bool> TryPairClassicAsync(ulong bleAddress, CancellationToken ct)
    {
        try
        {
            // Strategy 1: Direct lookup by BLE address — on dual-mode devices the
            // Classic BT address is often identical to the BLE address.
            var classicDevice = await BluetoothDevice.FromBluetoothAddressAsync(bleAddress)
                .AsTask(ct).ConfigureAwait(false);

            if (classicDevice is null)
            {
                _log.LogDebug("No Classic BT device found at BLE address {Address}", FormatBluetoothAddress(bleAddress));

                // Strategy 2: Enumerate unpaired Classic BT devices, match by name prefix.
                var aqsFilter = BluetoothDevice.GetDeviceSelectorFromPairingState(false);
                var devices = await global::Windows.Devices.Enumeration.DeviceInformation
                    .FindAllAsync(aqsFilter).AsTask(ct).ConfigureAwait(false);
                var match = devices.FirstOrDefault(d =>
                    d.Name.StartsWith("M01", StringComparison.OrdinalIgnoreCase)
                    || d.Name.StartsWith("QC", StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    _log.LogDebug("No matching Classic BT device found via enumeration");
                    return false;
                }

                classicDevice = await BluetoothDevice.FromIdAsync(match.Id)
                    .AsTask(ct).ConfigureAwait(false);
                if (classicDevice is null)
                    return false;
            }

            var pairing = classicDevice.DeviceInformation.Pairing;
            if (pairing.IsPaired)
            {
                _log.LogDebug("Classic BT device already paired");
                return true;
            }

            if (!pairing.CanPair)
            {
                _log.LogDebug("Classic BT device cannot be paired");
                return false;
            }

            // The glasses are slow to respond to Classic BT pairing — retry up to 5 times.
            // Each PairAsync attempt times out after ~10-15s on AuthenticationTimeout,
            // so re-acquire the device on each retry for a fresh state.
            const int maxAttempts = 5;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                if (attempt > 1)
                {
                    _log.LogDebug("Classic BT pairing attempt {Attempt}/{Max} — retrying after delay",
                        attempt, maxAttempts);
                    await Task.Delay(3000, ct).ConfigureAwait(false);

                    // Re-acquire device for fresh pairing state
                    classicDevice = await BluetoothDevice.FromBluetoothAddressAsync(bleAddress)
                        .AsTask(ct).ConfigureAwait(false);
                    if (classicDevice is null)
                        return false;

                    pairing = classicDevice.DeviceInformation.Pairing;
                    if (pairing.IsPaired)
                    {
                        _log.LogDebug("Classic BT device became paired between attempts");
                        return true;
                    }
                }

                var customPairing = pairing.Custom;
                customPairing.PairingRequested += (_, args) => args.Accept();

                var result = await customPairing.PairAsync(
                    global::Windows.Devices.Enumeration.DevicePairingKinds.ConfirmOnly)
                    .AsTask(ct).ConfigureAwait(false);

                if (result.Status == global::Windows.Devices.Enumeration.DevicePairingResultStatus.Paired
                    || result.Status == global::Windows.Devices.Enumeration.DevicePairingResultStatus.AlreadyPaired)
                {
                    _log.LogInformation("Classic BT paired on attempt {Attempt}", attempt);
                    return true;
                }

                _log.LogDebug("Classic BT pairing attempt {Attempt} returned {Status}",
                    attempt, result.Status);
            }

            _log.LogWarning("Classic BT pairing failed after {Max} attempts", maxAttempts);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Classic BT pairing attempt failed");
            return false;
        }
    }

    // ── Disconnect ──────────────────────────────────────────────────────────

    public async Task DisconnectAsync(CancellationToken ct)
    {
        State = HeyCyanState.Disconnecting;
        await CleanupDeviceAsync().ConfigureAwait(false);
        Device = null;
        State = HeyCyanState.Disconnected;
        _log.LogInformation("Disconnected from glasses");
    }

    // ── Commands ────────────────────────────────────────────────────────────

    public async Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken ct)
    {
        // Read from standard Device Information Service
        string firmware = "unknown", hardware = "unknown";
        string macAddress = Device?.Address ?? "00:00:00:00:00:00";

        if (_bleDevice is not null)
        {
            firmware = await ReadDeviceInfoCharAsync(CharFirmwareRevision, ct).ConfigureAwait(false) ?? "unknown";
            hardware = await ReadDeviceInfoCharAsync(CharHardwareRevision, ct).ConfigureAwait(false) ?? "unknown";
        }

        return new HeyCyanVersionInfo(hardware, firmware, "unknown", "unknown", macAddress);
    }

    public async Task<HeyCyanBattery> GetBatteryAsync(CancellationToken ct)
    {
        // Send battery poll command and wait for notify response (type 0x05)
        await SendCommandAsync(HeyCyanCommands.GetBattery(), ct).ConfigureAwait(false);

        try
        {
            var payload = await WaitForNotifyAsync(0x05, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            return HeyCyanFrameParser.ParseBattery(payload);
        }
        catch (TimeoutException)
        {
            _log.LogWarning("Battery response timed out");
            return new HeyCyanBattery(0, false);
        }
    }

    public Task SyncTimeAsync(CancellationToken ct)
        => SendCommandAsync(HeyCyanCommands.SyncTime(DateTimeOffset.UtcNow), ct);

    public Task TakePhotoAsync(CancellationToken ct)
        => SendCommandAsync(HeyCyanCommands.StartPhotoMode(), ct);

    public Task StartVideoAsync(CancellationToken ct)
        => SendCommandAsync(HeyCyanCommands.StartVideoRecording(), ct);

    public Task StopVideoAsync(CancellationToken ct)
        => SendCommandAsync(HeyCyanCommands.StopMode(), ct);

    public Task StartAudioAsync(CancellationToken ct)
        => SendCommandAsync(HeyCyanCommands.StartAudioRecording(), ct);

    public Task StopAudioAsync(CancellationToken ct)
        => SendCommandAsync(HeyCyanCommands.StopMode(), ct);

    public Task TakeAiPhotoAsync(CancellationToken ct)
        => SendCommandAsync(HeyCyanCommands.TakeAiPhoto(), ct);

    public async Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
    {
        await SendCommandAsync(HeyCyanCommands.EnterTransferMode(), ct).ConfigureAwait(false);
        State = HeyCyanState.TransferMode;

        // Wait for the IP address notify frame (type 0x08)
        IPAddress? glassesIp;
        try
        {
            var frame = await WaitForNotifyAsync(0x08, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            if (!HeyCyanFrameParser.TryParseTransferIp(frame, out glassesIp) || glassesIp is null)
                throw new InvalidOperationException("Failed to parse glasses IP from notify frame");
        }
        catch
        {
            State = HeyCyanState.Connected;
            throw;
        }

        var baseUrl = $"http://{glassesIp}";
        _log.LogInformation("Transfer mode active, glasses at {BaseUrl}", baseUrl);
        return new HeyCyanTransferSession(baseUrl, Array.Empty<string>());
    }

    public async Task ExitTransferModeAsync(CancellationToken ct)
    {
        await SendCommandAsync(HeyCyanCommands.ExitTransferMode(), ct).ConfigureAwait(false);
        State = HeyCyanState.Connected;
    }

    // ── BLE helpers ─────────────────────────────────────────────────────────

    private async Task SendCommandAsync(byte[] command, CancellationToken ct)
    {
        if (_txCharacteristic is null)
            throw new InvalidOperationException("Not connected — no write characteristic available");

        await _commandLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var writer = new DataWriter();
            writer.WriteBytes(command);
            var result = await _txCharacteristic.WriteValueWithResultAsync(
                writer.DetachBuffer(),
                GattWriteOption.WriteWithoutResponse).AsTask(ct).ConfigureAwait(false);

            if (result.Status != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"BLE write failed: {result.Status}");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task<string?> ReadDeviceInfoCharAsync(Guid charUuid, CancellationToken ct)
    {
        if (_bleDevice is null) return null;

        try
        {
            var svcResult = await _bleDevice.GetGattServicesForUuidAsync(DeviceInfoService)
                .AsTask(ct).ConfigureAwait(false);
            if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
                return null;

            var charsResult = await svcResult.Services[0].GetCharacteristicsForUuidAsync(charUuid)
                .AsTask(ct).ConfigureAwait(false);
            if (charsResult.Status != GattCommunicationStatus.Success || charsResult.Characteristics.Count == 0)
                return null;

            var readResult = await charsResult.Characteristics[0].ReadValueAsync()
                .AsTask(ct).ConfigureAwait(false);
            if (readResult.Status != GattCommunicationStatus.Success)
                return null;

            var reader = DataReader.FromBuffer(readResult.Value);
            return reader.ReadString(reader.UnconsumedBufferLength);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to read Device Info char {Uuid}", charUuid);
            return null;
        }
    }

    private Task<byte[]> WaitForNotifyAsync(byte notifyType, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponses[notifyType] = tcs;

        ct.Register(() => tcs.TrySetCanceled(ct));

        return Task.Run(async () =>
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout, ct)).ConfigureAwait(false);
            _pendingResponses.TryRemove(notifyType, out _);

            if (completed != tcs.Task)
                throw new TimeoutException($"No response for notify type 0x{notifyType:X2} within {timeout.TotalSeconds}s");

            return await tcs.Task.ConfigureAwait(false);
        }, ct);
    }

    // ── Notification handler ────────────────────────────────────────────────

    private void OnCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var bytes = args.CharacteristicValue.ToArray();
        if (bytes.Length < 7)
        {
            _log.LogDebug("Notify frame too short ({Length} bytes)", bytes.Length);
            return;
        }

        var notifyType = bytes[6];

        // Complete any pending one-shot waiter for this type
        if (_pendingResponses.TryRemove(notifyType, out var tcs))
        {
            tcs.TrySetResult(bytes);
            return;
        }

        // Dispatch by type
        switch (notifyType)
        {
            case 0x02: // AI-photo button
                ButtonPressed?.Invoke(this, new HeyCyanButtonEvent(HeyCyanButtonGesture.Tap, DateTimeOffset.UtcNow));
                break;

            case 0x03: // AI-voice button
                ButtonPressed?.Invoke(this, new HeyCyanButtonEvent(HeyCyanButtonGesture.DoubleTap, DateTimeOffset.UtcNow));
                break;

            case 0x05: // Battery
                if (bytes.Length >= 9)
                {
                    var battery = HeyCyanFrameParser.ParseBattery(bytes.AsSpan(7, 2).ToArray());
                    _log.LogDebug("Battery: {Pct}% charging={Charging}", battery.Percentage, battery.IsCharging);
                    BatteryUpdated?.Invoke(this, battery);
                }
                break;

            case 0x08: // Transfer IP
                _log.LogDebug("Transfer IP notify received");
                break;

            case 0x09: // P2P error
                var errorKind = HeyCyanFrameParser.ClassifyP2pError(bytes);
                if (errorKind == HeyCyanP2pErrorKind.Fatal)
                    _log.LogWarning("P2P fatal error in notify frame");
                else
                    _log.LogDebug("P2P noisy error (0xFF) — ignoring");
                break;

            default:
                _log.LogDebug("Unknown notify type 0x{Type:X2}", notifyType);
                break;
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            _log.LogWarning("BLE device disconnected unexpectedly");
            _ = Task.Run(async () =>
            {
                await CleanupDeviceAsync().ConfigureAwait(false);
                State = HeyCyanState.Disconnected;
            });
        }
    }

    // ── Cleanup ─────────────────────────────────────────────────────────────

    private async Task CleanupDeviceAsync()
    {
        if (_rxCharacteristic is not null)
        {
            _rxCharacteristic.ValueChanged -= OnCharacteristicValueChanged;
            try
            {
                await _rxCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None).AsTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Failed to unsubscribe from notifications");
            }
            _rxCharacteristic = null;
        }

        _txCharacteristic = null;

        if (_bleDevice is not null)
        {
            _bleDevice.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _bleDevice.Dispose();
            _bleDevice = null;
        }

        // Cancel all pending waiters
        foreach (var kvp in _pendingResponses)
        {
            kvp.Value.TrySetCanceled();
            _pendingResponses.TryRemove(kvp.Key, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupDeviceAsync().ConfigureAwait(false);
        _commandLock.Dispose();
    }

    // ── Utilities ───────────────────────────────────────────────────────────

    private static string FormatBluetoothAddress(ulong address)
    {
        var bytes = BitConverter.GetBytes(address);
        return $"{bytes[5]:X2}:{bytes[4]:X2}:{bytes[3]:X2}:{bytes[2]:X2}:{bytes[1]:X2}:{bytes[0]:X2}";
    }

    private static ulong ParseBluetoothAddress(string address)
    {
        var parts = address.Split(':');
        if (parts.Length != 6)
            throw new FormatException($"Invalid Bluetooth address: {address}");

        ulong result = 0;
        for (int i = 0; i < 6; i++)
            result = (result << 8) | Convert.ToByte(parts[i], 16);
        return result;
    }
}
