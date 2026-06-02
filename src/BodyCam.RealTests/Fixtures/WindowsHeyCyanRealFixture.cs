using BodyCam.Platforms.Windows;
using BodyCam.Platforms.Windows.Audio;
using BodyCam.Platforms.Windows.HeyCyan;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.RealTests.Fixtures;

/// <summary>
/// Real-hardware test fixture for HeyCyan glasses on Windows.
/// Constructs the full production object graph without MAUI DI.
/// 
/// Requires:
/// - HeyCyan glasses powered on and discoverable
/// - Windows PC with Bluetooth radio
/// - BODYCAM_REAL_HEYCYAN=1
/// - BODYCAM_REAL_HEYCYAN_MAC=XX:XX:XX:XX:XX:XX
/// </summary>
public sealed class WindowsHeyCyanRealFixture : IAsyncDisposable
{
    // Core services (internal types exposed as internal)
    internal WindowsHeyCyanGlassesSession Session { get; }
    public HeyCyanGlassesDeviceManager DeviceManager { get; }
    public HeyCyanAudioRouter Router { get; }

    // Audio infrastructure
    public AudioInputManager AudioInput { get; }
    public AudioOutputManager AudioOutput { get; }
    internal WindowsBluetoothEnumerator BtEnumerator { get; }
    internal WindowsBluetoothOutputEnumerator BtOutputEnumerator { get; }
    public HeyCyanAudioInputProvider MicProvider { get; }
    public HeyCyanAudioOutputProvider SpeakerProvider { get; }

    // WiFi + media transfer (null when created without transfer support)
    internal WindowsGlassesWiFiManager? WifiManager { get; }
    internal WindowsWiFiDirectManager? WifiDirectManager { get; }
    public IHeyCyanMediaTransfer? Transfer { get; }

    // Settings
    public InMemorySettingsService Settings { get; }

    private readonly ILoggerFactory _loggerFactory;

    private WindowsHeyCyanRealFixture(
        WindowsHeyCyanGlassesSession session,
        HeyCyanGlassesDeviceManager deviceManager,
        HeyCyanAudioRouter router,
        AudioInputManager audioInput,
        AudioOutputManager audioOutput,
        WindowsBluetoothEnumerator btEnumerator,
        WindowsBluetoothOutputEnumerator btOutputEnumerator,
        HeyCyanAudioInputProvider micProvider,
        HeyCyanAudioOutputProvider speakerProvider,
        InMemorySettingsService settings,
        ILoggerFactory loggerFactory,
        WindowsGlassesWiFiManager? wifiManager = null,
        WindowsWiFiDirectManager? wifiDirectManager = null,
        IHeyCyanMediaTransfer? transfer = null)
    {
        Session = session;
        DeviceManager = deviceManager;
        Router = router;
        AudioInput = audioInput;
        AudioOutput = audioOutput;
        BtEnumerator = btEnumerator;
        BtOutputEnumerator = btOutputEnumerator;
        MicProvider = micProvider;
        SpeakerProvider = speakerProvider;
        Settings = settings;
        _loggerFactory = loggerFactory;
        WifiManager = wifiManager;
        WifiDirectManager = wifiDirectManager;
        Transfer = transfer;
    }

