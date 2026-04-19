namespace BodyCam.UITests.Pages;

public class ConnectionSettingsPage : PageObjectBase<ConnectionSettingsPage>
{
    public ConnectionSettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "ConnectionSettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => TestConnectionButton.IsExists();

    // Test Connection
    public Button<ConnectionSettingsPage> TestConnectionButton => Button("TestConnectionButton");
    public Label<ConnectionSettingsPage> ConnectionStatusLabel => Label("ConnectionStatusLabel");

    // Provider selection
    public RadioButton<ConnectionSettingsPage> ProviderOpenAiRadio => RadioButton("ProviderOpenAiRadio");
    public RadioButton<ConnectionSettingsPage> ProviderAzureRadio => RadioButton("ProviderAzureRadio");

    // API Key
    public Entry<ConnectionSettingsPage> ApiKeyDisplay => Entry("ApiKeyDisplay");
    public Button<ConnectionSettingsPage> ToggleKeyVisibilityButton => Button("ToggleKeyVisibilityButton");
    public Button<ConnectionSettingsPage> ChangeApiKeyButton => Button("ChangeApiKeyButton");
    public Button<ConnectionSettingsPage> ClearApiKeyButton => Button("ClearApiKeyButton");

    // Model pickers (OpenAI)
    public Picker<ConnectionSettingsPage> VoiceModelPicker => Picker("VoiceModelPicker");
    public Picker<ConnectionSettingsPage> ChatModelPicker => Picker("ChatModelPicker");
    public Picker<ConnectionSettingsPage> VisionModelPicker => Picker("VisionModelPicker");
    public Picker<ConnectionSettingsPage> TranscriptionModelPicker => Picker("TranscriptionModelPicker");

    // Status labels
    public Label<ConnectionSettingsPage> RealtimeStatusLabel => Label("RealtimeStatusLabel");
    public Label<ConnectionSettingsPage> ChatStatusLabel => Label("ChatStatusLabel");
    public Label<ConnectionSettingsPage> VisionStatusLabel => Label("VisionStatusLabel");
    public Label<ConnectionSettingsPage> TranscriptionStatusLabel => Label("TranscriptionStatusLabel");

    // Azure settings
    public Entry<ConnectionSettingsPage> AzureEndpointEntry => Entry("AzureEndpointEntry");
    public Entry<ConnectionSettingsPage> AzureApiVersionEntry => Entry("AzureApiVersionEntry");
    public Entry<ConnectionSettingsPage> AzureRealtimeDeploymentEntry => Entry("AzureRealtimeDeploymentEntry");
    public Entry<ConnectionSettingsPage> AzureChatDeploymentEntry => Entry("AzureChatDeploymentEntry");
    public Entry<ConnectionSettingsPage> AzureVisionDeploymentEntry => Entry("AzureVisionDeploymentEntry");
}
