using System;
using Brinell.Maui.Testing;
using BodyCam.UITests.Pages;

namespace BodyCam.UITests;

public class BodyCamFixture : MauiTestFixtureBase
{
    private readonly MainPage _mainPage;
    private readonly SettingsPage _settingsPage;

    public BodyCamFixture()
    {
        _mainPage = new MainPage(Context);
        _settingsPage = new SettingsPage(Context);
    }

    public MainPage MainPage => _mainPage;
    public SettingsPage SettingsPage => _settingsPage;

    protected override string GetDefaultAppPath(string platform)
        => platform.ToLowerInvariant() switch
        {
            "windows" => @"E:\repos\Private\BodyCam\src\BodyCam\bin\Debug\net10.0-windows10.0.19041.0\win-x64\BodyCam.exe",
            _ => throw new NotSupportedException($"Platform '{platform}' is not supported.")
        };

    public void NavigateToHome()
    {
        // Click the Home tab in Shell TabBar
        // Shell uses Title-based navigation — find by Name "Home"
        var homeTab = Context.TryFindElement(Locator.ByName("Home"));
        homeTab?.Click();
        _mainPage.WaitReady(10000);
    }

    public void NavigateToSettings()
    {
        var settingsTab = Context.TryFindElement(Locator.ByName("Settings"));
        settingsTab?.Click();
        _settingsPage.WaitReady(10000);
    }
}
