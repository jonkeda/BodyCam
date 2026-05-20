using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses.HeyCyan;

namespace BodyCam.Services;

/// <summary>
/// Source profile for HeyCyan smart glasses — uses glasses camera, microphone, and speaker.
/// Available only when glasses are connected.
/// </summary>
public sealed class HeyCyanSourceProfile : ISourceProfile
{
    private readonly IHeyCyanGlassesSession _session;

    public HeyCyanSourceProfile(IHeyCyanGlassesSession session) => _session = session;

    public string Id => "heycyan-glasses";
    public string DisplayName => "HeyCyan Glasses";
    public int Order => 20;

    public bool IsAvailable =>
        _session.State == HeyCyanState.Connected ||
        _session.State == HeyCyanState.TransferMode;

    public string? UnavailableReason => IsAvailable ? null : "(not connected)";
    public int FallbackPriority => 100;

    public async Task ApplyAsync(CameraManager camera, AudioInputManager mic,
                                  AudioOutputManager speaker, CancellationToken ct = default)
    {
        await camera.SetActiveAsync("heycyan-glasses", ct);
        await mic.SetActiveAsync("heycyan-glasses", ct);
        await speaker.SetActiveAsync("heycyan-glasses", ct);
    }
}
