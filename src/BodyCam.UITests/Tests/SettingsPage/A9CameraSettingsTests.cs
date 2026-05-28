using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class A9CameraSettingsTests
{
    private readonly BodyCamFixture _fixture;

    public A9CameraSettingsTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.DeviceSettingsCard.Click(),
            _fixture.DeviceSettingsPage);
        _fixture.DeviceSettingsPage.ConnectDeviceButton.Click();
        _fixture.AddDevicesPage.WaitReady(10000);
        _fixture.AddDevicesPage.AddA9CameraButton.Click();
        _fixture.A9CameraSettingsPage.WaitReady(10000);
    }

    [Fact]
    public void AddA9CameraButton_Click_OpensA9CameraSettingsPage()
    {
        Assert.True(_fixture.A9CameraSettingsPage.IsLoaded());
    }

    [Fact]
    public void A9CameraFields_Exist()
    {
        _fixture.A9CameraSettingsPage.IpEntry.AssertExists();
        _fixture.A9CameraSettingsPage.UidEntry.AssertExists();
        _fixture.A9CameraSettingsPage.UsernameEntry.AssertExists();
        _fixture.A9CameraSettingsPage.PasswordEntry.AssertExists();
    }

    [Fact]
    public void A9CameraActions_Exist()
    {
        _fixture.A9CameraSettingsPage.TestConnectionButton.AssertExists();
        _fixture.A9CameraSettingsPage.SaveButton.AssertExists();
        _fixture.A9CameraSettingsPage.StatusLabel.AssertExists();
    }
}
