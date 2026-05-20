using BodyCam.Services.Audio;
using BodyCam.Services.Camera;

namespace BodyCam.Services;

/// <summary>
/// Source profile for phone/tablet — uses built-in camera, microphone, and speaker.
/// Default on Android and iOS.
/// </summary>
public sealed class PhoneSourceProfile : ISourceProfile
{
    public string Id => "phone";
    public string DisplayName => "Phone";
    public int Order => 10;
    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public int FallbackPriority => 10;

    public async Task ApplyAsync(CameraManager camera, AudioInputManager mic,
                                  AudioOutputManager speaker, CancellationToken ct = default)
    {
        await camera.SetActiveAsync("phone", ct);
        await mic.SetActiveAsync("platform", ct);

        // Phone speaker provider ID varies by platform
        var speakerProvider = speaker.Providers.FirstOrDefault(p =>
            p.ProviderId == "phone-speaker" || p.ProviderId == "windows-speaker");
        if (speakerProvider is not null)
            await speaker.SetActiveAsync(speakerProvider.ProviderId, ct);
    }
}
