using BodyCam.Mvvm;
using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BodyCam.ViewModels.Settings;

/// <summary>
/// ViewModel for the Glasses Camera section in Device Settings.
/// Shows connection status, battery, and a "Test Capture" button
/// to validate the end-to-end glasses capture pipeline.
/// </summary>
public sealed class GlassesCameraSectionViewModel : ViewModelBase, IDisposable
{
    private readonly IHeyCyanGlassesSession? _session;
    private readonly HeyCyanCameraProvider? _provider;
    private readonly ILogger<GlassesCameraSectionViewModel> _log;

    private string _status = "Not available on this platform";
    private bool _isTestCaptureEnabled;
    private ImageSource? _lastCaptureImage;
    private long? _lastCaptureLatencyMs;

    public GlassesCameraSectionViewModel(
        IHeyCyanGlassesSession? session,
        HeyCyanCameraProvider? provider,
        ILogger<GlassesCameraSectionViewModel> log)
    {
        _session = session;
        _provider = provider;
        _log = log;

        TestCaptureCommand = new AsyncRelayCommand(TestCaptureAsync);

        // Only wire up events if we have a session (Android)
        if (_session is not null && _provider is not null)
        {
            _session.StateChanged += OnStateChanged;
            _session.BatteryUpdated += OnBatteryUpdated;
            _session.MediaCountUpdated += OnMediaCountUpdated;

            UpdateStatus();
            UpdateTestCaptureEnabled();
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsTestCaptureEnabled
    {
        get => _isTestCaptureEnabled;
        private set => SetProperty(ref _isTestCaptureEnabled, value);
    }

    public ImageSource? LastCaptureImage
    {
        get => _lastCaptureImage;
        private set => SetProperty(ref _lastCaptureImage, value);
    }

    public long? LastCaptureLatencyMs
    {
        get => _lastCaptureLatencyMs;
        private set => SetProperty(ref _lastCaptureLatencyMs, value);
    }

    public AsyncRelayCommand TestCaptureCommand { get; }

    private void OnStateChanged(object? sender, HeyCyanState state)
    {
        UpdateStatus();
        UpdateTestCaptureEnabled();
    }

    private void OnBatteryUpdated(object? sender, HeyCyanBattery battery)
    {
        UpdateStatus();
    }

    private void OnMediaCountUpdated(object? sender, HeyCyanMediaCount count)
    {
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_session is null || _provider is null)
        {
            Status = "Not available on this platform";
            return;
        }

        var state = _session.State;
        var battery = _session.LastMediaCount is not null
            ? $" | {_session.LastMediaCount.Photos} photos"
            : string.Empty;

        Status = state switch
        {
            HeyCyanState.Disconnected => "Disconnected",
            HeyCyanState.Scanning => "Scanning…",
            HeyCyanState.Connecting => "Connecting…",
            HeyCyanState.Connected => $"Connected{battery}",
            HeyCyanState.TransferMode => $"Transfer mode (warm){battery}",
            HeyCyanState.Disconnecting => "Disconnecting…",
            _ => "Unknown"
        };
    }

    private void UpdateTestCaptureEnabled()
    {
        IsTestCaptureEnabled = _provider?.IsAvailable ?? false;
    }

    private async Task TestCaptureAsync()
    {
        if (_provider is null)
        {
            Status = "Error: Provider not available";
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            Status = "Capturing…";
            var jpg = await _provider.CaptureFrameAsync(CancellationToken.None);
            sw.Stop();

            if (jpg is null)
            {
                Status = "Error: Capture returned null";
                LastCaptureImage = null;
                LastCaptureLatencyMs = null;
            }
            else
            {
                LastCaptureImage = ImageSource.FromStream(() => new MemoryStream(jpg));
                LastCaptureLatencyMs = sw.ElapsedMilliseconds;
                var suffix = _provider.IsStoredImageDownloadFallback
                    ? " (stored image fallback)"
                    : string.Empty;
                Status = $"Captured {jpg.Length:N0} bytes in {sw.ElapsedMilliseconds} ms{suffix}";
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
            LastCaptureImage = null;
            LastCaptureLatencyMs = null;
            _log.LogError(ex, "Test capture failed");
        }
    }

    public void Dispose()
    {
        if (_session is not null)
        {
            _session.StateChanged -= OnStateChanged;
            _session.BatteryUpdated -= OnBatteryUpdated;
            _session.MediaCountUpdated -= OnMediaCountUpdated;
        }
    }
}
