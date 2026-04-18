using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Tests.TestInfrastructure.Providers;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services;

public class AudioInputManagerHotPlugTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    private AudioInputManager CreateManager(params IAudioInputProvider[] providers)
        => new(providers, _settings);

    [Fact]
    public void RegisterProvider_AddsToList()
    {
        var manager = CreateManager();
        var mic = new TestMicProvider(new byte[100]);

        manager.RegisterProvider(mic);

        manager.Providers.Should().ContainSingle(p => p.ProviderId == mic.ProviderId);
    }

    [Fact]
    public void RegisterProvider_IgnoresDuplicate()
    {
        var mic1 = new TestMicProvider(new byte[100]);
        var mic2 = new TestMicProvider(new byte[200]); // same ProviderId
        var manager = CreateManager(mic1);

        manager.RegisterProvider(mic2);

        manager.Providers.Should().HaveCount(1);
    }

    [Fact]
    public async Task RegisterProvider_AutoSwitches_WhenSavedPrefMatches()
    {
        // Platform provider has ProviderId "platform" — needed for fallback/auto-switch logic
        var platform = Substitute.For<IAudioInputProvider>();
        platform.ProviderId.Returns("platform");
        platform.IsAvailable.Returns(true);

        var manager = CreateManager(platform);
        await manager.SetActiveAsync("platform");
        // Set saved preference AFTER activation (SetActiveAsync overwrites it)
        _settings.ActiveAudioInputProvider.Returns("bt-test");

        // Simulate a BT provider arriving that matches saved preference
        var btProvider = Substitute.For<IAudioInputProvider>();
        btProvider.ProviderId.Returns("bt-test");
        btProvider.IsAvailable.Returns(true);

        manager.RegisterProvider(btProvider);

        // Should auto-switch since saved pref matches and current is platform
        await Task.Delay(50); // let async switch complete
        manager.Active?.ProviderId.Should().Be("bt-test");
    }

    [Fact]
    public async Task UnregisterProvider_FallsBack()
    {
        var platform = Substitute.For<IAudioInputProvider>();
        platform.ProviderId.Returns("platform");
        platform.IsAvailable.Returns(true);

        var btProvider = Substitute.For<IAudioInputProvider>();
        btProvider.ProviderId.Returns("bt-test");
        btProvider.IsAvailable.Returns(true);

        var manager = CreateManager(platform);
        manager.RegisterProvider(btProvider);
        await manager.SetActiveAsync("bt-test");
        manager.Active?.ProviderId.Should().Be("bt-test");

        await manager.UnregisterProviderAsync("bt-test");

        manager.Active?.ProviderId.Should().Be("platform");
        manager.Providers.Should().NotContain(p => p.ProviderId == "bt-test");
    }

    [Fact]
    public async Task UnregisterProvider_DisposesProvider()
    {
        var btProvider = Substitute.For<IAudioInputProvider>();
        btProvider.ProviderId.Returns("bt-test");

        var manager = CreateManager();
        manager.RegisterProvider(btProvider);

        await manager.UnregisterProviderAsync("bt-test");

        await btProvider.Received(1).DisposeAsync();
    }

    [Fact]
    public void ProvidersChanged_FiresOnRegister()
    {
        var manager = CreateManager();
        var fired = false;
        manager.ProvidersChanged += (_, _) => fired = true;

        manager.RegisterProvider(new TestMicProvider(new byte[100]));

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task ProvidersChanged_FiresOnUnregister()
    {
        var btProvider = Substitute.For<IAudioInputProvider>();
        btProvider.ProviderId.Returns("bt-test");

        var manager = CreateManager();
        manager.RegisterProvider(btProvider);

        var fired = false;
        manager.ProvidersChanged += (_, _) => fired = true;

        await manager.UnregisterProviderAsync("bt-test");

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task UnregisterProvider_Noop_WhenNotFound()
    {
        var manager = CreateManager();

        // Should not throw
        await manager.UnregisterProviderAsync("nonexistent");

        manager.Providers.Should().BeEmpty();
    }
}
