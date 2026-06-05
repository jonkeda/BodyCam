using BodyCam.UITestKit.Pages;

namespace BodyCam.UITests.Tests.MainPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "MainPage")]
public class QuickActionTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.MainPage Page => _fixture.MainPage;

    public QuickActionTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToHome();
        Page.EnsureActionsExpanded();
    }

    [Fact]
    public void LookButton_Exists()
    {
        Page.LookButton.AssertExists();
    }

    [Fact]
    public void LookDetailButton_Exists()
    {
        Page.LookDetailButton.AssertExists();
    }

    [Fact]
    public void LookSummaryButton_Exists()
    {
        Page.LookSummaryButton.AssertExists();
    }

    [Fact]
    public void ReadButton_Exists()
    {
        Page.ReadButton.AssertExists();
    }

    [Fact]
    public void ScanButton_Exists()
    {
        Page.ScanButton.AssertExists();
    }

    [Fact]
    public void ActionsDrawerButton_Exists()
    {
        Page.ActionsDrawerButton.AssertExists();
    }

    [Fact]
    public void ActionsDrawerButton_CollapsesActions()
    {
        Page.ActionsDrawerButton.Click();

        Assert.True(Page.LookButton.WaitExists(false, 5000),
            "Look should be hidden when the actions drawer is collapsed.");
    }

    [Fact]
    public void LookButton_IsEnabled()
    {
        Page.LookButton.AssertEnabled(true);
    }

    [Fact]
    public void LookDetailButton_IsEnabled()
    {
        Page.LookDetailButton.AssertEnabled(true);
    }

    [Fact]
    public void LookSummaryButton_IsEnabled()
    {
        Page.LookSummaryButton.AssertEnabled(true);
    }

    [Fact]
    public void ReadButton_IsEnabled()
    {
        Page.ReadButton.AssertEnabled(true);
    }

    [Fact]
    public void ScanButton_IsEnabled()
    {
        Page.ScanButton.AssertEnabled(true);
    }
}
