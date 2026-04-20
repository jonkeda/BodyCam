using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.MainPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "MainPage")]
public class DebugOverlayTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.MainPage Page => _fixture.MainPage;

    public DebugOverlayTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToHome();
    }

    [Fact]
    public void ClearButton_Click_DoesNotThrow()
    {
        Page.ClearButton.Click();
    }
}
