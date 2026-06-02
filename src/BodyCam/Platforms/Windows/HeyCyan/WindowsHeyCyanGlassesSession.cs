using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Storage.Streams;
using BodyCam.Platforms.Windows.Audio;
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
    private readonly WindowsBluetoothEnumerator _btEnumerator;
    private readonly WindowsBluetoothOutputEnumerator _btOutputEnumerator;
    private readonly WindowsGlassesWiFiManager? _wifiManager;
    private readonly IWindowsWiFiDirectConnector? _wifiDirectManager;
    private readonly bool _ensureClassicAudio;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private CancellationTokenSource? _classicAudioSetupCts;

    private BluetoothLEDevice? _bleDevice;
    private GattDeviceService? _gattService; // MUST be kept alive — GC kills notifications
    private GattSession? _gattSession; // MUST be kept alive — maintains BLE connection for notifications
    private GattCharacteristic? _txCharacteristic;
    private GattCharacteristic? _rxCharacteristic;

    // Pending one-shot response waiters keyed by notify type byte
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<byte[]>> _pendingResponses = new();

    // Channel for collecting ALL 0x41 notifications during transfer mode.
    // Avoids the single-shot waiter bug where the mode-change ACK consumes
    // the waiter before the IP notification (payload[0]==0x08) arrives.
    private Channel<byte[]>? _transferNotifyChannel;

    // BLE-provided transfer credentials (populated from 0x41 notification during transfer mode)
    private string? _bleTransferSsid;
    private string? _bleTransferPassword;

    /// <summary>
    /// Fired for every BLE notification frame (diagnostic use only).
    /// </summary>
    internal event EventHandler<byte[]>? RawNotifyReceived;

    internal IReadOnlyList<IPAddress> DiagnosticLastTransferEndpointCandidates { get; private set; } = [];
    internal IPAddress? DiagnosticLastValidatedTransferIp { get; private set; }

    private HeyCyanState _state = HeyCyanState.Disconnected;
    private HeyCyanMediaCount? _lastMediaCount;

    public WindowsHeyCyanGlassesSession(
        ILogger<WindowsHeyCyanGlassesSession> log,
        WindowsBluetoothEnumerator btEnumerator,
        WindowsBluetoothOutputEnumerator btOutputEnumerator,
        WindowsGlassesWiFiManager? wifiManager = null,
        IWindowsWiFiDirectConnector? wifiDirectManager = null,
        bool ensureClassicAudio = true)
    {
        _log = log;
        _btEnumerator = btEnumerator;
        _btOutputEnumerator = btOutputEnumerator;
        _wifiManager = wifiManager;
        _wifiDirectManager = wifiDirectManager;
        _ensureClassicAudio = ensureClassicAudio;
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

        // Fallback: also check paired BLE devices that may not be advertising
        // (e.g. glasses already connected via Classic BT — see RCA 002).
        try
        {
            // Check BLE paired devices
            var bleSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var blePairedDevices = await global::Windows.Devices.Enumeration.DeviceInformation
                .FindAllAsync(bleSelector).AsTask(ct).ConfigureAwait(false);

            _log.LogDebug("Paired-device fallback: {Count} BLE paired device(s) found", blePairedDevices.Count);
            foreach (var di in blePairedDevices)
            {
                _log.LogDebug("  BLE paired: {Name} ({Id}), match={Match}", di.Name, di.Id, IsHeyCyanName(di.Name));
                if (!IsHeyCyanName(di.Name)) continue;

                using var bleDevice = await BluetoothLEDevice.FromIdAsync(di.Id).AsTask(ct).ConfigureAwait(false);
                if (bleDevice is null) continue;

                if (!devices.ContainsKey(bleDevice.BluetoothAddress))
                {
                    var address = FormatBluetoothAddress(bleDevice.BluetoothAddress);
                    var info = new HeyCyanDeviceInfo(di.Name, address, Rssi: 0);
                    devices[bleDevice.BluetoothAddress] = info;
                    _log.LogInformation("BLE scan: found paired BLE device {Name} ({Address}) — not advertising",
                        di.Name, address);
                }
            }

            // Also check Classic BT paired devices — glasses may be Classic-paired only
            var classicSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var classicPairedDevices = await global::Windows.Devices.Enumeration.DeviceInformation
                .FindAllAsync(classicSelector).AsTask(ct).ConfigureAwait(false);

            _log.LogDebug("Paired-device fallback: {Count} Classic BT paired device(s) found", classicPairedDevices.Count);
            foreach (var di in classicPairedDevices)
            {
                _log.LogDebug("  Classic paired: {Name} ({Id}), match={Match}", di.Name, di.Id, IsHeyCyanName(di.Name));
                if (!IsHeyCyanName(di.Name)) continue;

                using var classicDevice = await BluetoothDevice.FromIdAsync(di.Id).AsTask(ct).ConfigureAwait(false);
                if (classicDevice is null) continue;

                if (!devices.ContainsKey(classicDevice.BluetoothAddress))
                {
                    var address = FormatBluetoothAddress(classicDevice.BluetoothAddress);
                    var info = new HeyCyanDeviceInfo(di.Name, address, Rssi: 0);
                    devices[classicDevice.BluetoothAddress] = info;
                    _log.LogInformation("BLE scan: found paired Classic BT device {Name} ({Address}) — not advertising",
                        di.Name, address);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Paired-device fallback scan failed");
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

            // Discover Serial Port Service (Uncached first; cached can help after
            // Windows has already seen the device). The glasses/Windows BLE stack
            // can report zero services on the first attempt even when a retry works.
            GattDeviceServicesResult? serviceResult = null;
            const int maxServiceAttempts = 4;
            for (var attempt = 1; attempt <= maxServiceAttempts; attempt++)
            {
                _bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(ct).ConfigureAwait(false);
                if (_bleDevice is null)
                    throw new InvalidOperationException($"Could not connect to BLE device {device.Address}");

                serviceResult = await _bleDevice.GetGattServicesForUuidAsync(
                    SerialPortService, BluetoothCacheMode.Uncached)
                    .AsTask(ct).ConfigureAwait(false);

                if (serviceResult.Status == GattCommunicationStatus.Success && serviceResult.Services.Count > 0)
                    break;

                var cachedResult = await _bleDevice.GetGattServicesForUuidAsync(
                    SerialPortService, BluetoothCacheMode.Cached)
                    .AsTask(ct).ConfigureAwait(false);

                if (cachedResult.Status == GattCommunicationStatus.Success && cachedResult.Services.Count > 0)
                {
                    serviceResult = cachedResult;
                    break;
                }

                _log.LogWarning(
                    "Serial port service not found on {Name}, attempt {Attempt}/{MaxAttempts} (uncached {UncachedStatus}/{UncachedCount}, cached {CachedStatus}/{CachedCount})",
                    device.Name,
                    attempt,
                    maxServiceAttempts,
                    serviceResult.Status,
                    serviceResult.Services.Count,
                    cachedResult.Status,
                    cachedResult.Services.Count);

                _bleDevice.Dispose();
                _bleDevice = null;

                if (attempt < maxServiceAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }

            if (_bleDevice is null || serviceResult is null
                || serviceResult.Status != GattCommunicationStatus.Success
                || serviceResult.Services.Count == 0)
            {
                throw new InvalidOperationException($"Serial port service not found on {device.Name}");
            }

            _gattService = serviceResult.Services[0];
            var charsResult = await _gattService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached)
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

            // GattSession keeps the connection alive — without this, Windows silently
            // drops GATT subscriptions and notifications stop being delivered.
            _gattSession = await GattSession.FromDeviceIdAsync(_bleDevice.BluetoothDeviceId).AsTask(ct).ConfigureAwait(false);
            _gattSession.MaintainConnection = true;

            // Subscribe to notifications — handler MUST be registered before CCCD write
            // per Windows BLE stack requirements (ValueChanged won't fire otherwise).
            _rxCharacteristic.ValueChanged += OnCharacteristicValueChanged;

            var notifyResult = await _rxCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct).ConfigureAwait(false);
            if (notifyResult != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"Failed to enable notifications: {notifyResult}");

            _bleDevice.ConnectionStatusChanged += OnConnectionStatusChanged;

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

            State = HeyCyanState.Connected;
            _log.LogInformation("Connected to {Name} ({Address})", device.Name, device.Address);

            // Classic BT audio remains important on Windows, but Windows may take
            // tens of seconds to expose HFP/A2DP MMDevice endpoints. Keep that
            // recovery running after BLE connect so mic/speaker can auto-select
            // when the OS finally marks them active.
            if (_ensureClassicAudio)
                StartClassicAudioSetup(address, device.Name);
            else
                _log.LogInformation("Skipping Classic BT audio setup for {Name}", device.Name);
        }
        catch
        {
            await CleanupDeviceAsync().ConfigureAwait(false);
            State = HeyCyanState.Disconnected;
            throw;
        }
    }

    /// <summary>
    /// Internal result of Classic BT audio endpoint discovery.
    /// Used only within this session — not exposed on <see cref="IHeyCyanGlassesSession"/>.
    /// </summary>
    private enum BtAudioResult
    {
        /// <summary>Both capture and render endpoints found.</summary>
        Success,
        /// <summary>Only some endpoints found (e.g. render but not capture).</summary>
        PartialSuccess,
        /// <summary>No BT audio endpoints found. GATT-only connection.</summary>
        Failure
    }

    private void StartClassicAudioSetup(ulong address, string deviceName)
    {
        _classicAudioSetupCts?.Cancel();
        _classicAudioSetupCts?.Dispose();
        _classicAudioSetupCts = new CancellationTokenSource();
        var token = _classicAudioSetupCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var audioResult = await EnsureClassicBtAudioAsync(address, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                    return;

                if (audioResult == BtAudioResult.Success)
                    _log.LogInformation("Classic BT audio ready for {Name} — both endpoints found", deviceName);
                else if (audioResult == BtAudioResult.PartialSuccess)
                    _log.LogWarning("Classic BT audio partial for {Name} — only some endpoints found", deviceName);
                else
                    _log.LogWarning("Classic BT audio unavailable for {Name} — Windows endpoints are not active", deviceName);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _log.LogDebug("Classic BT audio setup canceled for {Name}", deviceName);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Classic BT audio setup failed for {Name}", deviceName);
            }
        });
    }

    /// <summary>
    /// Ensures Classic BT audio endpoints (HFP capture + A2DP render) are available.
    /// Handles three scenarios:
    /// <list type="bullet">
    ///   <item>Already paired + endpoints exist → immediate return</item>
    ///   <item>Paired but no endpoints → SDP query to trigger profile connection, then poll</item>
    ///   <item>Not paired → pair first, then trigger profile connection</item>
    /// </list>
    /// This method never throws — failures are logged and returned as <see cref="BtAudioResult.Failure"/>.
    /// </summary>
    private async Task<BtAudioResult> EnsureClassicBtAudioAsync(ulong bleAddress, CancellationToken ct)
    {
        var mac = FormatBluetoothAddress(bleAddress);

        try
        {
            // Phase A: Quick check — are endpoints already here?
            _log.LogDebug("EnsureBtAudio Phase A: checking existing endpoints for {Mac}", mac);
            await WindowsBluetoothEnumerator.RefreshPairedDeviceCacheAsync().ConfigureAwait(false);
            _btEnumerator.ScanAndRegister();
            _btOutputEnumerator.ScanAndRegister();

            if (HasBtEndpoints(mac))
            {
                _log.LogInformation("EnsureBtAudio: endpoints already exist for {Mac}", mac);
                return GetBtAudioResult(mac);
            }

            // Phase B: Check if paired but no endpoints
            _log.LogDebug("EnsureBtAudio Phase B: checking pairing state for {Mac}", mac);
            var classicDevice = await BluetoothDevice.FromBluetoothAddressAsync(bleAddress)
                .AsTask(ct).ConfigureAwait(false);

            if (classicDevice is not null && classicDevice.DeviceInformation.Pairing.IsPaired)
            {
                _log.LogInformation("EnsureBtAudio: device paired but no endpoints — forcing profile connection for {Mac}", mac);
                if (await TryForceProfileConnectionAsync(classicDevice, mac, ct).ConfigureAwait(false))
                    return GetBtAudioResult(mac);

                // Profile forcing failed — log warning but do NOT unpair.
                // Unpairing a dual-mode device destroys the BLE bond, which
                // breaks GATT notification delivery (see RCA-808).
                _log.LogWarning("EnsureBtAudio: profile forcing failed for {Mac} — continuing without audio (preserving BLE bond)", mac);
                return BtAudioResult.Failure;
            }

            // Phase C: Not paired — full pairing sequence
            _log.LogDebug("EnsureBtAudio Phase C: pairing for {Mac}", mac);
            var paired = await TryPairClassicAsync(bleAddress, ct).ConfigureAwait(false);
            if (!paired)
            {
                _log.LogWarning("EnsureBtAudio: Classic BT pairing failed for {Mac}", mac);
                return BtAudioResult.Failure;
            }

            // After pairing, try to force profile connection and poll
            classicDevice = await BluetoothDevice.FromBluetoothAddressAsync(bleAddress)
                .AsTask(ct).ConfigureAwait(false);
            if (classicDevice is not null)
            {
                await TryForceProfileConnectionAsync(classicDevice, mac, ct).ConfigureAwait(false);
            }

            return HasBtEndpoints(mac) ? GetBtAudioResult(mac) : BtAudioResult.Failure;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "EnsureBtAudio failed for {Mac}", mac);
            return BtAudioResult.Failure;
        }
    }

    /// <summary>
    /// Attempt to force Windows to connect HFP/A2DP audio profiles for an already-paired device.
    /// Queries RFCOMM services via SDP, which can trigger Windows to discover and connect profiles.
    /// Then polls for endpoint appearance.
    /// </summary>
    private async Task<bool> TryForceProfileConnectionAsync(BluetoothDevice device, string mac, CancellationToken ct)
    {
        try
        {
            // Query RFCOMM services — SDP query may trigger Windows BT stack to connect HFP.
            var hfpId = RfcommServiceId.FromUuid(Guid.Parse("0000111e-0000-1000-8000-00805f9b34fb"));
            var services = await device.GetRfcommServicesForIdAsync(hfpId)
                .AsTask(ct).ConfigureAwait(false);

            if (services.Services.Count > 0)
                _log.LogInformation("HFP service found via SDP for {Mac} — waiting for Windows to create endpoint", mac);
            else
                _log.LogDebug("No HFP service found via SDP for {Mac} — polling anyway", mac);

            // Poll for endpoint appearance (2s intervals, 30s max)
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(2000, ct).ConfigureAwait(false);
                await WindowsBluetoothEnumerator.RefreshPairedDeviceCacheAsync().ConfigureAwait(false);
                _btEnumerator.ScanAndRegister();
                _btOutputEnumerator.ScanAndRegister();

                if (HasBtEndpoints(mac))
                {
                    _log.LogInformation("BT audio endpoint appeared after {Seconds}s for {Mac}", (i + 1) * 2, mac);
                    return true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "TryForceProfileConnection failed for {Mac}", mac);
        }

        return false;
    }

    private bool HasBtEndpoints(string mac)
    {
        var hasCapture = _btEnumerator.HasEndpointWithMac(mac);
        var hasRender = _btOutputEnumerator.HasEndpointWithMac(mac);
        return hasCapture || hasRender;
    }

    private BtAudioResult GetBtAudioResult(string mac)
    {
        var hasCapture = _btEnumerator.HasEndpointWithMac(mac);
        var hasRender = _btOutputEnumerator.HasEndpointWithMac(mac);
        if (hasCapture && hasRender) return BtAudioResult.Success;
        if (hasCapture || hasRender) return BtAudioResult.PartialSuccess;
        return BtAudioResult.Failure;
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
        // Send battery poll command and wait for notify response (action 0x42)
        await SendCommandAsync(HeyCyanCommands.GetBattery(), ct).ConfigureAwait(false);

        try
        {
            var payload = await WaitForNotifyAsync(0x42, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
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
        => SendCommandAsync(HeyCyanCommands.StopVideoRecording(), ct);

    public Task StartAudioAsync(CancellationToken ct)
        => SendCommandAsync(HeyCyanCommands.StartAudioRecording(), ct);

    public Task StopAudioAsync(CancellationToken ct)
        => SendCommandAsync(HeyCyanCommands.StopMode(), ct);

    public Task TakeAiPhotoAsync(CancellationToken ct)
        => SendCommandAsync(HeyCyanCommands.TakeAiPhoto(), ct);

    public async Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken ct)
    {
        DiagnosticLastTransferEndpointCandidates = [];
        DiagnosticLastValidatedTransferIp = null;

        // Create a channel to capture ALL 0x41 notifications during transfer.
        _transferNotifyChannel = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });

        if (_wifiDirectManager is not null)
        {
            try
            {
                // Android starts peer discovery before asking the glasses to enter
                // transfer mode, so keep the Windows watcher hot during the BLE flip.
                _wifiDirectManager.Disconnect();
                _wifiDirectManager.StartDiscovery();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "WiFi Direct discovery could not be started");
                Console.Error.WriteLine($"[WIFIDIRECT] Discovery start failed: {ex.Message}");
            }
        }

        // Send EnterTransferMode BLE command — glasses will respond with
        // a notification containing either an IP or SSID + password details.
        await SendCommandAsync(HeyCyanCommands.EnterTransferMode(), ct).ConfigureAwait(false);
        State = HeyCyanState.TransferMode;

        IPAddress? glassesIp = null;
        IPAddress? bleReportedIp = null;

        try
        {
            // Wait for BLE notification with SSID+password (up to 30s)
            using var notifyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            notifyCts.CancelAfter(TimeSpan.FromSeconds(30));

            await foreach (var payload in _transferNotifyChannel.Reader.ReadAllAsync(notifyCts.Token).ConfigureAwait(false))
            {
                Console.Error.WriteLine($"[BLE-0x41] Transfer notify ({payload.Length}B): {BitConverter.ToString(payload)}");

                // Direct IP notification (0x08): payload[1..4] = IPv4
                if (payload.Length >= 5 && payload[0] == 0x08)
                {
                    bleReportedIp = new IPAddress(payload.AsSpan(1, 4));
                    _log.LogInformation("Got glasses IP from BLE 0x08 notify: {Ip}", bleReportedIp);
                    Console.Error.WriteLine($"[BLE-0x41] Device-reported IP={bleReportedIp}");

                    // A BLE IP alone is not enough on Windows; we still need a
                    // WiFi Direct or hotspot route. In WiFi-Direct-only mode,
                    // stop waiting for hotspot credentials and connect now.
                    if (_wifiDirectManager is not null && _wifiManager is null)
                        break;

                    continue;
                }

                // SSID+password notification: [02-01-04-01][ssidLen LE16][pwdLen LE16][ssid][pwd]
                if (payload.Length >= 8 && payload[0] == 0x02 && payload[1] == 0x01
                    && payload[2] == 0x04 && payload[3] == 0x01)
                {
                    var ssidLen = payload[4] | (payload[5] << 8);
                    var pwdLen = payload[6] | (payload[7] << 8);

                    if (payload.Length >= 8 + ssidLen + pwdLen && ssidLen > 0)
                    {
                        var ssid = System.Text.Encoding.UTF8.GetString(payload, 8, ssidLen);
                        var password = pwdLen > 0
                            ? System.Text.Encoding.UTF8.GetString(payload, 8 + ssidLen, pwdLen)
                            : "123456789";

                        _bleTransferSsid = ssid;
                        _bleTransferPassword = password;
                        if (_wifiDirectManager is not null)
                            _wifiDirectManager.GroupPassword = password;
                        Console.Error.WriteLine($"[BLE-0x41] SSID='{ssid}' password=({password.Length} chars)");
                        _log.LogInformation("BLE transfer credentials: SSID='{Ssid}'", ssid);
                        break;
                    }
                }
            }

            // Build an actual Windows network route to the transfer server.
            // A BLE-reported IP is only the target address; without WiFi Direct
            // or hotspot association, Windows will route HTTP over the wrong NIC.
            if (_bleTransferSsid is not null || _wifiDirectManager is not null || _wifiManager is not null)
            {
                // Step 2: Poll GetWifiIP to trigger/confirm AP startup.
                Console.Error.WriteLine("[BLE] Step 2: Polling GetWifiIP to trigger/confirm AP startup...");
                var bleIp = bleReportedIp ?? await PollWifiIpReadyAsync(ct).ConfigureAwait(false);
                bleReportedIp ??= bleIp;

                // Step 3: Send GetDeviceConfig (0x47) — iOS SDK critical verification step.
                // This signals the glasses to actually start broadcasting the WiFi radio.
                Console.Error.WriteLine("[BLE] Step 3: Sending GetDeviceConfig (0x47) to activate AP radio...");
                await SendCommandAsync(HeyCyanCommands.GetDeviceConfig(), ct).ConfigureAwait(false);
                try
                {
                    var configPayload = await WaitForNotifyAsync(0x47, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                    Console.Error.WriteLine($"[BLE] GetDeviceConfig response: {BitConverter.ToString(configPayload)}");
                }
                catch (TimeoutException)
                {
                    Console.Error.WriteLine("[BLE] GetDeviceConfig — no response (continuing anyway)");
                }

                // Step 4: Wait 5s for the AP radio to spin up after config check.
                // iOS SDK waits 5s — the glasses WiFi radio needs time to start.
                Console.Error.WriteLine("[WIFI] Step 4: Waiting 5s for AP radio to start broadcasting...");
                await Task.Delay(5000, ct).ConfigureAwait(false);

                // Step 5: Confirm AP still broadcasting via second GetWifiIP poll.
                Console.Error.WriteLine("[BLE] Step 5: Confirming AP still broadcasting...");
                var confirmIp = await PollWifiIpReadyAsync(ct).ConfigureAwait(false);
                if (confirmIp is not null)
                {
                    bleIp ??= confirmIp;
                    bleReportedIp ??= confirmIp;
                    Console.Error.WriteLine($"[BLE] AP confirmed (IP={confirmIp})");
                }
                else
                {
                    Console.Error.WriteLine("[BLE] AP confirmation failed — proceeding with WiFi join anyway");
                }

                var routeReady = false;
                var routeCandidates = new List<IPAddress>();

                if (_wifiDirectManager is not null)
                {
                    if (_bleTransferPassword is not null)
                        _wifiDirectManager.GroupPassword = _bleTransferPassword;

                    Console.Error.WriteLine("[WIFIDIRECT] Step 6A: Connecting via WiFi Direct/P2P...");
                    var directIp = await TryWiFiDirectAsync(ct).ConfigureAwait(false);
                    if (directIp is not null)
                    {
                        routeReady = true;
                        Console.Error.WriteLine($"[WIFIDIRECT] Route established; endpoint IP={directIp}");
                        AddCandidates(
                            routeCandidates,
                            BuildTransferEndpointCandidates(
                                bleIp,
                                directIp,
                                _wifiDirectManager.ConnectionEndpointPairs,
                                includeKnownP2pCandidates: true));
                    }
                    else
                    {
                        Console.Error.WriteLine("[WIFIDIRECT] No WiFi Direct route established.");
                    }
                }

                if (!routeReady && _bleTransferSsid is not null && _wifiManager is not null)
                {
                    // Step 6B: Switch WiFi to the glasses network.
                    // Run BLE keepalive polling in parallel to prevent AP timeout.
                    // The glasses shut down their WiFi AP if they don't receive BLE
                    // commands for a period of time.
                    Console.Error.WriteLine($"[WIFI] Step 6B: Joining '{_bleTransferSsid}'...");

                    IPAddress? joinedIp = null;
                    using var joinCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                    // BLE keepalive: send GetWifiIP every 10s during WiFi join
                    var keepaliveTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (!joinCts.Token.IsCancellationRequested)
                            {
                                await Task.Delay(10_000, joinCts.Token).ConfigureAwait(false);
                                Console.Error.WriteLine("[BLE] Keepalive: GetWifiIP during WiFi join...");
                                await SendCommandAsync(HeyCyanCommands.GetWifiIP(), joinCts.Token).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException) { }
                    }, joinCts.Token);

                    try
                    {
                        joinedIp = await _wifiManager.ForceJoinAsync(
                            _bleTransferSsid,
                            _bleTransferPassword ?? WindowsGlassesWiFiManager.DefaultPassword,
                            ct,
                            bleKeepalive: async (token) =>
                            {
                                Console.Error.WriteLine("[BLE] Keepalive: GetWifiIP during scan wait...");
                                await SendCommandAsync(HeyCyanCommands.GetWifiIP(), token).ConfigureAwait(false);
                            }).ConfigureAwait(false);
                    }
                    finally
                    {
                        joinCts.Cancel();
                        try { await keepaliveTask.ConfigureAwait(false); } catch { }
                    }

                    if (joinedIp is not null)
                    {
                        routeReady = true;
                        AddCandidates(
                            routeCandidates,
                            BuildTransferEndpointCandidates(
                                bleIp,
                                joinedIp,
                                endpointPairs: null,
                                includeKnownP2pCandidates: false));
                    }
                    else
                    {
                        Console.Error.WriteLine("[WIFI] Hotspot join did not establish a route.");
                    }
                }

                if (routeReady)
                {
                    // Step 7: Send second GetDeviceConfig to keep glasses in transfer state.
                    Console.Error.WriteLine("[BLE] Step 7: Sending second GetDeviceConfig to keep glasses alive...");
                    await SendCommandAsync(HeyCyanCommands.GetDeviceConfig(), ct).ConfigureAwait(false);
                    try
                    {
                        var configPayload2 = await WaitForNotifyAsync(0x47, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                        Console.Error.WriteLine($"[BLE] GetDeviceConfig #2 response: {BitConverter.ToString(configPayload2)}");
                    }
                    catch (TimeoutException)
                    {
                        Console.Error.WriteLine("[BLE] GetDeviceConfig #2 — no response (continuing)");
                    }

                    // Step 8: Post-connect wait (15s) with BLE keepalive polling every 5s.
                    Console.Error.WriteLine("[WIFI] Step 8: Post-connect wait (15s) with BLE keepalive...");
                    for (int keepalive = 0; keepalive < 3; keepalive++)
                    {
                        await Task.Delay(5000, ct).ConfigureAwait(false);
                        Console.Error.WriteLine($"[BLE] Keepalive GetWifiIP poll ({keepalive + 1}/3)...");
                        await SendCommandAsync(HeyCyanCommands.GetWifiIP(), ct).ConfigureAwait(false);
                    }

                    DiagnosticLastTransferEndpointCandidates = routeCandidates.ToArray();
                    glassesIp = await ProbeTransferEndpointAsync(routeCandidates, ct).ConfigureAwait(false);
                    DiagnosticLastValidatedTransferIp = glassesIp;
                }

                if (glassesIp is null)
                {
                    Console.Error.WriteLine("[WIFI] No routed transfer endpoint found; probing fixed fallback candidates...");
                    glassesIp = routeReady
                        ? await ProbeCandidateIpsAsync(ct).ConfigureAwait(false)
                        : null;
                    DiagnosticLastValidatedTransferIp = glassesIp;
                }
            }

            if (glassesIp is null)
                throw new InvalidOperationException(
                    "Transfer mode: no routed glasses IP obtained. "
                    + $"BLE SSID={_bleTransferSsid ?? "(none)"}, "
                    + $"BLE IP={bleReportedIp?.ToString() ?? "(none)"}. "
                    + "Windows did not establish a WiFi Direct or hotspot route to the glasses.");
        }
        catch
        {
            State = HeyCyanState.Connected;
            throw;
        }
        finally
        {
            _transferNotifyChannel.Writer.TryComplete();
            _transferNotifyChannel = null;
            _bleTransferSsid = null;
            _bleTransferPassword = null;
        }

        var baseUrl = $"http://{glassesIp}";
        _log.LogInformation("Transfer mode active, glasses at {BaseUrl}", baseUrl);
        return new HeyCyanTransferSession(baseUrl, Array.Empty<string>());
    }

    /// <summary>
    /// Polls GetWifiIP command over BLE to trigger/confirm glasses AP startup.
    /// iOS SDK equivalent: getDeviceWifiIPSuccess with exponential backoff, up to 10 retries.
    /// Returns the glasses IP when the AP is ready, or null after timeout.
    /// </summary>
    private async Task<IPAddress?> PollWifiIpReadyAsync(CancellationToken ct)
    {
        const int maxRetries = 10;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            // Exponential backoff: 1s, 2s, 4s, 4s, 4s... (capped at 4s like iOS SDK)
            var delay = Math.Min(1000 * (1 << attempt), 4000);
            if (attempt > 0)
            {
                Console.Error.WriteLine($"[BLE] GetWifiIP attempt {attempt + 1}/{maxRetries} (waiting {delay}ms)...");
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }

            // Send [0x02, 0x03] GetWifiIP query
            await SendCommandAsync(HeyCyanCommands.GetWifiIP(), ct).ConfigureAwait(false);

            // Wait up to 3s for the 0x08 IP notification response
            using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            responseCts.CancelAfter(TimeSpan.FromSeconds(3));

            try
            {
                if (_transferNotifyChannel is null) return null;

                await foreach (var payload in _transferNotifyChannel.Reader.ReadAllAsync(responseCts.Token).ConfigureAwait(false))
                {
                    Console.Error.WriteLine($"[BLE-0x41] WifiIP response ({payload.Length}B): {BitConverter.ToString(payload)}");

                    // Response format: [0x02, 0x03, IP1, IP2, IP3, IP4, ...]
                    if (payload.Length >= 6 && payload[0] == 0x02 && payload[1] == 0x03)
                    {
                        var ip = new IPAddress(payload.AsSpan(2, 4));
                        if (!ip.Equals(IPAddress.Any))
                        {
                            Console.Error.WriteLine($"[BLE] Glasses AP ready! IP={ip}");
                            _log.LogInformation("GetWifiIP success: glasses AP at {Ip}", ip);
                            return ip;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (responseCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Timeout waiting for response — retry
                Console.Error.WriteLine($"[BLE] GetWifiIP attempt {attempt + 1} - no IP response yet");
            }
        }

        Console.Error.WriteLine($"[BLE] GetWifiIP failed after {maxRetries} attempts - AP may not have started");
        return null;
    }

    /// <summary>
    /// Reads the transfer notification channel waiting for the 0x08 IP payload.
    /// Per Android SDK: payload[0]==0x08, payload[1..4] = IPv4 octets.
    /// </summary>
    private async Task<IPAddress?> WaitForTransferIpNotifyAsync(CancellationToken ct)
    {
        if (_transferNotifyChannel is null) return null;

        try
        {
            await foreach (var payload in _transferNotifyChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                Console.Error.WriteLine($"[BLE-0x41] Transfer notify ({payload.Length}B): {BitConverter.ToString(payload)}");
                _log.LogInformation("Transfer 0x41 notify ({Length}B): {Hex}",
                    payload.Length, BitConverter.ToString(payload));

                // Android SDK: payload[0]==0x08 means IP notification, bytes [1..4] are IPv4
                if (payload.Length >= 5 && payload[0] == 0x08)
                {
                    var ip = new IPAddress(payload.AsSpan(1, 4));
                    _log.LogInformation("Got glasses IP from BLE 0x08 notify: {Ip}", ip);
                    return ip;
                }

                // Parse SSID+password notification (iOS flow):
                // Format: [02-01-04][status][ssid_len LE16][pwd_len LE16][ssid bytes][pwd bytes]
                if (payload.Length >= 8 && payload[0] == 0x02 && payload[1] == 0x01 && payload[2] == 0x04
                    && payload[3] == 0x01) // status == success
                {
                    var ssidLen = payload[4] | (payload[5] << 8);
                    var pwdLen = payload[6] | (payload[7] << 8);

                    if (payload.Length >= 8 + ssidLen + pwdLen && ssidLen > 0)
                    {
                        var ssid = System.Text.Encoding.UTF8.GetString(payload, 8, ssidLen);
                        var password = pwdLen > 0
                            ? System.Text.Encoding.UTF8.GetString(payload, 8 + ssidLen, pwdLen)
                            : "123456789";

                        _log.LogInformation("BLE transfer credentials: SSID='{Ssid}' pwd=({PwdLen} chars)",
                            ssid, password.Length);
                        Console.Error.WriteLine($"[BLE-0x41] SSID='{ssid}' password=({password.Length} chars)");

                        // Store for other strategies
                        _bleTransferSsid = ssid;
                        _bleTransferPassword = password;

                        // Feed the password to WiFi Direct manager for pairing
                        if (_wifiDirectManager is not null)
                            _wifiDirectManager.GroupPassword = password;

                        // Immediately connect to the glasses' hidden WiFi network.
                        // The glasses create a hidden AP with this SSID — connect directly
                        // rather than waiting for WiFi Direct peer discovery.
                        if (_wifiManager is not null)
                        {
                            _log.LogInformation("Connecting directly to glasses hidden WiFi '{Ssid}'...", ssid);
                            Console.Error.WriteLine($"[WIFI] Connecting directly to hidden network '{ssid}'...");

                            var glassesIpResult = await _wifiManager.ForceJoinAsync(ssid, password, ct)
                                .ConfigureAwait(false);

                            if (glassesIpResult is not null)
                            {
                                _log.LogInformation("Connected to glasses WiFi, IP: {Ip}", glassesIpResult);
                                Console.Error.WriteLine($"[WIFI] Connected! Glasses at: {glassesIpResult}");
                                return glassesIpResult;
                            }

                            Console.Error.WriteLine("[WIFI] Force-join didn't yield an IP, continuing...");
                        }
                    }
                }

                // Fallback: scan for 192.168.x.x pattern anywhere in payload
                if (payload.Length >= 6)
                {
                    for (int i = 0; i <= payload.Length - 4; i++)
                    {
                        if (payload[i] == 192 && payload[i + 1] == 168)
                        {
                            var ip = new IPAddress(payload.AsSpan(i, 4));
                            _log.LogInformation("Got glasses IP from 0x41 payload scan: {Ip}", ip);
                            return ip;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Overall timeout or channel completed — return null (best-effort strategy)
        }

        return null;
    }

    /// <summary>
    /// WiFi Direct strategy — discover peer and connect to get remote IP.
    /// Retries discovery once if the peer isn't found on the first attempt
    /// (handles case where previous failed connection left stale state).
    /// </summary>
    private async Task<IPAddress?> TryWiFiDirectAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(45));

            try
            {
                var remoteIp = await _wifiDirectManager!.WaitForPeerAndConnectAsync(cts.Token)
                    .ConfigureAwait(false);

                if (IPAddress.TryParse(remoteIp, out var ip))
                {
                    _log.LogInformation("WiFi Direct connected, glasses IP: {Ip}", ip);
                    return ip;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // First attempt timed out — restart discovery and try again
                _log.LogInformation("WiFi Direct peer not found, restarting discovery...");
                Console.Error.WriteLine("[WIFIDIRECT] Peer not found, restarting discovery...");
                _wifiDirectManager!.Disconnect();
                _wifiDirectManager.StartDiscovery();

                using var retryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                retryCts.CancelAfter(TimeSpan.FromSeconds(20));

                try
                {
                    var remoteIp = await _wifiDirectManager.WaitForPeerAndConnectAsync(retryCts.Token)
                        .ConfigureAwait(false);

                    if (IPAddress.TryParse(remoteIp, out var ip))
                    {
                        _log.LogInformation("WiFi Direct connected on retry, glasses IP: {Ip}", ip);
                        return ip;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Retry also timed out — give up on WiFi Direct
                    _log.LogWarning("WiFi Direct retry also timed out");
                    Console.Error.WriteLine("[WIFIDIRECT] Retry also timed out");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Overall cancellation — return null to let other strategies try
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WiFi Direct failed");
            Console.Error.WriteLine($"[WIFIDIRECT] Failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// WiFi hotspot strategy — scan for glasses SSID, join, get gateway IP.
    /// Extended to 30+ seconds with more retries (per RCA-810 fix plan).
    /// </summary>
    private async Task<IPAddress?> TryWiFiHotspotAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(2000, ct).ConfigureAwait(false);

            string? ssid = null;
            // Extended scan: 8 attempts × 4s = ~32 seconds total
            for (int attempt = 0; attempt < 8 && ssid is null; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(4000, ct).ConfigureAwait(false);
                ssid = await _wifiManager!.DiscoverGlassesSsidAsync(ct).ConfigureAwait(false);
            }

            if (ssid is null) return null;

            await _wifiManager!.JoinAsync(ssid, WindowsGlassesWiFiManager.DefaultPassword, ct)
                .ConfigureAwait(false);
            await Task.Delay(2000, ct).ConfigureAwait(false);

            var gatewayIp = _wifiManager.GetGatewayIp();
            if (gatewayIp is not null)
            {
                _log.LogInformation("WiFi hotspot connected, gateway IP: {Ip}", gatewayIp);
                return gatewayIp;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "WiFi hotspot fallback failed");
        }

        return null;
    }

    private static void AddCandidate(List<IPAddress> candidates, IPAddress? ip)
    {
        if (ip is null || ip.Equals(IPAddress.Any))
            return;
        if (candidates.Any(existing => existing.Equals(ip)))
            return;
        candidates.Add(ip);
    }

    private static void AddCandidates(List<IPAddress> candidates, IEnumerable<IPAddress> ips)
    {
        foreach (var ip in ips)
            AddCandidate(candidates, ip);
    }

    internal static IReadOnlyList<IPAddress> BuildTransferEndpointCandidates(
        IPAddress? bleReportedIp,
        IPAddress? routeIp,
        IEnumerable<WindowsWiFiDirectEndpointPair>? endpointPairs,
        bool includeKnownP2pCandidates)
    {
        var candidates = new List<IPAddress>();
        AddCandidate(candidates, bleReportedIp);
        AddCandidate(candidates, routeIp);

        if (endpointPairs is not null)
        {
            foreach (var pair in endpointPairs)
            {
                if (IPAddress.TryParse(pair.RemoteHost, out var remoteIp))
                    AddCandidate(candidates, remoteIp);
            }
        }

        if (includeKnownP2pCandidates)
        {
            AddCandidate(candidates, IPAddress.Parse("192.168.49.183"));
            AddCandidate(candidates, IPAddress.Parse("192.168.49.200"));
            AddCandidate(candidates, IPAddress.Parse("192.168.49.1"));
        }

        return candidates;
    }

    /// <summary>
    /// Verifies which routed candidate actually serves the glasses media API.
    /// This avoids returning a BLE-reported IP when Windows has connected via a
    /// different WiFi Direct endpoint address.
    /// </summary>
    private async Task<IPAddress?> ProbeTransferEndpointAsync(
        IEnumerable<IPAddress> candidates,
        CancellationToken ct)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var url = $"http://{candidate}/files/media.config";
            Console.Error.WriteLine($"[HTTP] Probing {url} ...");

            try
            {
                var response = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
                Console.Error.WriteLine($"[HTTP] {candidate}: {(int)response.StatusCode} {response.ReasonPhrase}");

                if (!response.IsSuccessStatusCode)
                    continue;

                var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (content.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                    || content.Contains("<html", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"[HTTP] {candidate}: HTML response, not glasses media.config");
                    continue;
                }

                _log.LogInformation("Transfer endpoint probe succeeded: {Ip}", candidate);
                Console.Error.WriteLine($"[HTTP] Transfer endpoint OK: {candidate}");
                return candidate;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"[HTTP] {candidate}: {ex.GetType().Name}: {ex.Message}");
                _log.LogDebug(ex, "Transfer endpoint probe failed for {Ip}", candidate);
            }
        }

        return null;
    }

    /// <summary>
    /// Probe known candidate IPs for the glasses HTTP server (iOS fallback).
    /// </summary>
    private async Task<IPAddress?> ProbeCandidateIpsAsync(CancellationToken ct)
    {
        // Try the glasses-typical subnets first. NEVER try 192.168.1.1 — that's
        // almost always the home router and will return HTTP 200 (router login page).
        var candidates = new[]
        {
            "192.168.49.183", // Android-observed glasses media host
            "192.168.49.200", // Android logs briefly observed this during import
            "192.168.49.1",  // WiFi Direct group-owner candidate
            "192.168.43.1",  // Android hotspot / WiFi Direct GO default
            "192.168.4.1",   // ESP32 / common IoT default
            "192.168.0.1",   // Generic router default
        };

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                Console.Error.WriteLine($"[WIFI] Probing http://{candidate}/files/media.config ...");
                var response = await httpClient.GetAsync($"http://{candidate}/files/media.config", ct)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    // Validate response is actually from the glasses (plain text file list),
                    // not a router login page returning HTTP 200.
                    var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (content.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                        || content.Contains("<html", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"[WIFI] Probe {candidate} returned HTML (router?), skipping");
                        _log.LogWarning("Probe {Ip} returned HTML (likely a router), skipping", candidate);
                        continue;
                    }

                    _log.LogInformation("Candidate IP probe succeeded: {Ip}", candidate);
                    Console.Error.WriteLine($"[WIFI] Probe {candidate} OK! Glasses found.");
                    return IPAddress.Parse(candidate);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogDebug("Probe {Ip} failed: {Msg}", candidate, ex.Message);
            }
        }

        return null;
    }

    public async Task ExitTransferModeAsync(CancellationToken ct)
    {
        // Disconnect WiFi Direct first (tears down P2P group)
        _wifiDirectManager?.Disconnect();

        // Leave glasses WiFi and reconnect to home network
        if (_wifiManager is not null)
            await _wifiManager.LeaveAndReconnectAsync(ct).ConfigureAwait(false);

        await SendCommandAsync(HeyCyanCommands.ExitTransferMode(), ct).ConfigureAwait(false);
        State = HeyCyanState.Connected;
    }

    // ── Diagnostic helpers (internal for test access) ────────────────────────

    /// <summary>
    /// Send a raw BLE command (diagnostic use only — bypasses HeyCyanCommands).
    /// </summary>
    internal Task SendRawDiagnosticCommandAsync(byte[] command, CancellationToken ct)
        => SendCommandAsync(command, ct);

    /// <summary>
    /// Access the underlying BLE device for GATT service enumeration (diagnostic only).
    /// </summary>
    internal BluetoothLEDevice? DiagnosticBleDevice => _bleDevice;

    // ── BLE helpers ─────────────────────────────────────────────────────────

    private async Task SendCommandAsync(byte[] command, CancellationToken ct)
    {
        if (_txCharacteristic is null)
            throw new InvalidOperationException("Not connected — no write characteristic available");

        _log.LogDebug("BLE write ({Length} bytes): {Hex}",
            command.Length, BitConverter.ToString(command));
        Console.Error.WriteLine($"[BLE-WRITE] ({command.Length}B) {BitConverter.ToString(command)}");

        await _commandLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var writer = new DataWriter();
            writer.WriteBytes(command);
            var result = await _txCharacteristic.WriteValueWithResultAsync(
                writer.DetachBuffer(),
                GattWriteOption.WriteWithResponse).AsTask(ct).ConfigureAwait(false);

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

        // Always log raw bytes for protocol debugging
        _log.LogDebug("BLE notify raw ({Length} bytes): {Hex}",
            bytes.Length, BitConverter.ToString(bytes));
        Console.Error.WriteLine($"[BLE-NOTIFY] ({bytes.Length}B) {BitConverter.ToString(bytes)}");

        RawNotifyReceived?.Invoke(this, bytes);

        // Parse Serial Port protocol frame: [0xBC][action][len_lo][len_hi][crc_lo][crc_hi][payload...]
        if (bytes.Length < 6 || bytes[0] != 0xBC)
        {
            _log.LogDebug("Notify frame not Serial Port protocol ({Length}B, first=0x{First:X2})",
                bytes.Length, bytes.Length > 0 ? bytes[0] : 0);
            return;
        }

        var action = bytes[1];
        var payloadLen = bytes[2] | (bytes[3] << 8); // LE
        var payload = bytes.Length > 6 ? bytes.AsSpan(6).ToArray() : Array.Empty<byte>();

        // Complete any pending one-shot waiter for this action
        if (_pendingResponses.TryRemove(action, out var tcs))
        {
            // During transfer mode, also feed 0x41 payloads into the channel
            // so the transfer logic can see ALL notifications (not just the first).
            if (action == 0x41 && _transferNotifyChannel is not null)
                _transferNotifyChannel.Writer.TryWrite(payload);

            tcs.TrySetResult(payload);
            return;
        }

        // Feed transfer channel for 0x41 even when no one-shot waiter is registered
        if (action == 0x41 && _transferNotifyChannel is not null)
        {
            _transferNotifyChannel.Writer.TryWrite(payload);
        }

        // Dispatch by action
        switch (action)
        {
            case 0x41: // GlassesControl response (buttons, mode changes, transfer)
                DispatchGlassesControl(payload);
                break;

            case 0x42: // Battery
                if (payload.Length >= 2)
                {
                    var battery = HeyCyanFrameParser.ParseBattery(payload);
                    _log.LogDebug("Battery: {Pct}% charging={Charging}", battery.Percentage, battery.IsCharging);
                    BatteryUpdated?.Invoke(this, battery);
                }
                break;

            case 0x45: // Heartbeat
                _log.LogDebug("Heartbeat response: {Hex}", BitConverter.ToString(payload));
                break;

            default:
                _log.LogDebug("Unhandled notify action 0x{Action:X2} ({Length}B payload)",
                    action, payload.Length);
                break;
        }
    }

    private void DispatchGlassesControl(byte[] payload)
    {
        if (payload.Length == 0) return;

        var subType = payload[0];
        switch (subType)
        {
            case 0x01 when payload.Length >= 2: // Mode-change / transfer response
                var subAction = payload[1];
                _log.LogInformation("GlassesControl mode response: sub=0x{Sub:X2}, payload={Hex}",
                    subAction, BitConverter.ToString(payload));
                break;

            case 0x02: // AI-photo button
                ButtonPressed?.Invoke(this, new HeyCyanButtonEvent(HeyCyanButtonGesture.Tap, DateTimeOffset.UtcNow));
                break;

            case 0x03: // AI-voice button
                ButtonPressed?.Invoke(this, new HeyCyanButtonEvent(HeyCyanButtonGesture.DoubleTap, DateTimeOffset.UtcNow));
                break;

            case 0x04: // Media counts / other sub-response
                _log.LogDebug("GlassesControl sub=0x04 ({Length}B): {Hex}",
                    payload.Length, BitConverter.ToString(payload));
                break;

            default:
                _log.LogDebug("GlassesControl unknown sub=0x{Sub:X2} ({Length}B)",
                    subType, payload.Length);
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
        _classicAudioSetupCts?.Cancel();
        _classicAudioSetupCts?.Dispose();
        _classicAudioSetupCts = null;

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

        if (_gattSession is not null)
        {
            _gattSession.Dispose();
            _gattSession = null;
        }

        if (_gattService is not null)
        {
            _gattService.Dispose();
            _gattService = null;
        }

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

    private static bool IsHeyCyanName(string? name) =>
        !string.IsNullOrEmpty(name)
        && (name.StartsWith("QC", StringComparison.OrdinalIgnoreCase)
         || name.StartsWith("O_", StringComparison.OrdinalIgnoreCase)
         || name.StartsWith("M01", StringComparison.OrdinalIgnoreCase)
         || name.Contains("Cyan", StringComparison.OrdinalIgnoreCase));

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
