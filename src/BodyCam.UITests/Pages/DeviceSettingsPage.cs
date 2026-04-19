namespace BodyCam.UITests.Pages;

public class DeviceSettingsPage : PageObjectBase<DeviceSettingsPage>
{
    public DeviceSettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "DeviceSettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => CameraSourcePicker.IsExists();

    // Camera
    public Picker<DeviceSettingsPage> CameraSourcePicker => Picker("CameraSourcePicker");

    // Audio Input
    public Picker<DeviceSettingsPage> AudioInputPicker => Picker("AudioInputPicker");

    // Audio Output
    public Picker<DeviceSettingsPage> AudioOutputPicker => Picker("AudioOutputPicker");
}
