using System;
using Brinell.Maui.Testing;
using BodyCam.UITests.Pages;

namespace BodyCam.UITests;

public class BodyCamFixture : MauiTestFixtureBase
{
    private readonly MainPage _mainPage;
    private readonly SettingsPage _settingsPage;
    private readonly SetupPage _setupPage;
    private readonly ConnectionSettingsPage _connectionSettingsPage;
    private readonly VoiceSettingsPage _voiceSettingsPage;
    private readonly DeviceSettingsPage _deviceSettingsPage;
    private readonly AdvancedSettingsPage _advancedSettingsPage;

    public BodyCamFixture()
    {
        _mainPage = new MainPage(Context);
        _settingsPage = new SettingsPage(Context);
        _setupPage = new SetupPage(Context);
        _connectionSettingsPage = new ConnectionSettingsPage(Context);
        _voiceSettingsPage = new VoiceSettingsPage(Context);
        _deviceSettingsPage = new DeviceSettingsPage(Context);
        _advancedSettingsPage = new AdvancedSettingsPage(Context);
        DismissSetupIfShown();
    }

    public MainPage MainPage => _mainPage;
    public SettingsPage SettingsPage => _settingsPage;
    public SetupPage SetupPage => _setupPage;
    public ConnectionSettingsPage ConnectionSettingsPage => _connectionSettingsPage;
    public VoiceSettingsPage VoiceSettingsPage => _voiceSettingsPage;
    public DeviceSettingsPage DeviceSettingsPage => _deviceSettingsPage;
    public AdvancedSettingsPage AdvancedSettingsPage => _advancedSettingsPage;

    protected override string GetDefaultAppPath(string platform)
        => platform.ToLowerInvariant() switch
        {
            "windows" => @"E:\repos\Private\BodyCam\src\BodyCam\bin\Debug\net10.0-windows10.0.19041.0\win-x64\BodyCam.exe",
            _ => throw new NotSupportedException($"Platform '{platform}' is not supported.")
        };

    public void NavigateToHome()
    {
        if (_mainPage.IsLoaded(2000)) return;

        // On a pushed page — use Shell back button to return
        for (int i = 0; i < 3; i++)
        {
            if (_mainPage.IsLoaded(1000)) return;
            Context.NavigateBack();
        }

        _mainPage.IsLoaded(5000);
    }

    public void NavigateToSettings()
    {
        if (_settingsPage.IsLoaded(2000)) return;

        // Ensure we're on MainPage first (⚙ button only visible there)
        NavigateToHome();
        _mainPage.NavIcon.Click();
        _settingsPage.WaitReady(5000);
    }

    public void NavigateToSettingsSubPage(Action clickCard, IPageObject subPage)
    {
        NavigateToSettings();
        clickCard();
        subPage.WaitReady(10000);
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
