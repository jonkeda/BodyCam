using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class AzureSettingsTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.ConnectionSettingsPage Page => _fixture.ConnectionSettingsPage;

    public AzureSettingsTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.ConnectionSettingsCard.Click(),
            _fixture.ConnectionSettingsPage);
        // Switch to Azure to make fields visible
        Page.ProviderAzureRadio.Select();
    }

    [Fact]
    public void AzureEndpointEntry_EnterUrl_SetsValue()
    {
        Page.AzureEndpointEntry.Clear();
        Page.AzureEndpointEntry.Enter("https://test.cognitiveservices.azure.com");
        Assert.Equal("https://test.cognitiveservices.azure.com", Page.AzureEndpointEntry.GetText());
    }

    [Fact]
    public void AzureApiVersionEntry_EnterVersion_SetsValue()
    {
        Page.AzureApiVersionEntry.Clear();
        Page.AzureApiVersionEntry.Enter("2025-04-01-preview");
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
