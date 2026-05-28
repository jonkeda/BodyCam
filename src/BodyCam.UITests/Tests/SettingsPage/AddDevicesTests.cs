using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class AddDevicesTests
{
    private readonly BodyCamFixture _fixture;

    public AddDevicesTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.DeviceSettingsCard.Click(),
            _fixture.DeviceSettingsPage);
        _fixture.DeviceSettingsPage.ConnectDeviceButton.Click();
        _fixture.AddDevicesPage.WaitReady(10000);
    }

    [Fact]
    public void ConnectDeviceButton_Click_OpensAddDevicesPage()
    {
        Assert.True(_fixture.AddDevicesPage.IsLoaded());
    }

    [Fact]
    public void AddDevicesList_Exists()
    {
        _fixture.AddDevicesPage.AddDevicesList.AssertExists();
    }

    [Fact]
    public void AddCyanGlassesButton_Exists()
    {
        _fixture.AddDevicesPage.AddCyanGlassesButton.AssertExists();
    }

    [Fact]
    public void AddA9CameraButton_Exists()
    {
        _fixture.AddDevicesPage.AddA9CameraButton.AssertExists();
    }
}
