using BodyCam.Services.Glasses.HeyCyan;

namespace BodyCam.Services.Camera;

/// <summary>
/// Camera selection strategy that prefers HeyCyan glasses when connected,
/// otherwise falls back to the first available provider (typically phone camera).
/// </summary>
public sealed class HeyCyanCameraSelector : ICameraProviderSelector
{
    private readonly IHeyCyanGlassesSession? _session;

    public HeyCyanCameraSelector(IHeyCyanGlassesSession? session = null)
    {
        _session = session;
    }

    public ICameraProvider Select(IReadOnlyList<ICameraProvider> providers)
    {
        // Prefer glasses when the session is Connected (or warm in TransferMode).
        if (_session?.State is HeyCyanState.Connected or HeyCyanState.TransferMode)
        {
            var glasses = providers.FirstOrDefault(p => p.ProviderId == "heycyan-glasses");
            if (glasses is { IsAvailable: true })
            {
                return glasses;
            }
        }

        // Otherwise: first available, with phone camera as the natural fallback.
        return providers.FirstOrDefault(p => p.IsAvailable)
            ?? providers.First();
    }
}
