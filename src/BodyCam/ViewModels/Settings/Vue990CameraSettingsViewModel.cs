using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Services.Camera.A9.Vue990;
using Microsoft.Extensions.Logging;

namespace BodyCam.ViewModels.Settings;

public sealed class Vue990CameraSettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ILogger<Vue990CameraSettingsViewModel> _log;
    private readonly Func<Vue990CameraConnectionSettings, CancellationToken, Task<string>> _testConnectionAsync;

    private string _cameraHost;
    private string _status = "Ready";
    private bool _isTesting;

    public Vue990CameraSettingsViewModel(
        ISettingsService settings,
        ILogger<Vue990CameraSettingsViewModel> log,
        Func<Vue990CameraConnectionSettings, CancellationToken, Task<string>>? testConnectionAsync = null)
    {
        _settings = settings;
        _log = log;
        _testConnectionAsync = testConnectionAsync ?? TestConnectionWithStatusAsync;

        Title = "Vue990 Camera";
        _cameraHost = settings.Vue990CameraIp ?? Vue990CameraProvider.DefaultHost;

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsTesting);
    }

    public string CameraHost
    {
        get => _cameraHost;
        set => SetProperty(ref _cameraHost, value ?? string.Empty);
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
                TestConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand TestConnectionCommand { get; }

    public Task SaveAsync()
    {
        SaveSettings(BuildSettings());
        Status = "Saved";
        return Task.CompletedTask;
    }

    public async Task TestConnectionAsync()
    {
        var connectionSettings = BuildSettings();
        if (string.IsNullOrWhiteSpace(connectionSettings.Host))
        {
            Status = "Enter a host.";
            return;
        }

        SaveSettings(connectionSettings);
        IsTesting = true;
        Status = "Testing connection...";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var summary = await _testConnectionAsync(connectionSettings, timeout.Token);
            Status = $"Connection test succeeded: {summary}";
        }
        catch (OperationCanceledException)
        {
            Status = "Connection test timed out.";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Vue990 connection test failed");
            Status = $"Connection test failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private Vue990CameraConnectionSettings BuildSettings()
    {
        return new Vue990CameraConnectionSettings(CameraHost.Trim());
    }

    private void SaveSettings(Vue990CameraConnectionSettings connectionSettings)
    {
        _settings.Vue990CameraIp = NullIfBlank(connectionSettings.Host);
    }

    private static async Task<string> TestConnectionWithStatusAsync(
        Vue990CameraConnectionSettings connectionSettings,
        CancellationToken ct)
    {
        var status = await new A9Vue990StatusClient()
            .GetStatusAsync(new A9Vue990StatusOptions
            {
                Host = connectionSettings.Host,
                Timeout = TimeSpan.FromSeconds(5),
            }, ct)
            .ConfigureAwait(false);

        if (!status.Success)
            throw new InvalidOperationException(status.Error ?? "Vue990 status request failed.");

        return string.IsNullOrWhiteSpace(status.Alias)
            ? status.DeviceId ?? status.RealDeviceId ?? connectionSettings.Host
            : $"{status.Alias} {status.RealDeviceId}".Trim();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record Vue990CameraConnectionSettings(string Host);
