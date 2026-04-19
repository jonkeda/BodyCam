using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

/// <summary>
/// Model pickers are only visible when OpenAI provider is active.
/// FlaUI cannot reliably Select() MAUI RadioButtons on WinUI3,
/// so these tests require OpenAI to be the persisted provider.
/// </summary>
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
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.ConnectionSettingsCard.Click(),
            _fixture.ConnectionSettingsPage);
    }

    [Fact(Skip = "FlaUI cannot Select() MAUI RadioButton on WinUI3 — requires OpenAI provider persisted")]
    public void VoiceModelPicker_Exists()
    {
        Page.VoiceModelPicker.AssertExists();
    }

    [Fact(Skip = "FlaUI cannot Select() MAUI RadioButton on WinUI3 — requires OpenAI provider persisted")]
    public void ChatModelPicker_Exists()
    {
        Page.ChatModelPicker.AssertExists();
    }

    [Fact(Skip = "FlaUI cannot Select() MAUI RadioButton on WinUI3 — requires OpenAI provider persisted")]
    public void VisionModelPicker_Exists()
    {
        Page.VisionModelPicker.AssertExists();
    }

    [Fact(Skip = "FlaUI cannot Select() MAUI RadioButton on WinUI3 — requires OpenAI provider persisted")]
    public void TranscriptionModelPicker_Exists()
    {
        Page.TranscriptionModelPicker.AssertExists();
    }

    [Fact(Skip = "FlaUI cannot Select() MAUI RadioButton on WinUI3 — requires OpenAI provider persisted")]
    public void VoiceModelPicker_HasSelectedValue()
    {
        var selected = Page.VoiceModelPicker.GetSelectedText();
        Assert.False(string.IsNullOrEmpty(selected));
    }
}
