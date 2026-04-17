using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class VoiceSettingsTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.SettingsPage Page => _fixture.SettingsPage;

    public VoiceSettingsTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettings();
    }

    [Fact]
    public void VoicePicker_Exists()
    {
        Page.VoicePicker.AssertExists();
    }

    [Fact]
    public void TurnDetectionPicker_Exists()
    {
        Page.TurnDetectionPicker.AssertExists();
    }

    [Fact]
    public void NoiseReductionPicker_Exists()
    {
        Page.NoiseReductionPicker.AssertExists();
    }

    [Fact]
    public void VoicePicker_HasSelectedValue()
    {
        var selected = Page.VoicePicker.GetSelectedText();
        Assert.False(string.IsNullOrEmpty(selected));
    }
}
