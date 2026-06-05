namespace BodyCam.UITestKit.Pages;

public class UsbCameraSettingsPage : PageObjectBase<UsbCameraSettingsPage>
{
    public UsbCameraSettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "UsbCameraSettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => DeviceMatchEntry.IsExists();

    public Entry<UsbCameraSettingsPage> DeviceMatchEntry => Entry("UsbCameraDeviceMatchEntry");

    public Button<UsbCameraSettingsPage> TestCaptureButton => Button("UsbCameraTestCaptureButton");

    public Button<UsbCameraSettingsPage> SaveButton => Button("UsbCameraSaveButton");

    public Label<UsbCameraSettingsPage> StatusLabel => Label("UsbCameraStatusLabel");
}
