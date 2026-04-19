namespace BodyCam.UITests.Pages;

public class VoiceSettingsPage : PageObjectBase<VoiceSettingsPage>
{
    public VoiceSettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "VoiceSettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => VoicePicker.IsExists();

    // Voice settings
    public Picker<VoiceSettingsPage> VoicePicker => Picker("VoicePicker");
    public Picker<VoiceSettingsPage> TurnDetectionPicker => Picker("TurnDetectionPicker");
    public Picker<VoiceSettingsPage> NoiseReductionPicker => Picker("NoiseReductionPicker");

    // System instructions
    public Editor<VoiceSettingsPage> SystemInstructionsEditor => Editor("SystemInstructionsEditor");
}
