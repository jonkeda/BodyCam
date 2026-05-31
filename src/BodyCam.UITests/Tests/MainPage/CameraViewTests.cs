using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.MainPage;

/// <summary>
/// Tests for the inline camera preview visibility.
/// </summary>
[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "MainPage")]
public class CameraViewTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.MainPage Page => _fixture.MainPage;

    public CameraViewTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToHome();
        Page.OffButton.Click();
        Page.EnsureActionsExpanded();
    }

    [Fact]
    public void Default_CameraPreviewNotVisible()
    {
        Assert.False(Page.CameraPreviewPanel.IsExists(),
            "Camera preview should not be visible until a camera action starts.");
    }

    [Fact]
    public void SleepButton_HidesCameraPreview()
    {
        Page.LookButton.Click();
        Page.CameraPreviewPanel.WaitExists(true, 5000);

        Page.OffButton.Click();

        Assert.True(Page.CameraPreviewPanel.WaitExists(false, 5000),
            "Camera preview should hide after switching to Sleep.");
    }
}
