using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class DeviceSettingsTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.DeviceSettingsPage Page => _fixture.DeviceSettingsPage;

    public DeviceSettingsTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.DeviceSettingsCard.Click(),
            _fixture.DeviceSettingsPage);
    }

    [Fact]
    public void ConnectDeviceButton_Exists()
    {
        Page.ConnectDeviceButton.AssertExists();
    }

    [Fact]
    public void ConnectedDevicesList_Exists()
    {
        Page.ConnectedDevicesList.AssertExists();
    }

    [Fact]
    public void ConnectedDeviceCardTitle_Exists()
    {
        Page.ConnectedDeviceCardTitle.AssertExists();
    }

    [Fact]
    public void SourceProfilePicker_Exists()
    {
        Page.SourceProfilePicker.AssertExists();
    }

    [Fact]
    public void CameraSourcePicker_Exists()
    {
        Page.CameraSourcePicker.AssertExists();
    }

    [Fact]
    public void AudioInputPicker_Exists()
    {
        Page.AudioInputPicker.AssertExists();
    }

    [Fact]
    public void AudioOutputPicker_Exists()
    {
        Page.AudioOutputPicker.AssertExists();
    }
}
