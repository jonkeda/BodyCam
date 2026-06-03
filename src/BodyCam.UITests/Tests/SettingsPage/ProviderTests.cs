using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class ProviderTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.LlmProvidersSettingsPage Page => _fixture.LlmProvidersSettingsPage;
    private Pages.LlmProviderSettingsPage DetailPage => _fixture.LlmProviderSettingsPage;

    public ProviderTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToLlmProviders();
    }

    [Fact]
    public void OpenAiProviderEditButton_Exists()
    {
        Page.EditOpenAiProviderButton.AssertExists();
    }

    [Fact]
    public void AzureProviderEditButton_Exists()
    {
        Page.EditAzureProviderButton.AssertExists();
    }

    [Fact]
    public void AzureProviderDetail_ShowsAzureFields()
    {
        Page.EditAzureProviderButton.Click();
        DetailPage.WaitReady(10000);

        DetailPage.AzureEndpointEntry.AssertExists();
        DetailPage.AzureApiVersionEntry.AssertExists();
    }

    [Fact]
    public void OpenAiProviderDetail_ShowsModelPickers()
    {
        Page.EditOpenAiProviderButton.Click();
        DetailPage.WaitReady(10000);

        DetailPage.RealtimeModelPicker.AssertExists();
        DetailPage.ChatModelPicker.AssertExists();
        DetailPage.VisionModelPicker.AssertExists();
        DetailPage.TranscriptionModelPicker.AssertExists();
    }
}
