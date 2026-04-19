namespace BodyCam.UITests.Pages;

public class SetupPage : PageObjectBase<SetupPage>
{
    public SetupPage(IMauiTestContext context) : base(context) { }

    public override string Name => "SetupPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => SetupNextButton.IsExists();

    // Progress
    public Label<SetupPage> SetupProgressLabel => Label("SetupProgressLabel");

    // Step content
    public Label<SetupPage> SetupStepIcon => Label("SetupStepIcon");
    public Label<SetupPage> SetupStepTitle => Label("SetupStepTitle");
    public Label<SetupPage> SetupStepDescription => Label("SetupStepDescription");
    public Label<SetupPage> SetupStatusLabel => Label("SetupStatusLabel");

    // Permission step
    public Button<SetupPage> GrantPermissionButton => Button("GrantPermissionButton");
    public Button<SetupPage> OpenSettingsButton => Button("OpenSettingsButton");

    // API Key step
    public RadioButton<SetupPage> SetupOpenAiRadio => RadioButton("SetupOpenAiRadio");
    public RadioButton<SetupPage> SetupAzureRadio => RadioButton("SetupAzureRadio");
    public Entry<SetupPage> SetupApiKeyEntry => Entry("SetupApiKeyEntry");
    public Button<SetupPage> ValidateKeyButton => Button("ValidateKeyButton");
    public Label<SetupPage> SetupStatusMessage => Label("SetupStatusMessage");

    // Navigation
    public Button<SetupPage> SetupSkipButton => Button("SetupSkipButton");
    public Button<SetupPage> SetupNextButton => Button("SetupNextButton");
}
