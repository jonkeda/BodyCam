using BodyCam.Models;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;

namespace BodyCam.Services;

/// <summary>
/// Source profile for custom device selection — user picks camera, mic, and speaker individually.
/// Always available.
/// </summary>
public sealed class CustomSourceProfile : ISourceProfile
{
    public string Id => "custom";
    public string DisplayName => "Custom";
    public int Order => 100;
    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public int FallbackPriority => 0;

    public async Task ApplyAsync(CameraManager camera, AudioInputManager mic,
                                  AudioOutputManager speaker, CancellationToken ct = default)
    {
        // Noop — individual pickers drive the managers directly when profile is Custom.
        // The ViewModel calls SetActiveAsync on each manager when the user changes a picker.
        await Task.CompletedTask;
    }

    public void SaveCustomSelections(ISettingsService settings)
    {
        var ds = settings.DeviceSettings;
        ds.Custom.CameraProviderId = ds.Active.CameraProviderId;
        ds.Custom.AudioInputProviderId = ds.Active.AudioInputProviderId;
        ds.Custom.AudioOutputProviderId = ds.Active.AudioOutputProviderId;
        settings.DeviceSettings = ds;
    }
}
