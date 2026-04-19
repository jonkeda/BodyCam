namespace BodyCam.UITests.Pages;

public class AdvancedSettingsPage : PageObjectBase<AdvancedSettingsPage>
{
    public AdvancedSettingsPage(IMauiTestContext context) : base(context) { }

    public override string Name => "AdvancedSettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => DebugModeSwitch.IsExists();

    // Debug switches
    public Switch<AdvancedSettingsPage> DebugModeSwitch => Switch("DebugModeSwitch");
    public Switch<AdvancedSettingsPage> ShowTokenCountsSwitch => Switch("ShowTokenCountsSwitch");
    public Switch<AdvancedSettingsPage> ShowCostEstimateSwitch => Switch("ShowCostEstimateSwitch");

    // Diagnostics
    public Switch<AdvancedSettingsPage> SendDiagnosticDataSwitch => Switch("SendDiagnosticDataSwitch");
    public Entry<AdvancedSettingsPage> AzureMonitorConnectionStringEntry => Entry("AzureMonitorConnectionStringEntry");
    public Switch<AdvancedSettingsPage> SendCrashReportsSwitch => Switch("SendCrashReportsSwitch");
    public Entry<AdvancedSettingsPage> SentryDsnEntry => Entry("SentryDsnEntry");
    public Switch<AdvancedSettingsPage> SendUsageDataSwitch => Switch("SendUsageDataSwitch");
}
