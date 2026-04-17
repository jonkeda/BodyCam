using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class DebugSettingsTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.SettingsPage Page => _fixture.SettingsPage;

    public DebugSettingsTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettings();
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

    [Fact(Skip = "Debug switches are below ScrollView fold; FlaUI cannot scroll MAUI ScrollViews")]
    public void DebugModeSwitch_CanToggle()
    {
        var initial = Page.DebugModeSwitch.IsOn();
        Page.DebugModeSwitch.Toggle();
        var toggled = Page.DebugModeSwitch.IsOn();
        Assert.NotEqual(initial, toggled);
        // Restore
        Page.DebugModeSwitch.Toggle();
    }

    [Fact(Skip = "Debug switches are below ScrollView fold; FlaUI cannot scroll MAUI ScrollViews")]
    public void ShowTokenCountsSwitch_CanToggle()
    {
        var initial = Page.ShowTokenCountsSwitch.IsOn();
        Page.ShowTokenCountsSwitch.Toggle();
        var toggled = Page.ShowTokenCountsSwitch.IsOn();
        Assert.NotEqual(initial, toggled);
        // Restore
        Page.ShowTokenCountsSwitch.Toggle();
    }

    [Fact(Skip = "Debug switches are below ScrollView fold; FlaUI cannot scroll MAUI ScrollViews")]
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
