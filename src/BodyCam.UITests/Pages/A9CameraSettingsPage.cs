namespace BodyCam.UITests.Pages;

public class A9CameraSettingsPage : PageObjectBase<A9CameraSettingsPage>
{
    public A9CameraSettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "A9CameraSettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => IpEntry.IsExists();

    public Entry<A9CameraSettingsPage> IpEntry => Entry("A9CameraIpEntry");

    public Entry<A9CameraSettingsPage> UidEntry => Entry("A9CameraUidEntry");

    public Entry<A9CameraSettingsPage> UsernameEntry => Entry("A9CameraUsernameEntry");

    public Entry<A9CameraSettingsPage> PasswordEntry => Entry("A9CameraPasswordEntry");

    public Button<A9CameraSettingsPage> TestConnectionButton => Button("A9CameraTestConnectionButton");

    public Button<A9CameraSettingsPage> SaveButton => Button("A9CameraSaveButton");

    public Label<A9CameraSettingsPage> StatusLabel => Label("A9CameraStatusLabel");
}
