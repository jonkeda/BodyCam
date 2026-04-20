using BodyCam.UITests.Pages;

namespace BodyCam.UITests.Tests.Navigation;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Feature", "Navigation")]
public class NavigationTests
{
    private readonly BodyCamFixture _fixture;

    public NavigationTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void NavigateToSettings_ShowsSettingsPage()
    {
        _fixture.NavigateToSettings();
        Assert.True(_fixture.SettingsPage.IsLoaded());
    }

    [Fact]
    public void NavigateBackFromSettings_ShowsMainPage()
    {
        _fixture.NavigateToSettings();
        _fixture.NavigateToHome();
        Assert.True(_fixture.MainPage.IsLoaded());
    }

    [Fact]
    public void Navigation_RoundTrip_AllPagesLoad()
    {
        _fixture.NavigateToHome();
        Assert.True(_fixture.MainPage.IsLoaded());

        _fixture.NavigateToSettings();
        Assert.True(_fixture.SettingsPage.IsLoaded());

        _fixture.NavigateToHome();
        Assert.True(_fixture.MainPage.IsLoaded());
    }
}
