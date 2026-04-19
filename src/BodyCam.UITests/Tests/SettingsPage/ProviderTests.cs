using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class ProviderTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.ConnectionSettingsPage Page => _fixture.ConnectionSettingsPage;

    public ProviderTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.ConnectionSettingsCard.Click(),
            _fixture.ConnectionSettingsPage);
    }

    [Fact]
    public void ProviderOpenAiRadio_Exists()
    {
        Page.ProviderOpenAiRadio.AssertExists();
    }

    [Fact]
    public void ProviderAzureRadio_Exists()
    {
        Page.ProviderAzureRadio.AssertExists();
    }

    [Fact]
    public void SelectAzure_ShowsAzureFields()
    {
        Page.ProviderAzureRadio.Select();
        Page.AzureEndpointEntry.AssertExists();
        Page.AzureApiVersionEntry.AssertExists();
        // Restore to OpenAI
        Page.ProviderOpenAiRadio.Select();
    }

    [Fact(Skip = "FlaUI cannot reliably Select() MAUI RadioButton on WinUI3")]
    public void SelectOpenAi_ShowsModelPickers()
    {
        Page.ProviderOpenAiRadio.Select();
        Page.VoiceModelPicker.AssertExists();
        Page.ChatModelPicker.AssertExists();
        Page.VisionModelPicker.AssertExists();
        Page.TranscriptionModelPicker.AssertExists();
    }
}
