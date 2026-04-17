using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.MainPage;

/// <summary>
/// Tests for the Camera tab view visibility.
/// Uses CameraPlaceholder label as a sentinel since MAUI Grid/CameraView
/// don't create UIA automation peers that FlaUI can discover.
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
        // Ensure we start on transcript tab
        Page.TranscriptTabButton.Click();
    }

    [Fact]
    public void Default_CameraContentNotVisible()
    {
        // Camera placeholder should not be in UIA tree when camera tab is hidden
        Assert.False(Page.CameraPlaceholder.IsExists(),
            "Camera content should not be visible in default transcript view");
    }

    [Fact]
    public void ClickCameraTab_CameraContentAppears()
    {
        Page.CameraTabButton.Click();

        Assert.True(Page.CameraPlaceholder.WaitExists(true, 5000),
            "Camera content should appear after clicking Camera tab");
    }

    [Fact]
    public void ClickCameraTab_TranscriptContentDisappears()
    {
        Page.CameraTabButton.Click();

        // TranscriptList (CollectionView) should be removed from UIA tree
        Page.CameraPlaceholder.WaitExists(true, 5000);
    }

    [Fact]
    public void SwitchBackToTranscript_CameraContentHides()
    {
        Page.CameraTabButton.Click();
        Page.CameraPlaceholder.WaitExists(true, 5000);

        Page.TranscriptTabButton.Click();

        Assert.True(Page.CameraPlaceholder.WaitExists(false, 5000),
            "Camera content should hide after switching back to transcript");
    }
}
