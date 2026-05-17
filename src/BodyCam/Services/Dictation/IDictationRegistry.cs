namespace BodyCam.Services.Dictation;

/// <summary>
/// M16 dictation registry contract.
/// Stub interface for future M16 dictation feature.
/// When M16 is implemented, this interface will be defined in the M16 module.
/// </summary>
public interface IDictationRegistry
{
    /// <summary>
    /// Register a dictation source for the given local URI.
    /// The registry ensures idempotency via the sha256 hash.
    /// </summary>
    void Register(IDictationSource source, string localUri, string sha256);
}
