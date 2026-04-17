using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.MainPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "MainPage")]
public class TabSwitchingTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.MainPage Page => _fixture.MainPage;

    public TabSwitchingTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToHome();
    }

    [Fact]
    public void TranscriptTabButton_Exists()
    {
        Page.TranscriptTabButton.AssertExists();
    }

    [Fact]
    public void CameraTabButton_Exists()
    {
        Page.CameraTabButton.AssertExists();
    }

    [Fact]
    public void CameraTabButton_Click_DoesNotThrow()
    {
        Page.CameraTabButton.Click();
        // Verify we can switch back
        Page.TranscriptTabButton.Click();
    }
}