    public static async Task<WindowsHeyCyanRealFixture> CreateAsync()
    {
        var loggerFactory = LoggerFactory.Create(b => b
            .AddDebug()
            .SetMinimumLevel(LogLevel.Trace));

        var settings = new InMemorySettingsService();
        var appSettings = new AppSettings();

        // Audio managers with platform providers
        var platformMic = new PlatformMicProvider(appSettings);
        var platformSpeaker = new WindowsSpeakerProvider();

        var audioInput = new AudioInputManager(
            new IAudioInputProvider[] { platformMic },
            settings,
            loggerFactory.CreateLogger<AudioInputManager>());

        var audioOutput = new AudioOutputManager(
            new IAudioOutputProvider[] { platformSpeaker },
            settings,
            appSettings,
            loggerFactory.CreateLogger<AudioOutputManager>());

        // BT enumerators (real MMDevice)
        var btEnum = new WindowsBluetoothEnumerator(audioInput, appSettings);
        var btOutEnum = new WindowsBluetoothOutputEnumerator(audioOutput);

        // Warm paired-device cache
        await WindowsBluetoothEnumerator.RefreshPairedDeviceCacheAsync();
        btEnum.ScanAndRegister();
        btOutEnum.ScanAndRegister();

        // Session (real WinRT BLE)
        var session = new WindowsHeyCyanGlassesSession(
            loggerFactory.CreateLogger<WindowsHeyCyanGlassesSession>(),
            btEnum,
            btOutEnum);

        // BT audio providers (MAC-aware wrappers)
        var btInput = new BluetoothAudioInputProvider(
            audioInput,
            loggerFactory.CreateLogger<BluetoothAudioInputProvider>());
        var btOutput = new BluetoothAudioOutputProvider(
            audioOutput,
            loggerFactory.CreateLogger<BluetoothAudioOutputProvider>());

        // HeyCyan-specific providers
        var mic = new HeyCyanAudioInputProvider(
            session,
            btInput,
            loggerFactory.CreateLogger<HeyCyanAudioInputProvider>());

        var speaker = new HeyCyanAudioOutputProvider(
            session,
            btOutput,
            loggerFactory.CreateLogger<HeyCyanAudioOutputProvider>());

        var camera = new HeyCyanCameraProvider(
            session,
            new NullMediaTransfer(),
            loggerFactory.CreateLogger<HeyCyanCameraProvider>());

        var button = new HeyCyanButtonProvider(
            session,
            loggerFactory.CreateLogger<HeyCyanButtonProvider>());

        // Router (real event-driven auto-select)
        var router = new HeyCyanAudioRouter(
            session, audioInput, audioOutput, mic, speaker,
            loggerFactory.CreateLogger<HeyCyanAudioRouter>());

        // Wire EndpointRegistered → Router (normally done in MainPage.xaml.cs)
        btEnum.EndpointRegistered += router.OnBtEndpointRegistered;
        btOutEnum.EndpointRegistered += router.OnBtEndpointRegistered;

        // Device manager (real orchestration)
        var deviceMgr = new HeyCyanGlassesDeviceManager(
            session, camera, mic, speaker, button, new NullMediaTransfer(), settings,
            loggerFactory.CreateLogger<HeyCyanGlassesDeviceManager>());

        return new WindowsHeyCyanRealFixture(
            session, deviceMgr, router,
            audioInput, audioOutput,
            btEnum, btOutEnum,
            mic, speaker, settings,
            loggerFactory);
    }

    /// <summary>
    /// Create a fixture with real WiFi + HTTP media transfer support.
    /// Use this for WiFi transfer tests that need <see cref="Transfer"/>.
    /// </summary>
    public static async Task<WindowsHeyCyanRealFixture> CreateWithTransferAsync()
    {
        var loggerFactory = LoggerFactory.Create(b => b
            .AddDebug()
            .SetMinimumLevel(LogLevel.Trace));

        var settings = new InMemorySettingsService();
        var appSettings = new AppSettings();

        var platformMic = new PlatformMicProvider(appSettings);
        var platformSpeaker = new WindowsSpeakerProvider();

        var audioInput = new AudioInputManager(
            new IAudioInputProvider[] { platformMic },
            settings,
            loggerFactory.CreateLogger<AudioInputManager>());

        var audioOutput = new AudioOutputManager(
            new IAudioOutputProvider[] { platformSpeaker },
            settings,
            appSettings,
            loggerFactory.CreateLogger<AudioOutputManager>());

        var btEnum = new WindowsBluetoothEnumerator(audioInput, appSettings);
        var btOutEnum = new WindowsBluetoothOutputEnumerator(audioOutput);

        await WindowsBluetoothEnumerator.RefreshPairedDeviceCacheAsync();
        btEnum.ScanAndRegister();
        btOutEnum.ScanAndRegister();

        // WiFi manager (real WinRT WiFiAdapter)
        var wifiManager = new WindowsGlassesWiFiManager(
            loggerFactory.CreateLogger<WindowsGlassesWiFiManager>());

        // WiFi Direct manager (real WinRT WiFiDirectDevice)
        var bleMac = Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC");
        var wifiDirectManager = new WindowsWiFiDirectManager(
            loggerFactory.CreateLogger<WindowsWiFiDirectManager>(),
            bleMac);

        // Session with WiFi + WiFi Direct support
        var session = new WindowsHeyCyanGlassesSession(
            loggerFactory.CreateLogger<WindowsHeyCyanGlassesSession>(),
            btEnum,
            btOutEnum,
            wifiManager,
            wifiDirectManager);

        // Real HTTP client factory (standard HttpClient, no network binding on Windows)
        var httpFactory = new WindowsHeyCyanHttpClientFactory(
            loggerFactory.CreateLogger<WindowsHeyCyanHttpClientFactory>());

        // Real media transfer
        var transfer = new HeyCyanMediaTransfer(
            session,
            httpFactory,
            loggerFactory.CreateLogger<HeyCyanMediaTransfer>());

        var btInput = new BluetoothAudioInputProvider(
            audioInput,
            loggerFactory.CreateLogger<BluetoothAudioInputProvider>());
        var btOutput = new BluetoothAudioOutputProvider(
            audioOutput,
            loggerFactory.CreateLogger<BluetoothAudioOutputProvider>());

        var mic = new HeyCyanAudioInputProvider(
            session, btInput,
            loggerFactory.CreateLogger<HeyCyanAudioInputProvider>());
        var speaker = new HeyCyanAudioOutputProvider(
            session, btOutput,
            loggerFactory.CreateLogger<HeyCyanAudioOutputProvider>());

        var camera = new HeyCyanCameraProvider(
            session, transfer,
            loggerFactory.CreateLogger<HeyCyanCameraProvider>());

        var button = new HeyCyanButtonProvider(
            session, loggerFactory.CreateLogger<HeyCyanButtonProvider>());

        var router = new HeyCyanAudioRouter(
            session, audioInput, audioOutput, mic, speaker,
            loggerFactory.CreateLogger<HeyCyanAudioRouter>());

        btEnum.EndpointRegistered += router.OnBtEndpointRegistered;
        btOutEnum.EndpointRegistered += router.OnBtEndpointRegistered;

        var deviceMgr = new HeyCyanGlassesDeviceManager(
            session, camera, mic, speaker, button, transfer, settings,
            loggerFactory.CreateLogger<HeyCyanGlassesDeviceManager>());

        return new WindowsHeyCyanRealFixture(
            session, deviceMgr, router,
            audioInput, audioOutput,
            btEnum, btOutEnum,
            mic, speaker, settings,
            loggerFactory,
            wifiManager, wifiDirectManager, transfer);
    }

