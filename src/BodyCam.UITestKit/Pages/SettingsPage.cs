namespace BodyCam.UITestKit.Pages;

public class SettingsPage : PageObjectBase<SettingsPage>
{
    public SettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "SettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => ConnectionSettingsCard.WaitExists(true, timeoutMs);

    // Category cards (Frame with TapGestureRecognizer — use Button wrapper for Click support)
    [UatName("Connection Settings")]
    public Button<SettingsPage> ConnectionSettingsCard => Button("ConnectionSettingsCard");

    [UatName("Voice Settings")]
    public Button<SettingsPage> VoiceSettingsCard => Button("VoiceSettingsCard");

    [UatName("Device Settings")]
    public Button<SettingsPage> DeviceSettingsCard => Button("DeviceSettingsCard");

    [UatName("Commands Settings")]
    public Button<SettingsPage> CommandsSettingsCard => Button("CommandsSettingsCard");

    [UatName("Advanced Settings")]
    public Button<SettingsPage> AdvancedSettingsCard => Button("AdvancedSettingsCard");
}
