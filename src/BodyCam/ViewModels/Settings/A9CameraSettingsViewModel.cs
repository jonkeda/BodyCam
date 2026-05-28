using BodyCam.Mvvm;
using BodyCam.Services;
using BodyCam.Services.Camera.A9;
using Microsoft.Extensions.Logging;

namespace BodyCam.ViewModels.Settings;

public sealed class A9CameraSettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ILogger<A9CameraSettingsViewModel> _log;
    private readonly Func<A9CameraConnectionSettings, CancellationToken, Task> _testConnectionAsync;

    private string _a9CameraIp;
    private string _a9CameraUid;
    private string _a9CameraUsername;
    private string _a9CameraPassword;
    private string _status = "Ready";
    private bool _isTesting;

    public A9CameraSettingsViewModel(
        ISettingsService settings,
        ILogger<A9CameraSettingsViewModel> log,
        Func<A9CameraConnectionSettings, CancellationToken, Task>? testConnectionAsync = null)
    {
        _settings = settings;
        _log = log;
        _testConnectionAsync = testConnectionAsync ?? TestConnectionWithSessionAsync;

        Title = "A9 Camera";
        _a9CameraIp = settings.A9CameraIp ?? string.Empty;
        _a9CameraUid = settings.A9CameraUid ?? string.Empty;
        _a9CameraUsername = DefaultIfBlank(settings.A9CameraUsername);
        _a9CameraPassword = DefaultIfBlank(settings.A9CameraPassword);

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsTesting);
    }

    public string A9CameraIp
    {
        get => _a9CameraIp;
        set => SetProperty(ref _a9CameraIp, value ?? string.Empty);
    }

    public string A9CameraUid
    {
        get => _a9CameraUid;
        set => SetProperty(ref _a9CameraUid, value ?? string.Empty);
    }

    public string A9CameraUsername
    {
        get => _a9CameraUsername;
        set => SetProperty(ref _a9CameraUsername, value ?? string.Empty);
    }

    public string A9CameraPassword
    {
        get => _a9CameraPassword;
        set => SetProperty(ref _a9CameraPassword, value ?? string.Empty);
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
        if (string.IsNullOrWhiteSpace(connectionSettings.Ip))
        {
            Status = "Enter an IP address.";
            return;
        }

        SaveSettings(connectionSettings);
        IsTesting = true;
        Status = "Testing connection...";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await _testConnectionAsync(connectionSettings, timeout.Token);
            Status = "Connection test succeeded.";
        }
        catch (OperationCanceledException)
        {
            Status = "Connection test timed out.";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "A9 connection test failed");
            Status = $"Connection test failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private A9CameraConnectionSettings BuildSettings()
    {
        return new A9CameraConnectionSettings(
            A9CameraIp.Trim(),
            NullIfBlank(A9CameraUid),
            DefaultIfBlank(A9CameraUsername),
            DefaultIfBlank(A9CameraPassword));
    }

    private void SaveSettings(A9CameraConnectionSettings connectionSettings)
    {
        _settings.A9CameraIp = NullIfBlank(connectionSettings.Ip);
        _settings.A9CameraUid = connectionSettings.Uid;
        _settings.A9CameraUsername = connectionSettings.Username;
        _settings.A9CameraPassword = connectionSettings.Password;

        A9CameraUsername = connectionSettings.Username;
        A9CameraPassword = connectionSettings.Password;
    }

    private async Task TestConnectionWithSessionAsync(
        A9CameraConnectionSettings connectionSettings,
        CancellationToken ct)
    {
        var session = new A9Session(
            connectionSettings.Ip,
            connectionSettings.Username,
            connectionSettings.Password,
            _log);

        try
        {
            await session.ConnectAsync(ct);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static string DefaultIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "admin" : value.Trim();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record A9CameraConnectionSettings(
    string Ip,
    string? Uid,
    string Username,
    string Password);
