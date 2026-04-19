using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class ApiKeyTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.ConnectionSettingsPage Page => _fixture.ConnectionSettingsPage;

    public ApiKeyTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.ConnectionSettingsCard.Click(),
            _fixture.ConnectionSettingsPage);
    }

    [Fact]
    public void ApiKeyDisplay_Exists()
    {
        Page.ApiKeyDisplay.AssertExists();
    }

    [Fact]
    public void ToggleKeyVisibilityButton_Exists()
    {
        Page.ToggleKeyVisibilityButton.AssertExists();
    }

    [Fact]
    public void ChangeApiKeyButton_Exists()
    {
        Page.ChangeApiKeyButton.AssertExists();
    }

    [Fact]
    public void ClearApiKeyButton_Exists()
    {
        Page.ClearApiKeyButton.AssertExists();
    }

    [Fact]
    public void TestConnectionButton_Exists()
    {
        Page.TestConnectionButton.AssertExists();
    }

    [Fact]
    public void ToggleKeyVisibility_Click_DoesNotThrow()
    {
        Page.ToggleKeyVisibilityButton.Click();
        // Click again to restore
        Page.ToggleKeyVisibilityButton.Click();
    }
}
