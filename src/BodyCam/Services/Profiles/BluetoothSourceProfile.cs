using BodyCam.Services.Audio;
using BodyCam.Services.Camera;

namespace BodyCam.Services;

/// <summary>
/// Source profile for Bluetooth audio — uses phone/system camera with BT mic and speaker.
/// Available only when a Bluetooth audio device is connected.
/// </summary>
public sealed class BluetoothSourceProfile : ISourceProfile
{
    private readonly IBluetoothAudioInputProvider _btInput;
    private readonly IBluetoothAudioOutputProvider _btOutput;

    public BluetoothSourceProfile(
        IBluetoothAudioInputProvider btInput,
        IBluetoothAudioOutputProvider btOutput)
    {
        _btInput = btInput;
        _btOutput = btOutput;
    }

    public string Id => "bluetooth";
    public string DisplayName => "Bluetooth Audio";
    public int Order => 30;
    public bool IsAvailable => _btInput.IsAvailable || _btOutput.IsAvailable;
    public string? UnavailableReason => IsAvailable ? null : "(no device paired)";
    public int FallbackPriority => 50;

    public async Task ApplyAsync(CameraManager camera, AudioInputManager mic,
                                  AudioOutputManager speaker, CancellationToken ct = default)
    {
        // Camera stays on phone/system default
        var cameraProvider = camera.Providers.FirstOrDefault(p => p.ProviderId == "phone");
        if (cameraProvider is not null)
            await camera.SetActiveAsync("phone", ct);

        await mic.SetActiveAsync("bluetooth-generic", ct);
        await speaker.SetActiveAsync("bluetooth-generic", ct);
    }
}
