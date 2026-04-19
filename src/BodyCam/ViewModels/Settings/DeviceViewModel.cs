using BodyCam.Mvvm;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;

namespace BodyCam.ViewModels.Settings;

public class DeviceViewModel : ViewModelBase
{
    private readonly CameraManager _cameraManager;
    private readonly AudioInputManager _audioInputManager;
    private readonly AudioOutputManager _audioOutputManager;

    public DeviceViewModel(CameraManager cameraManager, AudioInputManager audioInputManager, AudioOutputManager audioOutputManager)
    {
        _cameraManager = cameraManager;
        _audioInputManager = audioInputManager;
        _audioOutputManager = audioOutputManager;
        Title = "Devices";

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

    public IReadOnlyList<IAudioInputProvider> AudioInputProviders => _audioInputManager.Providers;

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

    public IReadOnlyList<IAudioOutputProvider> AudioOutputProviders => _audioOutputManager.Providers;

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
}
