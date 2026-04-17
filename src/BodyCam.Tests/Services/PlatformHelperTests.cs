using BodyCam.Services;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class PlatformHelperTests
{
    [Fact]
    public void GetKeywordPath_FormatsCorrectly()
    {
        var path = PlatformHelper.GetKeywordPath("hey-bodycam");
        // In test context (not Android/iOS), should use "windows"
        path.Should().Be("wakewords/hey-bodycam_en_windows.ppn");
    }

    [Fact]
    public void GetPlatformSuffix_ReturnsNonEmpty()
    {
        var suffix = PlatformHelper.GetPlatformSuffix();
        suffix.Should().NotBeNullOrEmpty();
    }
}
