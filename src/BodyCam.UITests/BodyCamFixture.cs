using System;
using Brinell.Maui.Testing;
using BodyCam.UITests.Pages;

namespace BodyCam.UITests;

public class BodyCamFixture : MauiTestFixtureBase
{
    private readonly MainPage _mainPage;
    private readonly SettingsPage _settingsPage;
    private readonly SetupPage _setupPage;

    public BodyCamFixture()
    {
        _mainPage = new MainPage(Context);
        _settingsPage = new SettingsPage(Context);
        _setupPage = new SetupPage(Context);
        DismissSetupIfShown();
    }

    public MainPage MainPage => _mainPage;
    public SettingsPage SettingsPage => _settingsPage;
    public SetupPage SetupPage => _setupPage;

    protected override string GetDefaultAppPath(string platform)
        => platform.ToLowerInvariant() switch
        {
            "windows" => @"E:\repos\Private\BodyCam\src\BodyCam\bin\Debug\net10.0-windows10.0.19041.0\win-x64\BodyCam.exe",
            _ => throw new NotSupportedException($"Platform '{platform}' is not supported.")
        };

    public void NavigateToHome()
    {
        _settingsPage.NavIcon.Click();
        _mainPage.WaitReady(10000);
    }

    public void NavigateToSettings()
    {
        _mainPage.NavIcon.Click();
        _settingsPage.WaitReady(10000);
    }

    /// <summary>
    /// If the Setup page is shown (first launch), skip through it to reach MainPage.
    /// </summary>
    private void DismissSetupIfShown()
    {
        if (!_setupPage.IsLoaded(3000)) return;

        // On Windows, setup only has the API Key step — skip through to finish
        for (int i = 0; i < 5; i++)
        {
            if (_mainPage.IsLoaded())
                break;

            if (_setupPage.SetupNextButton.IsExists())
                _setupPage.SetupNextButton.Click();
        }

        _mainPage.WaitReady(10000);
    }
}
