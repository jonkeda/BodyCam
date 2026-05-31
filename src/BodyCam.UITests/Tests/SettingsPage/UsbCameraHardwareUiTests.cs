using System;
using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "RealHardware")]
[Trait("Page", "SettingsPage")]
public class UsbCameraHardwareUiTests
{
    private const string DefaultDeviceMatch = "VID_349C&PID_0411";

    private readonly BodyCamFixture _fixture;
    private Pages.UsbCameraSettingsPage Page => _fixture.UsbCameraSettingsPage;

    public UsbCameraHardwareUiTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public void TestCaptureButton_WithPluggedInUsbCamera_ReportsSuccess()
    {
        Skip.IfNot(
            Environment.GetEnvironmentVariable("BODYCAM_REAL_USB_CAMERA_UI") == "1",
            "BODYCAM_REAL_USB_CAMERA_UI not set to 1");

        NavigateToUsbCameraSettings();

        Page.DeviceMatchEntry.Clear();
        Page.DeviceMatchEntry.Enter(
            Environment.GetEnvironmentVariable("BODYCAM_USB_CAMERA_DEVICE_MATCH")
            ?? DefaultDeviceMatch);

        Page.TestCaptureButton.Click();

        Page.StatusLabel.AssertTextContains("Capture test succeeded", timeoutMs: 20000);
    }

    [SkippableFact]
    public void TakePicture_WithUsbCameraSelected_ReportsCaptured()
    {
        Skip.IfNot(
            Environment.GetEnvironmentVariable("BODYCAM_REAL_USB_CAMERA_UI") == "1",
            "BODYCAM_REAL_USB_CAMERA_UI not set to 1");

        NavigateToUsbCameraSettings();

        Page.DeviceMatchEntry.Clear();
        Page.DeviceMatchEntry.Enter(
            Environment.GetEnvironmentVariable("BODYCAM_USB_CAMERA_DEVICE_MATCH")
            ?? DefaultDeviceMatch);
        Page.SaveButton.Click();
        Page.StatusLabel.AssertTextContains("Saved", timeoutMs: 5000);

        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.DeviceSettingsCard.Click(),
            _fixture.DeviceSettingsPage);

        _fixture.DeviceSettingsPage.CameraSourcePicker.SelectByText("USB Camera", timeoutMs: 10000);
        _fixture.DeviceSettingsPage.TakePictureButton.Click();

        _fixture.DeviceSettingsPage.TakePictureStatusLabel.AssertTextContains("Captured", timeoutMs: 20000);
        _fixture.DeviceSettingsPage.LastPictureImage.AssertExists();
    }

    private void NavigateToUsbCameraSettings()
    {
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.DeviceSettingsCard.Click(),
            _fixture.DeviceSettingsPage);
        _fixture.DeviceSettingsPage.ConnectDeviceButton.Click();
        _fixture.AddDevicesPage.WaitReady(10000);
        _fixture.AddDevicesPage.AddUsbCameraButton.Click();
        Page.WaitReady(10000);
    }
}
