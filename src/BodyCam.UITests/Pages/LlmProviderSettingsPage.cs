namespace BodyCam.UITests.Pages;

public class LlmProviderSettingsPage : PageObjectBase<LlmProviderSettingsPage>
{
    public LlmProviderSettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "LlmProviderSettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => ApiKeyDisplay.WaitExists(true, timeoutMs);

    public Button<LlmProviderSettingsPage> SetActiveProviderButton => Button("SetActiveProviderButton");

    public Entry<LlmProviderSettingsPage> ApiKeyDisplay => Entry("LlmProviderApiKeyDisplay");

    public Button<LlmProviderSettingsPage> ToggleKeyVisibilityButton => Button("LlmProviderToggleKeyButton");

    public Button<LlmProviderSettingsPage> ChangeApiKeyButton => Button("LlmProviderChangeKeyButton");

    public Button<LlmProviderSettingsPage> ClearApiKeyButton => Button("LlmProviderClearKeyButton");

    public Picker<LlmProviderSettingsPage> RealtimeModelPicker => Picker("LlmRealtimeModelPicker");

    public Picker<LlmProviderSettingsPage> ChatModelPicker => Picker("LlmChatModelPicker");

    public Picker<LlmProviderSettingsPage> VisionModelPicker => Picker("LlmVisionModelPicker");

    public Picker<LlmProviderSettingsPage> TranscriptionModelPicker => Picker("LlmTranscriptionModelPicker");

    public Entry<LlmProviderSettingsPage> AzureEndpointEntry => Entry("LlmAzureEndpointEntry");

    public Entry<LlmProviderSettingsPage> AzureApiVersionEntry => Entry("LlmAzureApiVersionEntry");

    public Entry<LlmProviderSettingsPage> AzureRealtimeDeploymentEntry => Entry("LlmAzureRealtimeDeploymentEntry");

    public Entry<LlmProviderSettingsPage> AzureChatDeploymentEntry => Entry("LlmAzureChatDeploymentEntry");

    public Entry<LlmProviderSettingsPage> AzureVisionDeploymentEntry => Entry("LlmAzureVisionDeploymentEntry");

    public Button<LlmProviderSettingsPage> TestConnectionButton => Button("LlmProviderTestButton");

    public Label<LlmProviderSettingsPage> DiagnosticsLabel => Label("LlmProviderDiagnosticsLabel");
}
