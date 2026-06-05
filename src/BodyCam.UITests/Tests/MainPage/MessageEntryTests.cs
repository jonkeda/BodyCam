using BodyCam.UITestKit.Pages;

namespace BodyCam.UITests.Tests.MainPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "MainPage")]
public class MessageEntryTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.MainPage Page => _fixture.MainPage;

    public MessageEntryTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToHome();
    }

    [Fact]
    public void MessageEntry_Exists()
    {
        Page.MessageEntry.AssertExists();
    }

    [Fact]
    public void SendMessageButton_Exists()
    {
        Page.SendMessageButton.AssertExists();
    }

    [Fact]
    public void SendMessageButton_IsClickable()
    {
        Page.SendMessageButton.AssertVisible(true);
        Page.SendMessageButton.AssertEnabled(true);
    }
}
