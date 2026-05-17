using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// Contract conformance: verifies that platform-specific session implementations
/// (AndroidHeyCyanGlassesSession, IosHeyCyanGlassesSession) fully implement
/// IHeyCyanGlassesSession. Catches missing interface members at compile time
/// on every platform.
/// </summary>
public sealed class HeyCyanSessionContractTests
{
    [Fact]
    public void Platform_session_implements_full_contract()
    {
#if ANDROID
        typeof(IHeyCyanGlassesSession).IsAssignableFrom(typeof(AndroidHeyCyanGlassesSession))
            .Should().BeTrue();
#elif IOS
        typeof(IHeyCyanGlassesSession).IsAssignableFrom(typeof(BodyCam.Platforms.iOS.HeyCyan.IosHeyCyanGlassesSession))
            .Should().BeTrue();
#else
        // No HeyCyan session on Windows/other platforms - test passes trivially
        true.Should().BeTrue();
#endif
    }
}
