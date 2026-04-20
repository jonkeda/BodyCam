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
    public void ClearButton_Exists()
    {
        Page.ClearButton.AssertExists();
    }
}
