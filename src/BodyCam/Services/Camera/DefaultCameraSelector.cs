namespace BodyCam.Services.Camera;

/// <summary>
/// Default camera selection strategy: pick the first available provider.
/// Phone camera is typically registered last, so it acts as a natural fallback.
/// </summary>
public sealed class DefaultCameraSelector : ICameraProviderSelector
{
    public ICameraProvider Select(IReadOnlyList<ICameraProvider> providers)
    {
        return providers.FirstOrDefault(p => p.IsAvailable)
            ?? providers.First(); // Never null; at least one provider is always registered
    }
}
