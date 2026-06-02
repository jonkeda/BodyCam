namespace BodyCam.Tests.Services.Glasses.HeyCyan.RealHardware;

/// <summary>
/// Opt-in fact for tests that require physical HeyCyan glasses and an Android
/// phone with the BodyCam app installed.
/// </summary>
public sealed class HeyCyanRealHardwareFactAttribute : FactAttribute
{
    public HeyCyanRealHardwareFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_WIFI"),
                "1",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set BODYCAM_REAL_HEYCYAN_WIFI=1 to run real HeyCyan WiFi hardware tests.";
        }
    }
}
