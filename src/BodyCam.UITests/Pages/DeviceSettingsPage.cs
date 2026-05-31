namespace BodyCam.UITests.Pages;

public class DeviceSettingsPage : PageObjectBase<DeviceSettingsPage>
{
    public DeviceSettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "DeviceSettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => ConnectedDevicesList.IsExists() && (CameraSourcePicker.IsExists() || SourceProfilePicker.IsExists());

    // Connected Devices
    public Button<DeviceSettingsPage> ConnectDeviceButton => Button("ConnectDeviceButton");

    public Brinell.Maui.Controls.Collection.CollectionView<DeviceSettingsPage> ConnectedDevicesList
        => CollectionView("ConnectedDevicesList");

    public Label<DeviceSettingsPage> ConnectedDeviceCardTitle => Label("ConnectedDeviceCardTitle");

    // Source Profile
    public Picker<DeviceSettingsPage> SourceProfilePicker => Picker("SourceProfilePicker");

    // Camera
    public Picker<DeviceSettingsPage> CameraSourcePicker => Picker("CameraSourcePicker");

    public Button<DeviceSettingsPage> TakePictureButton => Button("TakePictureButton");

    public Label<DeviceSettingsPage> TakePictureStatusLabel => Label("TakePictureStatusLabel");

    public Image<DeviceSettingsPage> LastPictureImage => Image("LastPictureImage");

    // Audio Input
    public Picker<DeviceSettingsPage> AudioInputPicker => Picker("AudioInputPicker");

    // Audio Output
    public Picker<DeviceSettingsPage> AudioOutputPicker => Picker("AudioOutputPicker");
}
