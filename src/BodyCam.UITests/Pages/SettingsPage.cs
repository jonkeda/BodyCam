namespace BodyCam.UITests.Pages;

public class SettingsPage : PageObjectBase<SettingsPage>
{
    public SettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "SettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => ConnectionSettingsCard.WaitExists(true, timeoutMs);

    // Shell navigation
    public Button<SettingsPage> NavIcon => Button("NavIcon");

    // Category cards (Frame with TapGestureRecognizer — use Button wrapper for Click support)
    public Button<SettingsPage> ConnectionSettingsCard => Button("ConnectionSettingsCard");
    public Button<SettingsPage> VoiceSettingsCard => Button("VoiceSettingsCard");
    public Button<SettingsPage> DeviceSettingsCard => Button("DeviceSettingsCard");
    public Button<SettingsPage> AdvancedSettingsCard => Button("AdvancedSettingsCard");
}
