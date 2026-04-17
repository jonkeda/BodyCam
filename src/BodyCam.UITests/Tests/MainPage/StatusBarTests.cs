using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.MainPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "MainPage")]
public class StatusBarTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.MainPage Page => _fixture.MainPage;

    public StatusBarTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToHome();
    }

    [Fact]
    public void SleepButton_Exists()
    {
        Page.SleepButton.AssertExists();
    }

    [Fact]
    public void ListenButton_Exists()
    {
        Page.ListenButton.AssertExists();
    }

    [Fact]
    public void ActiveButton_Exists()
    {
        Page.ActiveButton.AssertExists();
    }

    [Fact]
    public void SleepButton_IsClickable()
    {
        Page.SleepButton.AssertVisible(true);
        Page.SleepButton.AssertEnabled(true);
    }

    [Fact]
    public void ListenButton_IsClickable()
    {
        Page.ListenButton.AssertVisible(true);
        Page.ListenButton.AssertEnabled(true);
    }

    [Fact]
    public void ActiveButton_IsClickable()
    {
        Page.ActiveButton.AssertVisible(true);
        Page.ActiveButton.AssertEnabled(true);
    }

    [Fact]
    public void DebugToggleButton_Exists()
    {
        Page.DebugToggleButton.AssertExists();
    }

    [Fact]
    public void ClearButton_Exists()
    {
        Page.ClearButton.AssertExists();
    }
}
