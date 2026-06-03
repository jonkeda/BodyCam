using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace BodyCam.Services;

/// <summary>
/// Coordinates long-lived runtime startup and cross-service device policy.
/// Page code should trigger this service, not own the startup sequence itself.
/// </summary>
public sealed class AppRuntimeCoordinator : IAppRuntimeCoordinator, IAsyncDisposable
{
    private readonly CameraManager _cameraManager;
    private readonly AudioInputManager _audioInputManager;
    private readonly AudioOutputManager _audioOutputManager;
    private readonly ButtonInputManager _buttonInputManager;
    private readonly IButtonMappingStore _buttonMappingStore;
    private readonly SourceProfileManager _sourceProfileManager;
    private readonly IServiceProvider _services;
    private readonly ILogger<AppRuntimeCoordinator> _log;
    private readonly SemaphoreSlim _startupGate = new(1, 1);
    private readonly SemaphoreSlim _profilePolicyGate = new(1, 1);

    private bool _started;
    private bool _buttonInputStarted;
    private bool _listenersWired;
    private HeyCyanGlassesDeviceManager? _glassesManager;
    private HeyCyanAudioRouter? _heyCyanAudioRouter;

#if WINDOWS
    private BodyCam.Platforms.Windows.Audio.WindowsBluetoothEnumerator? _windowsBluetoothInput;
    private BodyCam.Platforms.Windows.Audio.WindowsBluetoothOutputEnumerator? _windowsBluetoothOutput;
#elif ANDROID
    private BodyCam.Platforms.Android.Audio.AndroidBluetoothEnumerator? _androidBluetoothInput;
    private BodyCam.Platforms.Android.Audio.AndroidBluetoothOutputEnumerator? _androidBluetoothOutput;
#endif

    public AppRuntimeCoordinator(
        CameraManager cameraManager,
        AudioInputManager audioInputManager,
        AudioOutputManager audioOutputManager,
        ButtonInputManager buttonInputManager,
        IButtonMappingStore buttonMappingStore,
        SourceProfileManager sourceProfileManager,
        IServiceProvider services,
        ILogger<AppRuntimeCoordinator> log)
    {
        _cameraManager = cameraManager;
        _audioInputManager = audioInputManager;
        _audioOutputManager = audioOutputManager;
        _buttonInputManager = buttonInputManager;
        _buttonMappingStore = buttonMappingStore;
        _sourceProfileManager = sourceProfileManager;
        _services = services;
        _log = log;
    }

    public bool IsStarted => _started;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _startupGate.WaitAsync(ct);
        try
        {
            if (_started)
                return;

            _log.LogInformation("Starting app runtime");

            InstantiateLongLivedServices();
            await _buttonMappingStore.LoadAsync();

            await _cameraManager.InitializeAsync(ct);
            await _audioInputManager.InitializeAsync(ct);
            await _audioOutputManager.InitializeAsync(ct);

            PreparePlatformEnumerators();
            WireLongLivedListeners();
            await StartPlatformEnumeratorsAsync(ct);

            await _profilePolicyGate.WaitAsync(ct);
            try
            {
                await _sourceProfileManager.InitializeAsync(ct);
            }
            finally
            {
                _profilePolicyGate.Release();
            }

            await _buttonInputManager.StartAsync(ct);
            _buttonInputStarted = true;

            _started = true;
            StartKnownDeviceReconnect();

            _log.LogInformation("App runtime started");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start app runtime");
            throw;
        }
        finally
        {
            _startupGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _startupGate.WaitAsync(ct);
        try
        {
            if (!_started)
                return;

            if (_buttonInputStarted)
            {
                await _buttonInputManager.StopAsync();
                _buttonInputStarted = false;
            }

            StopPlatformEnumerators();
            UnwireLongLivedListeners();
            _started = false;

            _log.LogInformation("App runtime stopped");
        }
        finally
        {
            _startupGate.Release();
        }
    }

    private void InstantiateLongLivedServices()
    {
        _ = _services.GetService<AecBypassManager>();
        _heyCyanAudioRouter ??= _services.GetService<HeyCyanAudioRouter>();
        _glassesManager ??= _services.GetService<HeyCyanGlassesDeviceManager>();
    }

    private void PreparePlatformEnumerators()
    {
#if WINDOWS
        _windowsBluetoothInput ??= _services.GetService<BodyCam.Platforms.Windows.Audio.WindowsBluetoothEnumerator>();
        _windowsBluetoothOutput ??= _services.GetService<BodyCam.Platforms.Windows.Audio.WindowsBluetoothOutputEnumerator>();
#elif ANDROID
        _androidBluetoothInput ??= _services.GetService<BodyCam.Platforms.Android.Audio.AndroidBluetoothEnumerator>();
        _androidBluetoothOutput ??= _services.GetService<BodyCam.Platforms.Android.Audio.AndroidBluetoothOutputEnumerator>();
#endif
    }

