using System;
using Brinell.Maui.Testing;
using BodyCam.UITestKit.Pages;

namespace BodyCam.UITestKit;

public class BodyCamFixture : MauiTestFixtureBase
{
    private readonly MainPage _mainPage;
    private readonly SettingsPage _settingsPage;
    private readonly SetupPage _setupPage;
    private readonly ConnectionSettingsPage _connectionSettingsPage;
    private readonly LlmProvidersSettingsPage _llmProvidersSettingsPage;
    private readonly LlmProviderSettingsPage _llmProviderSettingsPage;
    private readonly VoiceSettingsPage _voiceSettingsPage;
    private readonly DeviceSettingsPage _deviceSettingsPage;
    private readonly AddDevicesPage _addDevicesPage;
    private readonly A9CameraSettingsPage _a9CameraSettingsPage;
    private readonly UsbCameraSettingsPage _usbCameraSettingsPage;
    private readonly AdvancedSettingsPage _advancedSettingsPage;

    public BodyCamFixture()
        : this(BodyCamFixtureLaunchOptions.Default)
    {
    }

    protected BodyCamFixture(BodyCamFixtureLaunchOptions launchOptions)
    {
        LaunchOptions = launchOptions;
        _mainPage = new MainPage(Context);
        _settingsPage = new SettingsPage(Context);
        _setupPage = new SetupPage(Context);
        _connectionSettingsPage = new ConnectionSettingsPage(Context);
        _llmProvidersSettingsPage = new LlmProvidersSettingsPage(Context);
        _llmProviderSettingsPage = new LlmProviderSettingsPage(Context);
        _voiceSettingsPage = new VoiceSettingsPage(Context);
        _deviceSettingsPage = new DeviceSettingsPage(Context);
        _addDevicesPage = new AddDevicesPage(Context);
        _a9CameraSettingsPage = new A9CameraSettingsPage(Context);
        _usbCameraSettingsPage = new UsbCameraSettingsPage(Context);
        _advancedSettingsPage = new AdvancedSettingsPage(Context);
        DismissSetupIfShown();
    }

    public BodyCamFixtureLaunchOptions LaunchOptions { get; }

    public MainPage MainPage => _mainPage;
    public SettingsPage SettingsPage => _settingsPage;
    public SetupPage SetupPage => _setupPage;
    public ConnectionSettingsPage ConnectionSettingsPage => _connectionSettingsPage;
    public LlmProvidersSettingsPage LlmProvidersSettingsPage => _llmProvidersSettingsPage;
    public LlmProviderSettingsPage LlmProviderSettingsPage => _llmProviderSettingsPage;
    public VoiceSettingsPage VoiceSettingsPage => _voiceSettingsPage;
    public DeviceSettingsPage DeviceSettingsPage => _deviceSettingsPage;
    public AddDevicesPage AddDevicesPage => _addDevicesPage;
    public A9CameraSettingsPage A9CameraSettingsPage => _a9CameraSettingsPage;
    public UsbCameraSettingsPage UsbCameraSettingsPage => _usbCameraSettingsPage;
    public AdvancedSettingsPage AdvancedSettingsPage => _advancedSettingsPage;

    protected override string GetDefaultAppPath(string platform)
        => platform.ToLowerInvariant() switch
        {
            "windows" => @"E:\repos\Private\BodyCam\src\BodyCam\bin\Debug\net10.0-windows10.0.19041.0\win-x64\BodyCam.exe",
            _ => throw new NotSupportedException($"Platform '{platform}' is not supported.")
        };

    public void NavigateToHome()
    {
        if (IsHomeRootLoaded(1000)) return;

        // On a pushed page, main-page controls can still exist in the UI tree.
        // The first-page settings button is only visible at the root, so use it
        // as the stronger home sentinel.
        for (int i = 0; i < 5; i++)
        {
            Context.NavigateBack();
            if (IsHomeRootLoaded(1500)) return;
        }

        if (!IsHomeRootLoaded(5000))
            throw new InvalidOperationException("Could not navigate back to the BodyCam home root.");
    }

    public void NavigateToSettings()
    {
        if (_settingsPage.IsLoaded(2000)) return;

        // Ensure we're on MainPage first (settings button only visible there)
        NavigateToHome();
        _mainPage.NavIcon.Click();
        _settingsPage.WaitReady(5000);
    }

    public void NavigateToConnectionSettings()
    {
        NavigateToSettingsSubPage(
            () => _settingsPage.ConnectionSettingsCard.Click(),
            _connectionSettingsPage);
    }

    private bool IsHomeRootLoaded(int timeoutMs)
        => _mainPage.IsLoaded(timeoutMs) && _mainPage.NavIcon.WaitExists(true, timeoutMs);

    public void NavigateToSettingsSubPage(Action clickCard, IPageObject subPage)
    {
        NavigateToSettings();
        clickCard();
        subPage.WaitReady(10000);
    }

    public void NavigateToLlmProviders()
    {
        NavigateToSettingsSubPage(
            () => _settingsPage.ConnectionSettingsCard.Click(),
            _llmProvidersSettingsPage);
    }

    public void NavigateToLlmProvidersSettings()
    {
        NavigateToLlmProviders();
    }

    public void NavigateToVoiceSettings()
    {
        NavigateToSettingsSubPage(
            () => _settingsPage.VoiceSettingsCard.Click(),
            _voiceSettingsPage);
    }

    public void NavigateToDeviceSettings()
    {
        NavigateToSettingsSubPage(
            () => _settingsPage.DeviceSettingsCard.Click(),
            _deviceSettingsPage);
    }

    public void NavigateToAdvancedSettings()
    {
        NavigateToSettingsSubPage(
            () => _settingsPage.AdvancedSettingsCard.Click(),
            _advancedSettingsPage);
    }

    public void NavigateToLlmProviderDetail(Action clickProvider)
    {
        NavigateToLlmProviders();
        clickProvider();
        _llmProviderSettingsPage.WaitReady(10000);
    }

    public virtual void ResetScenarioState()
    {
        NavigateToHome();

        if (_mainPage.DismissSnapshotButton.WaitExists(true, 500))
            _mainPage.DismissSnapshotButton.Click();

        if (_mainPage.MessageEntry.WaitExists(true, 500))
            _mainPage.MessageEntry.Clear();

        if (_mainPage.SleepButton.WaitExists(true, 500))
            _mainPage.SleepButton.Click();

        _mainPage.EnsureActionsExpanded();
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
