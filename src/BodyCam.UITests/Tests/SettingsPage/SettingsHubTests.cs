using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class SettingsHubTests
{
    private readonly BodyCamFixture _fixture;

    public SettingsHubTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettings();
    }

    [Fact]
    public void ConnectionSettingsCard_Exists()
    {
        _fixture.SettingsPage.ConnectionSettingsCard.AssertExists();
    }

    [Fact]
    public void VoiceSettingsCard_Exists()
    {
        _fixture.SettingsPage.VoiceSettingsCard.AssertExists();
    }

    [Fact]
    public void DeviceSettingsCard_Exists()
    {
        _fixture.SettingsPage.DeviceSettingsCard.AssertExists();
    }

    [Fact]
    public void AdvancedSettingsCard_Exists()
    {
        _fixture.SettingsPage.AdvancedSettingsCard.AssertExists();
    }

    [Fact]
    public void ConnectionSettingsCard_Click_OpensConnectionPage()
    {
        _fixture.SettingsPage.ConnectionSettingsCard.Click();
        Assert.True(_fixture.ConnectionSettingsPage.IsLoaded(10000));
    }

    [Fact]
    public void VoiceSettingsCard_Click_OpensVoicePage()
    {
        _fixture.SettingsPage.VoiceSettingsCard.Click();
        Assert.True(_fixture.VoiceSettingsPage.IsLoaded(10000));
    }

    [Fact]
    public void DeviceSettingsCard_Click_OpensDevicePage()
    {
        _fixture.SettingsPage.DeviceSettingsCard.Click();
        Assert.True(_fixture.DeviceSettingsPage.IsLoaded(10000));
    }

    [Fact]
    public void AdvancedSettingsCard_Click_OpensAdvancedPage()
    {
        _fixture.SettingsPage.AdvancedSettingsCard.Click();
        Assert.True(_fixture.AdvancedSettingsPage.IsLoaded(10000));
    }
}
