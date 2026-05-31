using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio;

public sealed class AudioRoutePolicyService : IAudioRoutePolicyService, IAsyncDisposable
{
    private readonly AudioInputManager _input;
    private readonly AudioOutputManager _output;
    private readonly IRouteMonitor _routeMonitor;
    private readonly ISettingsService _settings;
    private readonly AppSettings _appSettings;
    private readonly ILogger<AudioRoutePolicyService> _logger;

    private AudioRoutePolicy _current = AudioRoutePolicy.Default;

    public AudioRoutePolicy Current => _current;

    public event EventHandler<AudioRoutePolicy>? PolicyChanged;

    public AudioRoutePolicyService(
        AudioInputManager input,
        AudioOutputManager output,
        IRouteMonitor routeMonitor,
        ISettingsService settings,
        AppSettings appSettings,
        ILogger<AudioRoutePolicyService> logger)
    {
        _input = input;
        _output = output;
        _routeMonitor = routeMonitor;
        _settings = settings;
        _appSettings = appSettings;
        _logger = logger;

        _input.ActiveProviderChanged += OnRouteInputsChanged;
        _output.ActiveProviderChanged += OnRouteInputsChanged;
        _output.ActiveOutputRouteChanged += OnRouteInputsChanged;
        _routeMonitor.RouteChanged += OnRouteInputsChanged;

        Recompute();
    }

    public AudioRoutePolicy Recompute()
    {
        var inputCapabilities = _input.Active?.InputCapabilities ?? AudioInputCapabilities.Default;
        var outputCapabilities = GetOutputCapabilities(_output.Active);
        var speakMode = !string.Equals(_settings.OutputMode, "Silent", StringComparison.OrdinalIgnoreCase);
        var hasLocalPlayback = speakMode && _output.Active is not null;

        if (!hasLocalPlayback)
            outputCapabilities = AudioOutputCapabilities.NoLocalPlayback;

        var routeReportsHeadphones = _routeMonitor.IsHeadphonesConnected;
        var routeReportsBluetooth = _routeMonitor.IsBluetoothAudioConnected;
        var routeIsolated = outputCapabilities.EchoPathKind == EchoPathKind.Unknown && routeReportsHeadphones;
        var outputIsIsolated = outputCapabilities.IsAcousticallyIsolated || routeIsolated;
        var estimatedLatency = hasLocalPlayback
            ? Math.Max(0, inputCapabilities.EstimatedInputLatencyMs)
                + Math.Max(0, outputCapabilities.EstimatedOutputLatencyMs)
            : 0;

        var aecMode = SelectAecMode(
            hasLocalPlayback,
            inputCapabilities,
            outputCapabilities,
            outputIsIsolated);

        var cleanupMode = SelectVoiceCleanupMode();
        var explanation = BuildExplanation(
            hasLocalPlayback,
            inputCapabilities,
            outputCapabilities,
            outputIsIsolated,
            routeReportsHeadphones,
            routeReportsBluetooth,
            aecMode);

        var policy = new AudioRoutePolicy(
            inputCapabilities,
            outputCapabilities,
            hasLocalPlayback,
            routeReportsHeadphones,
            routeReportsBluetooth,
            estimatedLatency,
            aecMode,
            cleanupMode,
            explanation);

        if (!policy.Equals(_current))
        {
            _current = policy;
            _logger.LogInformation(
                "Audio route policy changed: AEC={AecMode}, cleanup={CleanupMode}, localPlayback={LocalPlayback}, output={EchoPath}, routeHeadphones={Headphones}, routeBluetooth={Bluetooth}, latency={Latency}ms, reason={Reason}",
                policy.AecMode,
                policy.VoiceCleanupMode,
                policy.HasLocalPlayback,
                policy.OutputCapabilities.EchoPathKind,
                policy.RouteReportsHeadphones,
                policy.RouteReportsBluetoothAudio,
                policy.EstimatedRoundTripLatencyMs,
                policy.Explanation);
            PolicyChanged?.Invoke(this, policy);
        }

        return _current;
    }

    private void OnRouteInputsChanged(object? sender, EventArgs e)
    {
        Recompute();
    }

    private static AudioOutputCapabilities GetOutputCapabilities(IAudioOutputProvider? provider)
    {
        if (provider is null)
            return AudioOutputCapabilities.NoLocalPlayback;

        return provider.OutputCapabilities ?? AudioOutputCapabilities.Unknown(provider.EstimatedOutputLatencyMs);
    }

    private AecMode SelectAecMode(
        bool hasLocalPlayback,
        AudioInputCapabilities input,
        AudioOutputCapabilities output,
        bool outputIsIsolated)
    {
        if (!_appSettings.AecEnabled || _appSettings.DisableAec)
            return AecMode.Off;

        if (!hasLocalPlayback)
            return AecMode.Off;

        if (outputIsIsolated || !output.NeedsEchoCancellation)
            return AecMode.Off;

        if (input.PlatformEchoCancellationActive)
            return AecMode.PlatformNative;

        return AecMode.WebRtcApm;
    }

    private VoiceCleanupMode SelectVoiceCleanupMode()
    {
        if (_appSettings.NoiseSuppressionLevel <= 0 && _appSettings.AgcCompressionGainDb <= 0)
            return VoiceCleanupMode.Off;

        if (_appSettings.AgcCompressionGainDb <= 0)
            return VoiceCleanupMode.NoiseSuppressionOnly;

        return VoiceCleanupMode.NoiseSuppressionAndAgc;
    }

    private static string BuildExplanation(
        bool hasLocalPlayback,
        AudioInputCapabilities input,
        AudioOutputCapabilities output,
        bool outputIsIsolated,
        bool routeReportsHeadphones,
        bool routeReportsBluetooth,
        AecMode aecMode)
    {
        if (!hasLocalPlayback)
            return "AEC off because output mode is Silent or no output provider is active.";

        if (aecMode == AecMode.Off && outputIsIsolated)
        {
            if (output.IsAcousticallyIsolated)
                return $"AEC off because output provider declares an isolated {output.EchoPathKind} route.";

            return "AEC off because the route monitor reports headphones for an unknown output route.";
        }

        if (aecMode == AecMode.Off && !output.NeedsEchoCancellation)
            return $"AEC off because output provider declares {output.EchoPathKind} does not need echo cancellation.";

        if (aecMode == AecMode.PlatformNative)
            return "AEC handled by the input provider's platform-native echo cancellation.";

        if (aecMode == AecMode.WebRtcApm)
            return $"WebRTC APM enabled because output provider declares {output.EchoPathKind} needs echo cancellation.";

        return $"AEC {aecMode}; routeHeadphones={routeReportsHeadphones}, routeBluetooth={routeReportsBluetooth}, platformAec={input.PlatformEchoCancellationActive}.";
    }

    public ValueTask DisposeAsync()
    {
        _input.ActiveProviderChanged -= OnRouteInputsChanged;
        _output.ActiveProviderChanged -= OnRouteInputsChanged;
        _output.ActiveOutputRouteChanged -= OnRouteInputsChanged;
        _routeMonitor.RouteChanged -= OnRouteInputsChanged;
        return ValueTask.CompletedTask;
    }
}
