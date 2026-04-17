using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class ProviderTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.SettingsPage Page => _fixture.SettingsPage;

    public ProviderTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettings();
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

    [Fact]
    public void SelectOpenAi_ShowsModelPickers()
    {
        Page.ProviderOpenAiRadio.Select();
        Page.VoiceModelPicker.AssertExists();
        Page.ChatModelPicker.AssertExists();
        Page.VisionModelPicker.AssertExists();
        Page.TranscriptionModelPicker.AssertExists();
    }
}
