using BodyCam.Services.Audio;
using BodyCam.Services.Camera;

namespace BodyCam.Services;

/// <summary>
/// A bundled device configuration that maps to a single Source dropdown entry.
/// Implementations are discovered via DI (registered as IEnumerable&lt;ISourceProfile&gt;).
/// </summary>
public interface ISourceProfile
{
    /// <summary>Stable ID persisted in settings (e.g. "phone", "heycyan-glasses").</summary>
    string Id { get; }

    /// <summary>User-visible label (e.g. "Phone", "HeyCyan Glasses").</summary>
    string DisplayName { get; }

    /// <summary>Sort order in the dropdown (lower = higher).</summary>
    int Order { get; }

    /// <summary>True when all required devices for this profile are connected.</summary>
    bool IsAvailable { get; }

    /// <summary>Suffix shown when !IsAvailable (e.g. "(not connected)").</summary>
    string? UnavailableReason { get; }

    /// <summary>Priority for smart fallback (higher = preferred).</summary>
    int FallbackPriority { get; }

    /// <summary>Activate this profile's devices on the managers.</summary>
    Task ApplyAsync(CameraManager camera, AudioInputManager mic,
                    AudioOutputManager speaker, CancellationToken ct = default);

    /// <summary>Persist custom provider IDs (only meaningful for CustomSourceProfile).</summary>
    void SaveCustomSelections(ISettingsService settings) { }
}
