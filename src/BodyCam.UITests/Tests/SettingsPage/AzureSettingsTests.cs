using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class AzureSettingsTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.LlmProviderSettingsPage Page => _fixture.LlmProviderSettingsPage;

    public AzureSettingsTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToLlmProviderDetail(
            () => _fixture.LlmProvidersSettingsPage.EditAzureProviderButton.Click());
    }

    [Fact]
    public void AzureEndpointEntry_EnterUrl_SetsValue()
    {
        Page.AzureEndpointEntry.SetText("https://test.cognitiveservices.azure.com");
        Assert.Equal("https://test.cognitiveservices.azure.com", Page.AzureEndpointEntry.GetText());
    }

    [Fact]
    public void AzureApiVersionEntry_EnterVersion_SetsValue()
    {
        Page.AzureApiVersionEntry.SetText("2025-04-01-preview");
        Assert.Equal("2025-04-01-preview", Page.AzureApiVersionEntry.GetText());
    }

    [Fact]
    public void AzureRealtimeDeployment_Exists()
    {
        Page.AzureRealtimeDeploymentEntry.AssertExists();
    }

    [Fact]
    public void AzureChatDeployment_Exists()
    {
        Page.AzureChatDeploymentEntry.AssertExists();
    }

    [Fact]
    public void AzureVisionDeployment_Exists()
    {
        Page.AzureVisionDeploymentEntry.AssertExists();
    }
}
