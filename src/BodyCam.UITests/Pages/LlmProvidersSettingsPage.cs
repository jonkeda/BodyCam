namespace BodyCam.UITests.Pages;

public class LlmProvidersSettingsPage : PageObjectBase<LlmProvidersSettingsPage>
{
    public LlmProvidersSettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "LlmProvidersSettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => AddLlmProviderButton.WaitExists(true, timeoutMs);

    public Button<LlmProvidersSettingsPage> AddLlmProviderButton => Button("AddLlmProviderButton");

    public Button<LlmProvidersSettingsPage> EditOpenAiProviderButton => Button("EditopenaiProviderButton");

    public Button<LlmProvidersSettingsPage> EditAzureProviderButton => Button("EditazureopenaiProviderButton");

    public Button<LlmProvidersSettingsPage> EditGrokProviderButton => Button("EditxaigrokProviderButton");
}
