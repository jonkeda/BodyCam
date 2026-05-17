namespace BodyCam.Services.Camera;

/// <summary>
/// Strategy for selecting which camera provider should be active
/// when multiple providers are available.
/// </summary>
public interface ICameraProviderSelector
{
    /// <summary>
    /// Select the best camera provider from the available set.
    /// Must return a non-null provider (never fails).
    /// </summary>
    ICameraProvider Select(IReadOnlyList<ICameraProvider> providers);
}
