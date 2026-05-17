using BodyCam.Mvvm;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;
using Microsoft.Maui.Controls;

namespace BodyCam.ViewModels.Settings;

public class DeviceViewModel : ViewModelBase, IDisposable
{
    private readonly CameraManager _cameraManager;
    private readonly AudioInputManager _audioInputManager;
    private readonly AudioOutputManager _audioOutputManager;
    private readonly HeyCyanGlassesDeviceManager _glasses;

    public DeviceViewModel(
        CameraManager cameraManager,
        AudioInputManager audioInputManager,
        AudioOutputManager audioOutputManager,
        GlassesCameraSectionViewModel glassesCameraSection,
        HeyCyanGlassesDeviceManager glasses,
        HeyCyanButtonMappingsViewModel? heyCyanButtonMappings = null)
    {
        _cameraManager = cameraManager;
        _audioInputManager = audioInputManager;
        _audioOutputManager = audioOutputManager;
        _glasses = glasses;
        GlassesCameraSection = glassesCameraSection;
        HeyCyanButtonMappings = heyCyanButtonMappings;
        Title = "Devices";

        ConnectGlassesCommand = new AsyncRelayCommand(async () =>
            await Shell.Current.GoToAsync("glasses"));
        DisconnectGlassesCommand = new AsyncRelayCommand(
            () => _glasses.DisconnectAsync(CancellationToken.None),
            () => IsGlassesConnected);
        TestSoundCommand = new AsyncRelayCommand(TestSoundAsync);
        TestRecordingCommand = new AsyncRelayCommand(TestRecordingAsync);

        _glasses.StateChanged += OnGlassesStateChanged;
        _glasses.StatusChanged += (_, _) => RefreshGlassesInfo();

        _audioInputManager.ProvidersChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AudioInputProviders));
            OnPropertyChanged(nameof(SelectedAudioInputProvider));
        };

        _audioOutputManager.ProvidersChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AudioOutputProviders));
            OnPropertyChanged(nameof(SelectedAudioOutputProvider));
        };
    }

    // ── Commands ────────────────────────────────────────────────────────────

    public AsyncRelayCommand ConnectGlassesCommand { get; }
    public AsyncRelayCommand DisconnectGlassesCommand { get; }
    public AsyncRelayCommand TestSoundCommand { get; }
    public AsyncRelayCommand TestRecordingCommand { get; }

    // ── Glasses info ────────────────────────────────────────────────────────

    public bool IsGlassesConnected => _glasses.State == GlassesConnectionState.Connected;
    public string GlassesName => _glasses.State == GlassesConnectionState.Connected
        ? _glasses.MacAddress ?? "Glasses" : string.Empty;
    public int GlassesBatteryPct => _glasses.Battery?.Percentage ?? 0;
    public bool GlassesIsCharging => _glasses.Battery?.IsCharging ?? false;
    public string GlassesMac => _glasses.MacAddress ?? "—";
    public string GlassesFirmware => _glasses.Version?.Firmware ?? "—";
    public string GlassesHardware => _glasses.Version?.Hardware ?? "—";
    public int GlassesPhotos => _glasses.MediaCount?.Photos ?? 0;
    public int GlassesVideos => _glasses.MediaCount?.Videos ?? 0;
    public int GlassesAudioFiles => _glasses.MediaCount?.AudioFiles ?? 0;

    // ── Audio test state ────────────────────────────────────────────────────

    private bool _isTestingSound;
    public bool IsTestingSound
    {
        get => _isTestingSound;
        private set => SetProperty(ref _isTestingSound, value);
    }

    private bool _isTestingRecording;
    public bool IsTestingRecording
    {
        get => _isTestingRecording;
        private set => SetProperty(ref _isTestingRecording, value);
    }

    private string? _testRecordingStatus;
    public string? TestRecordingStatus
    {
        get => _testRecordingStatus;
        private set => SetProperty(ref _testRecordingStatus, value);
    }

    public GlassesCameraSectionViewModel GlassesCameraSection { get; }

    public HeyCyanButtonMappingsViewModel? HeyCyanButtonMappings { get; }

    public IReadOnlyList<ICameraProvider> CameraProviders => _cameraManager.Providers;

    public ICameraProvider? SelectedCameraProvider
    {
        get => _cameraManager.Active;
        set
        {
            if (value is not null && value != _cameraManager.Active)
            {
                _ = _cameraManager.SetActiveAsync(value.ProviderId);
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<IAudioInputProvider> AudioInputProviders =>
        _audioInputManager.Providers.Where(p => p.IsAvailable).ToList().AsReadOnly();

    public IAudioInputProvider? SelectedAudioInputProvider
    {
        get => _audioInputManager.Active;
        set
        {
            if (value is not null && value != _audioInputManager.Active)
            {
                _ = _audioInputManager.SetActiveAsync(value.ProviderId);
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<IAudioOutputProvider> AudioOutputProviders =>
        _audioOutputManager.Providers.Where(p => p.IsAvailable).ToList().AsReadOnly();

    public IAudioOutputProvider? SelectedAudioOutputProvider
    {
        get => _audioOutputManager.Active;
        set
        {
            if (value is not null && value != _audioOutputManager.Active)
            {
                _ = _audioOutputManager.SetActiveAsync(value.ProviderId);
                OnPropertyChanged();
            }
        }
    }

    public void Dispose()
    {
        _glasses.StateChanged -= OnGlassesStateChanged;
        GlassesCameraSection?.Dispose();
    }

    // ── Glasses state handling ──────────────────────────────────────────────

    private void OnGlassesStateChanged(object? sender, GlassesConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            RefreshGlassesInfo();
            DisconnectGlassesCommand.RaiseCanExecuteChanged();

            // Refresh filtered provider lists (IsAvailable changes with connection state)
            OnPropertyChanged(nameof(AudioInputProviders));
            OnPropertyChanged(nameof(AudioOutputProviders));

            if (state == GlassesConnectionState.Connected)
                await AutoSelectGlassesProvidersAsync();
            else if (state == GlassesConnectionState.Disconnected)
                await RevertToDefaultProvidersAsync();
        });
    }

    private async Task AutoSelectGlassesProvidersAsync()
    {
        const string id = "heycyan-glasses";
        if (_cameraManager.Providers.Any(p => p.ProviderId == id))
            await _cameraManager.SetActiveAsync(id);
        if (_audioInputManager.Providers.Any(p => p.ProviderId == id))
            await _audioInputManager.SetActiveAsync(id);
        if (_audioOutputManager.Providers.Any(p => p.ProviderId == id))
            await _audioOutputManager.SetActiveAsync(id);

        OnPropertyChanged(nameof(SelectedCameraProvider));
        OnPropertyChanged(nameof(SelectedAudioInputProvider));
        OnPropertyChanged(nameof(SelectedAudioOutputProvider));
    }

    private async Task RevertToDefaultProvidersAsync()
    {
        var defaultCam = _cameraManager.Providers.FirstOrDefault(p => p.ProviderId != "heycyan-glasses");
        var defaultMic = _audioInputManager.Providers.FirstOrDefault(p => p.ProviderId != "heycyan-glasses");
        var defaultSpk = _audioOutputManager.Providers.FirstOrDefault(p => p.ProviderId != "heycyan-glasses");

        if (defaultCam is not null) await _cameraManager.SetActiveAsync(defaultCam.ProviderId);
        if (defaultMic is not null) await _audioInputManager.SetActiveAsync(defaultMic.ProviderId);
        if (defaultSpk is not null) await _audioOutputManager.SetActiveAsync(defaultSpk.ProviderId);

        OnPropertyChanged(nameof(SelectedCameraProvider));
        OnPropertyChanged(nameof(SelectedAudioInputProvider));
        OnPropertyChanged(nameof(SelectedAudioOutputProvider));
    }

    private void RefreshGlassesInfo()
    {
        OnPropertyChanged(nameof(IsGlassesConnected));
        OnPropertyChanged(nameof(GlassesName));
        OnPropertyChanged(nameof(GlassesBatteryPct));
        OnPropertyChanged(nameof(GlassesIsCharging));
        OnPropertyChanged(nameof(GlassesMac));
        OnPropertyChanged(nameof(GlassesFirmware));
        OnPropertyChanged(nameof(GlassesHardware));
        OnPropertyChanged(nameof(GlassesPhotos));
        OnPropertyChanged(nameof(GlassesVideos));
        OnPropertyChanged(nameof(GlassesAudioFiles));
    }

    // ── Audio tests ─────────────────────────────────────────────────────────

    private async Task TestSoundAsync()
    {
        var provider = _audioOutputManager.Active;
        if (provider is null) return;

        IsTestingSound = true;
        try
        {
            // Generate 1-second 440 Hz sine wave, 16kHz mono PCM16
            const int sampleRate = 16000;
            const int durationMs = 1000;
            const double frequency = 440.0;
            var samples = sampleRate * durationMs / 1000;
            var pcm = new byte[samples * 2];
            for (int i = 0; i < samples; i++)
            {
                var value = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * 8000);
                pcm[i * 2] = (byte)(value & 0xFF);
                pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            await provider.StartAsync(sampleRate);
            await provider.PlayChunkAsync(pcm);
            await Task.Delay(durationMs + 200);
            await provider.StopAsync();
        }
        finally
        {
            IsTestingSound = false;
        }
    }

    private async Task TestRecordingAsync()
    {
        var input = _audioInputManager.Active;
        var output = _audioOutputManager.Active;
        if (input is null || output is null) return;

        IsTestingRecording = true;
        var buffer = new List<byte[]>();
        void OnChunk(object? _, byte[] chunk) => buffer.Add(chunk);

        try
        {
            // Record 3 seconds
            TestRecordingStatus = "Recording…";
            input.AudioChunkAvailable += OnChunk;
            await input.StartAsync();
            await Task.Delay(3000);
            await input.StopAsync();
            input.AudioChunkAvailable -= OnChunk;

            if (buffer.Count == 0)
            {
                TestRecordingStatus = "No audio captured";
                return;
            }

            // Playback
            TestRecordingStatus = "Playing back…";
            await output.StartAsync(16000);
            foreach (var chunk in buffer)
                await output.PlayChunkAsync(chunk);
            await Task.Delay(500);
            await output.StopAsync();
            TestRecordingStatus = $"Done — {buffer.Count} chunks recorded";
        }
        catch (Exception ex)
        {
            TestRecordingStatus = $"Error: {ex.Message}";
        }
        finally
        {
            input.AudioChunkAvailable -= OnChunk;
            IsTestingRecording = false;
        }
    }
}
