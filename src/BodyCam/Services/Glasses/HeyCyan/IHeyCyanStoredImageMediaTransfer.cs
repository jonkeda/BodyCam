namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Marker for a media transfer implementation that intentionally substitutes
/// stored bytes for the WiFi media download step.
/// </summary>
public interface IHeyCyanStoredImageMediaTransfer : IHeyCyanMediaTransfer
{
    string FallbackFileName { get; }
}
