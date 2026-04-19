using BodyCam.UITests.Pages;

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
    }

    [Fact]
    public void LookButton_Exists()
    {
        Page.LookButton.AssertExists();
    }

    [Fact]
    public void ReadButton_Exists()
    {
        Page.ReadButton.AssertExists();
    }

    [Fact]
    public void FindButton_Exists()
    {
        Page.FindButton.AssertExists();
    }

    [Fact]
    public void AskButton_Exists()
    {
        Page.AskButton.AssertExists();
    }

    [Fact]
    public void PhotoButton_Exists()
    {
        Page.PhotoButton.AssertExists();
    }

    [Fact]
    public void AskButton_IsAlwaysEnabled()
    {
        // Ask button has no CanAct binding, should always be enabled
        Page.AskButton.AssertEnabled(true);
    }

    [Fact]
    public void LookButton_Click_DoesNotThrow()
    {
        Page.LookButton.Click();
    }

    [Fact]
    public void ReadButton_Click_DoesNotThrow()
    {
        Page.ReadButton.Click();
    }

    [Fact]
    public void PhotoButton_Click_DoesNotThrow()
    {
        Page.PhotoButton.Click();
    }
}
