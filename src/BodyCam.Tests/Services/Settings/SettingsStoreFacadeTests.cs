using BodyCam.Models;
using BodyCam.Services.Camera.Commands;
using BodyCam.Services.Settings;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;

namespace BodyCam.Tests.Services.Settings;

public sealed class SettingsStoreFacadeTests
{
    [Fact]
    public void AiProviderFacade_ReadsAndWritesUnderlyingSettings()
    {
        var settings = new FakeSettingsService();
        IAiProviderSettingsStore providers = new SettingsStoreFacade(settings);

        providers.ProviderId = "azure-openai";
        providers.ChatModel = "chat-model";
        providers.AzureEndpoint = "https://example.test";

        settings.ProviderId.Should().Be("azure-openai");
        settings.ChatModel.Should().Be("chat-model");
        settings.AzureEndpoint.Should().Be("https://example.test");
    }

    [Fact]
    public void DeviceFacade_ReadsAndWritesDeviceSettings()
    {
        var settings = new FakeSettingsService();
        IDeviceSettingsStore devices = new SettingsStoreFacade(settings);

        devices.DeviceSettings = new DeviceSettings { ActiveProfileId = "heycyan-glasses" };
        devices.DefaultTouchCommandMode = CameraCommandMode.ManualAim;
        devices.HeyCyanAutoReconnect = true;

        settings.DeviceSettings.ActiveProfileId.Should().Be("heycyan-glasses");
        settings.DefaultTouchCommandMode.Should().Be(CameraCommandMode.ManualAim);
        settings.HeyCyanAutoReconnect.Should().BeTrue();
    }

    [Fact]
    public void DiagnosticsFacade_ReadsAndWritesUnderlyingSettings()
    {
        var settings = new FakeSettingsService();
        IDiagnosticsSettingsStore diagnostics = new SettingsStoreFacade(settings);

        diagnostics.SendUsageData = true;
        diagnostics.SentryDsn = "dsn";

        settings.SendUsageData.Should().BeTrue();
        settings.SentryDsn.Should().Be("dsn");
    }
}
