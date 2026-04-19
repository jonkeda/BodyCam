using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class DebugSettingsTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.AdvancedSettingsPage Page => _fixture.AdvancedSettingsPage;

    public DebugSettingsTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.AdvancedSettingsCard.Click(),
            _fixture.AdvancedSettingsPage);
    }

    [Fact]
    public void DebugModeSwitch_Exists()
    {
        Page.DebugModeSwitch.AssertExists();
    }

    [Fact]
    public void ShowTokenCountsSwitch_Exists()
    {
        Page.ShowTokenCountsSwitch.AssertExists();
    }

    [Fact]
    public void ShowCostEstimateSwitch_Exists()
    {
        Page.ShowCostEstimateSwitch.AssertExists();
    }

    [Fact(Skip = "MAUI Switch does not respond to FlaUI toggle on WinUI3")]
    public void DebugModeSwitch_CanToggle()
    {
        var initial = Page.DebugModeSwitch.IsOn();
        Page.DebugModeSwitch.Toggle();
        var toggled = Page.DebugModeSwitch.IsOn();
        Assert.NotEqual(initial, toggled);
        // Restore
        Page.DebugModeSwitch.Toggle();
    }

    [Fact(Skip = "MAUI Switch does not respond to FlaUI toggle on WinUI3")]
    public void ShowTokenCountsSwitch_CanToggle()
    {
        var initial = Page.ShowTokenCountsSwitch.IsOn();
        Page.ShowTokenCountsSwitch.Toggle();
        var toggled = Page.ShowTokenCountsSwitch.IsOn();
        Assert.NotEqual(initial, toggled);
        // Restore
        Page.ShowTokenCountsSwitch.Toggle();
    }

    [Fact(Skip = "MAUI Switch does not respond to FlaUI toggle on WinUI3")]
    public void ShowCostEstimateSwitch_CanToggle()
    {
        var initial = Page.ShowCostEstimateSwitch.IsOn();
        Page.ShowCostEstimateSwitch.Toggle();
        var toggled = Page.ShowCostEstimateSwitch.IsOn();
        Assert.NotEqual(initial, toggled);
        // Restore
        Page.ShowCostEstimateSwitch.Toggle();
    }
}
