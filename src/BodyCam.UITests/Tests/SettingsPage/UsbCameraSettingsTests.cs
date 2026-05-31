using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class UsbCameraSettingsTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.UsbCameraSettingsPage Page => _fixture.UsbCameraSettingsPage;

    public UsbCameraSettingsTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.DeviceSettingsCard.Click(),
            _fixture.DeviceSettingsPage);
        _fixture.DeviceSettingsPage.ConnectDeviceButton.Click();
        _fixture.AddDevicesPage.WaitReady(10000);
        _fixture.AddDevicesPage.AddUsbCameraButton.Click();
        Page.WaitReady(10000);
    }

    [Fact]
    public void AddUsbCameraButton_Click_OpensUsbCameraSettingsPage()
    {
        Assert.True(Page.IsLoaded());
    }

    [Fact]
    public void UsbCameraFields_Exist()
    {
        Page.DeviceMatchEntry.AssertExists();
    }

    [Fact]
    public void UsbCameraActions_Exist()
    {
        Page.TestCaptureButton.AssertExists();
        Page.SaveButton.AssertExists();
        Page.StatusLabel.AssertExists();
    }

    [Fact]
    public void DeviceMatchEntry_EnterVidPid_SetsValue()
    {
        Page.DeviceMatchEntry.Clear();
        Page.DeviceMatchEntry.Enter("VID_349C&PID_0411");

        Assert.Equal("VID_349C&PID_0411", Page.DeviceMatchEntry.GetText());
    }
}
