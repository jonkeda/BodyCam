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
    public void OffButton_Exists()
    {
        Page.OffButton.AssertExists();
    }

    [Fact]
    public void OnButton_Exists()
    {
        Page.OnButton.AssertExists();
    }

    [Fact]
    public void ListeningButton_Exists()
    {
        Page.ListeningButton.AssertExists();
    }

    [Fact]
    public void SettingsButton_Exists()
    {
        Page.SettingsButton.AssertExists();
    }

    [Fact]
    public void SpeakButton_Exists()
    {
        Page.SpeakButton.AssertExists();
    }

    [Fact]
    public void SilentButton_Exists()
    {
        Page.SilentButton.AssertExists();
    }

    [Fact]
    public void OffButton_IsClickable()
    {
        Page.OffButton.AssertVisible(true);
        Page.OffButton.AssertEnabled(true);
    }

    [Fact]
    public void OnButton_IsClickable()
    {
        Page.OnButton.AssertVisible(true);
        Page.OnButton.AssertEnabled(true);
    }

    [Fact]
    public void ListeningButton_IsClickable()
    {
        Page.ListeningButton.AssertVisible(true);
        Page.ListeningButton.AssertEnabled(true);
    }

    [Fact]
    public void SettingsButton_IsClickable()
    {
        Page.SettingsButton.AssertVisible(true);
        Page.SettingsButton.AssertEnabled(true);
    }

    [Fact]
    public void SpeakButton_IsClickable()
    {
        Page.SpeakButton.AssertVisible(true);
        Page.SpeakButton.AssertEnabled(true);
    }

    [Fact]
    public void SilentButton_IsClickable()
    {
        Page.SilentButton.AssertVisible(true);
        Page.SilentButton.AssertEnabled(true);
    }
}
