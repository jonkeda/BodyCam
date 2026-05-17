namespace BodyCam.Services.Glasses;

using BodyCam.Services.Camera;
using BodyCam.Services.Audio;
using BodyCam.Services.Input;
using BodyCam.Mvvm;

/// <summary>
/// Base class for glasses device managers. Coordinates camera, audio, and button providers
/// for smart glasses, and projects vendor-specific connection state onto a common model.
/// </summary>
public class GlassesDeviceManager : ObservableObject
{
    protected readonly ICameraProvider _cameraProvider;
    protected readonly IAudioInputProvider _audioInputProvider;
    protected readonly IAudioOutputProvider _audioOutputProvider;
    protected readonly IButtonInputProvider _buttonInputProvider;

    private GlassesConnectionState _state;

    public GlassesDeviceManager(
        ICameraProvider cameraProvider,
        IAudioInputProvider audioInputProvider,
        IAudioOutputProvider audioOutputProvider,
        IButtonInputProvider buttonInputProvider)
    {
        _cameraProvider = cameraProvider;
        _audioInputProvider = audioInputProvider;
        _audioOutputProvider = audioOutputProvider;
        _buttonInputProvider = buttonInputProvider;
    }

    public GlassesConnectionState State
    {
        get => _state;
        protected set => SetProperty(ref _state, value);
    }

    public event EventHandler<GlassesConnectionState>? StateChanged;

    protected void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, State);
    }
}

public enum GlassesConnectionState
{
    Disconnected,
    Scanning,
    Connecting,
    Connected,
    Disconnecting
}
