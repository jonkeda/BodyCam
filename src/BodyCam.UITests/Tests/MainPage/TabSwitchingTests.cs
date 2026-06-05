using BodyCam.UITestKit.Pages;

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
    public void TranscriptTabButton_IsRemoved()
    {
        Assert.False(Page.TranscriptTabButton.IsExists(),
            "The m42 first page should not show a Transcript tab.");
    }

    [Fact]
    public void CameraTabButton_IsRemoved()
    {
        Assert.False(Page.CameraTabButton.IsExists(),
            "The m42 first page should not show a Camera tab.");
    }

    [Fact]
    public void TranscriptList_Exists()
    {
        Page.TranscriptList.AssertExists();
    }
}