    private async Task StartPlatformEnumeratorsAsync(CancellationToken ct)
    {
#if WINDOWS
        _windowsBluetoothInput?.ScanAndRegister();
        _windowsBluetoothInput?.StartListening();

        _windowsBluetoothOutput?.ScanAndRegister();
        _windowsBluetoothOutput?.StartListening();
#elif ANDROID
        var btStatus = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
        if (btStatus == PermissionStatus.Granted)
        {
            _androidBluetoothInput?.ScanAndRegister();
            _androidBluetoothInput?.StartListening();

            _androidBluetoothOutput?.ScanAndRegister();
            _androidBluetoothOutput?.StartListening();
        }
#else
        await Task.CompletedTask;
#endif
    }

    private void StopPlatformEnumerators()
    {
#if WINDOWS
        _windowsBluetoothInput?.StopListening();
        _windowsBluetoothOutput?.StopListening();
#elif ANDROID
        _androidBluetoothInput?.StopListening();
        _androidBluetoothOutput?.StopListening();
#endif
    }

    private void WireLongLivedListeners()
    {
        if (_listenersWired)
            return;

#if WINDOWS
        if (_heyCyanAudioRouter is not null)
        {
            if (_windowsBluetoothInput is not null)
                _windowsBluetoothInput.EndpointRegistered += _heyCyanAudioRouter.OnBtEndpointRegistered;
            if (_windowsBluetoothOutput is not null)
                _windowsBluetoothOutput.EndpointRegistered += _heyCyanAudioRouter.OnBtEndpointRegistered;
        }
#endif

        _audioInputManager.ProvidersChanged += OnAudioProvidersChanged;
        _audioOutputManager.ProvidersChanged += OnAudioProvidersChanged;

        if (_glassesManager is not null)
            _glassesManager.StateChanged += OnGlassesStateChanged;

        _listenersWired = true;
    }

    private void UnwireLongLivedListeners()
    {
        if (!_listenersWired)
            return;

#if WINDOWS
        if (_heyCyanAudioRouter is not null)
        {
            if (_windowsBluetoothInput is not null)
                _windowsBluetoothInput.EndpointRegistered -= _heyCyanAudioRouter.OnBtEndpointRegistered;
            if (_windowsBluetoothOutput is not null)
                _windowsBluetoothOutput.EndpointRegistered -= _heyCyanAudioRouter.OnBtEndpointRegistered;
        }
#endif

        _audioInputManager.ProvidersChanged -= OnAudioProvidersChanged;
        _audioOutputManager.ProvidersChanged -= OnAudioProvidersChanged;

        if (_glassesManager is not null)
            _glassesManager.StateChanged -= OnGlassesStateChanged;

        _listenersWired = false;
    }

    private void StartKnownDeviceReconnect()
    {
        if (_glassesManager is null)
            return;

        _ = RunLoggedAsync(
            () => _glassesManager.TryAutoReconnectAsync(),
            "Known-device auto-reconnect failed");
    }

    private void OnAudioProvidersChanged(object? sender, EventArgs e)
    {
        _ = RunSourceProfilePolicyAsync(
            ct => _sourceProfileManager.HandleDeviceChangedAsync(ct),
            "Source profile update after provider change failed");
    }

    private void OnGlassesStateChanged(object? sender, GlassesConnectionState state)
    {
        switch (state)
        {
            case GlassesConnectionState.Connected:
                _ = RunSourceProfilePolicyAsync(
                    ct => _sourceProfileManager.HandleDeviceConnectedAsync(ct),
                    "Source profile update after glasses connect failed");
                break;

            case GlassesConnectionState.Disconnected:
                _ = RunSourceProfilePolicyAsync(
                    ct => _sourceProfileManager.HandleDeviceDisconnectedAsync(ct),
                    "Source profile update after glasses disconnect failed");
                break;
        }
    }

    private async Task RunSourceProfilePolicyAsync(
        Func<CancellationToken, Task> policy,
        string failureMessage)
    {
        await _profilePolicyGate.WaitAsync();
        try
        {
            await policy(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{FailureMessage}", failureMessage);
        }
        finally
        {
            _profilePolicyGate.Release();
        }
    }

    private async Task RunLoggedAsync(Func<Task> action, string failureMessage)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{FailureMessage}", failureMessage);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _profilePolicyGate.Dispose();
        _startupGate.Dispose();
    }
}
