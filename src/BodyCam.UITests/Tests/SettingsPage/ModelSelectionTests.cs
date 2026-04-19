using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class ModelSelectionTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.ConnectionSettingsPage Page => _fixture.ConnectionSettingsPage;

    public ModelSelectionTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettings();
        _fixture.SettingsPage.ConnectionSettingsCard.Click();
        _fixture.ConnectionSettingsPage.WaitReady(10000);
        // Ensure OpenAI provider is selected
        Page.ProviderOpenAiRadio.Select();
    }

    [Fact]
    public void VoiceModelPicker_Exists()
    {
        Page.VoiceModelPicker.AssertExists();
    }

    [Fact]
    public void ChatModelPicker_Exists()
    {
        Page.ChatModelPicker.AssertExists();
    }

    [Fact]
    public void VisionModelPicker_Exists()
    {
        Page.VisionModelPicker.AssertExists();
    }

    [Fact]
    public void TranscriptionModelPicker_Exists()
    {
        Page.TranscriptionModelPicker.AssertExists();
    }

    [Fact]
    public void VoiceModelPicker_HasSelectedValue()
    {
        var selected = Page.VoiceModelPicker.GetSelectedText();
        Assert.False(string.IsNullOrEmpty(selected));
    }
}