    /// <summary>
    /// Scan for glasses and connect via the full DeviceManager flow.
    /// Retries scanning up to <paramref name="maxAttempts"/> times because
    /// glasses may not always be BLE-advertising.
    /// </summary>
    public async Task<HeyCyanDeviceInfo> ScanAndConnectAsync(
        string mac, TimeSpan scanTimeout, CancellationToken ct, int maxAttempts = 3)
    {
        IReadOnlyList<HeyCyanDeviceInfo> devices = [];
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            devices = await Session.ScanAsync(scanTimeout, ct);
            var target = devices.FirstOrDefault(d =>
                string.Equals(d.Address, mac, StringComparison.OrdinalIgnoreCase));

            if (target is not null)
            {
                await DeviceManager.ConnectAsync(target, ct);
                return target;
            }

            if (attempt < maxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new InvalidOperationException(
            $"Glasses {mac} not found after {maxAttempts} scan attempts. Last scan found: [{string.Join(", ", devices.Select(d => $"{d.Name} ({d.Address})"))}]");
    }

    /// <summary>
    /// Connect directly by MAC address without scanning.
    /// Constructs a <see cref="HeyCyanDeviceInfo"/> from the known MAC and
    /// connects via the full DeviceManager flow. Retries on transient GATT failures.
    /// </summary>
    public async Task<HeyCyanDeviceInfo> ConnectByAddressAsync(
        string mac, CancellationToken ct, int maxAttempts = 3)
    {
        // Derive a name from the MAC suffix (e.g. "HeyCyan_E6C9")
        var macClean = mac.Replace(":", "").Replace("-", "");
        var suffix = macClean.Length >= 4 ? macClean[^4..] : macClean;
        var device = new HeyCyanDeviceInfo($"HeyCyan_{suffix}", mac, 0);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await DeviceManager.ConnectAsync(device, ct);
                return device;
            }
            catch (InvalidOperationException) when (attempt < maxAttempts)
            {
                // GATT discovery can fail transiently — wait before retry
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }

        // Final attempt — let exceptions propagate
        await DeviceManager.ConnectAsync(device, ct);
        return device;
    }

    public async ValueTask DisposeAsync()
    {
        if (Transfer is not null)
        {
            try { await Transfer.DisposeAsync(); }
            catch { /* best-effort cleanup */ }
        }

        if (Session.State != HeyCyanState.Disconnected)
        {
            try { await Session.DisconnectAsync(CancellationToken.None); }
            catch { /* best-effort cleanup */ }
        }

        await Session.DisposeAsync();
        _loggerFactory.Dispose();
    }

    /// <summary>
    /// Minimal no-op media transfer for tests that don't exercise camera/media.
    /// </summary>
    private sealed class NullMediaTransfer : IHeyCyanMediaTransfer
    {
        public bool IsWarm => false;
        public Task<IReadOnlyList<HeyCyanMediaEntry>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<HeyCyanMediaEntry>>(Array.Empty<HeyCyanMediaEntry>());
        public Task<byte[]> DownloadAsync(string fileName, CancellationToken ct)
            => Task.FromResult(Array.Empty<byte>());
        public Task<Stream> OpenAsync(string fileName, CancellationToken ct)
            => Task.FromResult<Stream>(new MemoryStream());
        public Task ExitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }
}
