using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Services.Camera.Usb;
using Microsoft.Extensions.Logging;

namespace BodyCam.ViewModels.Settings;

public sealed class UsbCameraSettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ILogger<UsbCameraSettingsViewModel> _log;
    private readonly Func<UsbCameraConnectionSettings, CancellationToken, Task<string>> _testCaptureAsync;

    private string _deviceMatch;
    private string _status = "Ready";
    private bool _isTesting;

    public UsbCameraSettingsViewModel(
        ISettingsService settings,
        IUsbCameraClient cameraClient,
        ILogger<UsbCameraSettingsViewModel> log,
        Func<UsbCameraConnectionSettings, CancellationToken, Task<string>>? testCaptureAsync = null)
    {
        _settings = settings;
        _log = log;
        _testCaptureAsync = testCaptureAsync ?? ((connectionSettings, ct) =>
            TestCaptureAsync(cameraClient, connectionSettings, ct));

        Title = "USB Camera";
        _deviceMatch = settings.UsbCameraDeviceMatch ?? UsbCameraProvider.DefaultDeviceMatch;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        TestCaptureCommand = new AsyncRelayCommand(TestCaptureAsync, () => !IsTesting);
    }

    public string DeviceMatch
    {
        get => _deviceMatch;
        set => SetProperty(ref _deviceMatch, value ?? string.Empty);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        private set
        {
            if (SetProperty(ref _isTesting, value))
                TestCaptureCommand.RaiseCanExecuteChanged();
        }
    }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand TestCaptureCommand { get; }

    public Task SaveAsync()
    {
        SaveSettings(BuildSettings());
        Status = "Saved";
        return Task.CompletedTask;
    }

    public async Task TestCaptureAsync()
    {
        var connectionSettings = BuildSettings();
        if (string.IsNullOrWhiteSpace(connectionSettings.DeviceMatch))
        {
            Status = "Enter a device name or VID/PID.";
            return;
        }

        SaveSettings(connectionSettings);
        IsTesting = true;
        Status = "Testing capture...";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var summary = await _testCaptureAsync(connectionSettings, timeout.Token);
            Status = $"Capture test succeeded: {summary}";
        }
        catch (OperationCanceledException)
        {
            Status = "Capture test timed out.";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "USB camera capture test failed");
            Status = $"Capture test failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private UsbCameraConnectionSettings BuildSettings()
    {
        return new UsbCameraConnectionSettings(DeviceMatch.Trim());
    }

    private void SaveSettings(UsbCameraConnectionSettings connectionSettings)
    {
        _settings.UsbCameraDeviceMatch = NullIfBlank(connectionSettings.DeviceMatch);
    }

    private static async Task<string> TestCaptureAsync(
        IUsbCameraClient cameraClient,
        UsbCameraConnectionSettings connectionSettings,
        CancellationToken ct)
    {
        if (!cameraClient.IsSupported)
            throw new InvalidOperationException("USB camera capture is not supported on this platform yet.");

        var result = await cameraClient.CaptureJpegAsync(
                new UsbCameraCaptureOptions(connectionSettings.DeviceMatch),
                ct)
            .ConfigureAwait(false);

        if (!result.Success || result.JpegBytes is null)
            throw new InvalidOperationException(result.Error ?? "USB camera capture failed.");

        var name = string.IsNullOrWhiteSpace(result.DeviceName)
            ? connectionSettings.DeviceMatch
            : result.DeviceName.Trim();

        return $"{name}, {result.JpegBytes.Length:N0} bytes";
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record UsbCameraConnectionSettings(string DeviceMatch);

