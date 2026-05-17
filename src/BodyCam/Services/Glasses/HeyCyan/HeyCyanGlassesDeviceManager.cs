namespace BodyCam.Services.Glasses.HeyCyan;

using BodyCam.Services;
using BodyCam.Services.Glasses;
using Microsoft.Extensions.Logging;

/// <summary>
/// Concrete GlassesDeviceManager for HeyCyan QCSDK smart glasses.
/// Aggregates the HeyCyan session (Phases 1+6), four provider adapters
/// (camera P2, mic+speaker P3, button P4), and optional media-transfer helper (P5).
/// Projects QCSDK session events onto the base GlassesDeviceManager observables.
/// </summary>
public sealed class HeyCyanGlassesDeviceManager : GlassesDeviceManager
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly HeyCyanCameraProvider _camera;
    private readonly HeyCyanAudioInputProvider _mic;
    private readonly HeyCyanAudioOutputProvider _speaker;
    private readonly HeyCyanButtonProvider _button;
    private readonly IHeyCyanMediaTransfer? _media;
    private readonly ISettingsService _settings;
    private readonly ILogger<HeyCyanGlassesDeviceManager> _log;

    private HeyCyanDeviceInfo? _lastDevice;
    private HeyCyanBattery? _battery;
    private HeyCyanVersionInfo? _version;
    private HeyCyanMediaCount? _mediaCount;

    public HeyCyanGlassesDeviceManager(
        IHeyCyanGlassesSession session,
        HeyCyanCameraProvider camera,
        HeyCyanAudioInputProvider mic,
        HeyCyanAudioOutputProvider speaker,
        HeyCyanButtonProvider button,
        IHeyCyanMediaTransfer media,
        ISettingsService settings,
        ILogger<HeyCyanGlassesDeviceManager> log)
        : base(camera, mic, speaker, button)
    {
        _session = session;
        _camera = camera;
        _mic = mic;
        _speaker = speaker;
        _button = button;
        _media = media;
        _settings = settings;
        _log = log;

        _session.StateChanged += OnSessionStateChanged;
        _session.BatteryUpdated += OnBatteryUpdated;
        _session.MediaCountUpdated += OnMediaCountUpdated;
    }

    // Status surface for UI consumption (status panel, shell widget) -----------------
    public HeyCyanBattery? Battery
    {
        get => _battery;
        private set => SetProperty(ref _battery, value);
    }

    public HeyCyanVersionInfo? Version
    {
        get => _version;
        private set => SetProperty(ref _version, value);
    }

    public HeyCyanMediaCount? MediaCount
    {
        get => _mediaCount;
        private set => SetProperty(ref _mediaCount, value);
    }

    public string? MacAddress => Version?.MacAddress;

    public event EventHandler? StatusChanged;

    // Scan / connect / disconnect --------------------------------------------
    public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(
        TimeSpan timeout, CancellationToken ct)
        => _session.ScanAsync(timeout, ct);

    public async Task ConnectAsync(HeyCyanDeviceInfo device, CancellationToken ct)
    {
        _lastDevice = device;
        await _session.ConnectAsync(device, ct);

        // Persist for auto-reconnect on next startup
        _settings.LastHeyCyanDeviceAddress = device.Address;
        _settings.LastHeyCyanDeviceName = device.Name;

        Version = await _session.GetVersionAsync(ct);
        Battery = await _session.GetBatteryAsync(ct);
        await _session.SyncTimeAsync(ct);

        // Activate providers — managers auto-prefer them via priority
        await _camera.StartAsync(ct);
        try
        {
            await _mic.StartAsync(ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("BT capture endpoint"))
        {
            _log.LogWarning("Glasses mic unavailable — pair glasses as BT headset in Windows Settings for audio. ({Msg})", ex.Message);
        }
        // Speaker StartAsync requires sample rate; defer to first audio output request
        await _button.StartAsync(ct);

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Quick BLE scan for the last-connected device. Call once at app startup.
    /// </summary>
    public async Task TryAutoReconnectAsync()
    {
        if (!_settings.HeyCyanAutoReconnect) return;
        var savedAddress = _settings.LastHeyCyanDeviceAddress;
        if (savedAddress is null) return;
        if (State != GlassesConnectionState.Disconnected) return;

        _log.LogInformation("Auto-reconnect: scanning for saved device {Mac}", savedAddress);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var found = await ScanAsync(TimeSpan.FromSeconds(5), cts.Token);
            var match = found.FirstOrDefault(d =>
                string.Equals(d.Address, savedAddress, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                _log.LogInformation("Auto-reconnect: found {Name} ({Mac}), connecting", match.Name, match.Address);
                await ConnectAsync(match, CancellationToken.None);
            }
            else
            {
                _log.LogInformation("Auto-reconnect: saved device not found in range");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Auto-reconnect failed");
        }
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        try
        {
            await _session.DisconnectAsync(ct);
        }
        finally
        {
            // Base + per-capability managers handle fallback
        }
    }

    // Session → manager projection -------------------------------------------
    private void OnSessionStateChanged(object? s, HeyCyanState state)
    {
        State = state switch
        {
            HeyCyanState.Disconnected => GlassesConnectionState.Disconnected,
            HeyCyanState.Scanning => GlassesConnectionState.Scanning,
            HeyCyanState.Connecting => GlassesConnectionState.Connecting,
            HeyCyanState.Connected => GlassesConnectionState.Connected,
            HeyCyanState.TransferMode => GlassesConnectionState.Connected, // still present
            HeyCyanState.Disconnecting => GlassesConnectionState.Disconnecting,
            _ => GlassesConnectionState.Disconnected,
        };

        // Wave 4: structured fallback log for test verification
        if (state == HeyCyanState.Disconnected)
        {
            _log.LogInformation(
                "HeyCyan disconnected — fallback initiated (lastDevice={Mac})",
                _lastDevice?.Address ?? "<unknown>");
        }

        RaiseStateChanged();
    }

    private void OnBatteryUpdated(object? s, HeyCyanBattery b)
    {
        Battery = b;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaCountUpdated(object? s, HeyCyanMediaCount c)
    {
        MediaCount = c;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
