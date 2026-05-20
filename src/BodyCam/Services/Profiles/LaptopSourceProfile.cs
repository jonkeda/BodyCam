using BodyCam.Services.Audio;
using BodyCam.Services.Camera;

namespace BodyCam.Services;

/// <summary>
/// Source profile for laptop/desktop — uses phone camera, system microphone, and system speaker.
/// Default on Windows.
/// </summary>
public sealed class LaptopSourceProfile : ISourceProfile
{
    public string Id => "laptop";
    public string DisplayName => "Laptop";
    public int Order => 10;
    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public int FallbackPriority => 10;

    public async Task ApplyAsync(CameraManager camera, AudioInputManager mic,
                                  AudioOutputManager speaker, CancellationToken ct = default)
    {
        await camera.SetActiveAsync("phone", ct);
        await mic.SetActiveAsync("platform", ct);
        await speaker.SetActiveAsync("windows-speaker", ct);
    }
}
