namespace BodyCam.UITests.Pages;

public class SettingsPage : PageObjectBase<SettingsPage>
{
    public SettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "SettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => ProviderOpenAiRadio.IsExists();

    // Provider selection
    public RadioButton<SettingsPage> ProviderOpenAiRadio => RadioButton("ProviderOpenAiRadio");
    public RadioButton<SettingsPage> ProviderAzureRadio => RadioButton("ProviderAzureRadio");

    // Model pickers (OpenAI)
    public Picker<SettingsPage> VoiceModelPicker => Picker("VoiceModelPicker");
    public Picker<SettingsPage> ChatModelPicker => Picker("ChatModelPicker");
    public Picker<SettingsPage> VisionModelPicker => Picker("VisionModelPicker");
    public Picker<SettingsPage> TranscriptionModelPicker => Picker("TranscriptionModelPicker");

    // Status labels
    public Label<SettingsPage> RealtimeStatusLabel => Label("RealtimeStatusLabel");
    public Label<SettingsPage> ChatStatusLabel => Label("ChatStatusLabel");
    public Label<SettingsPage> VisionStatusLabel => Label("VisionStatusLabel");
    public Label<SettingsPage> TranscriptionStatusLabel => Label("TranscriptionStatusLabel");

    // Azure settings
    public Entry<SettingsPage> AzureEndpointEntry => Entry("AzureEndpointEntry");
    public Entry<SettingsPage> AzureApiVersionEntry => Entry("AzureApiVersionEntry");
    public Entry<SettingsPage> AzureRealtimeDeploymentEntry => Entry("AzureRealtimeDeploymentEntry");
    public Entry<SettingsPage> AzureChatDeploymentEntry => Entry("AzureChatDeploymentEntry");
    public Entry<SettingsPage> AzureVisionDeploymentEntry => Entry("AzureVisionDeploymentEntry");

    // Voice settings
    public Picker<SettingsPage> VoicePicker => Picker("VoicePicker");
    public Picker<SettingsPage> TurnDetectionPicker => Picker("TurnDetectionPicker");
    public Picker<SettingsPage> NoiseReductionPicker => Picker("NoiseReductionPicker");

    // System instructions
    public Editor<SettingsPage> SystemInstructionsEditor => Editor("SystemInstructionsEditor");

    // API key
    public Entry<SettingsPage> ApiKeyDisplay => Entry("ApiKeyDisplay");
    public Button<SettingsPage> ToggleKeyVisibilityButton => Button("ToggleKeyVisibilityButton");
    public Button<SettingsPage> ChangeApiKeyButton => Button("ChangeApiKeyButton");
    public Button<SettingsPage> ClearApiKeyButton => Button("ClearApiKeyButton");

    // Connection test
    public Button<SettingsPage> TestConnectionButton => Button("TestConnectionButton");
    public Label<SettingsPage> ConnectionStatusLabel => Label("ConnectionStatusLabel");

    // Debug switches
    public Switch<SettingsPage> DebugModeSwitch => Switch("DebugModeSwitch");
    public Switch<SettingsPage> ShowTokenCountsSwitch => Switch("ShowTokenCountsSwitch");
    public Switch<SettingsPage> ShowCostEstimateSwitch => Switch("ShowCostEstimateSwitch");
}
