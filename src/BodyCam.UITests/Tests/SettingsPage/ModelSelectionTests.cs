using BodyCam.UITestKit.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

/// <summary>
/// Model pickers are visible on the OpenAI provider detail page.
/// </summary>
[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class ModelSelectionTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.LlmProviderSettingsPage Page => _fixture.LlmProviderSettingsPage;

    public ModelSelectionTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToLlmProviderDetail(
            () => _fixture.LlmProvidersSettingsPage.EditOpenAiProviderButton.Click());
    }

    [Fact]
    public void VoiceModelPicker_Exists()
    {
        Page.RealtimeModelPicker.AssertExists();
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
        var selected = Page.RealtimeModelPicker.GetSelectedText();
        Assert.False(string.IsNullOrEmpty(selected));
    }
}
