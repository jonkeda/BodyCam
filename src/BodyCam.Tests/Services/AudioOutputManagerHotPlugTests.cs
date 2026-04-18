using BodyCam.Services;
using BodyCam.Services.Audio;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services;

public class AudioOutputManagerHotPlugTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly AppSettings _appSettings = new();

    private AudioOutputManager CreateManager(params IAudioOutputProvider[] providers)
        => new(providers, _settings, _appSettings);

    [Fact]
    public void RegisterProvider_AddsToList()
    {
        var manager = CreateManager();
        var btProvider = Substitute.For<IAudioOutputProvider>();
        btProvider.ProviderId.Returns("bt-out:test");

        manager.RegisterProvider(btProvider);

        manager.Providers.Should().ContainSingle(p => p.ProviderId == "bt-out:test");
    }

    [Fact]
    public void RegisterProvider_IgnoresDuplicate()
    {
        var bt1 = Substitute.For<IAudioOutputProvider>();
        bt1.ProviderId.Returns("bt-out:test");
        var bt2 = Substitute.For<IAudioOutputProvider>();
        bt2.ProviderId.Returns("bt-out:test");

        var manager = CreateManager(bt1);

        manager.RegisterProvider(bt2);

        manager.Providers.Should().HaveCount(1);
    }

    [Fact]
    public async Task RegisterProvider_AutoSwitches_WhenSavedPrefMatches()
    {
        var speaker = Substitute.For<IAudioOutputProvider>();
        speaker.ProviderId.Returns("windows-speaker");
        speaker.IsAvailable.Returns(true);

        var manager = CreateManager(speaker);
        await manager.SetActiveAsync("windows-speaker");
        // Set saved preference AFTER activation (SetActiveAsync overwrites it)
        _settings.ActiveAudioOutputProvider.Returns("bt-out:test");

        var btProvider = Substitute.For<IAudioOutputProvider>();
        btProvider.ProviderId.Returns("bt-out:test");
        btProvider.IsAvailable.Returns(true);

        manager.RegisterProvider(btProvider);

        await Task.Delay(50); // let async switch complete
        manager.Active?.ProviderId.Should().Be("bt-out:test");
    }

    [Fact]
    public async Task UnregisterProvider_FallsBack()
    {
        var speaker = Substitute.For<IAudioOutputProvider>();
        speaker.ProviderId.Returns("windows-speaker");
        speaker.IsAvailable.Returns(true);

        var btProvider = Substitute.For<IAudioOutputProvider>();
        btProvider.ProviderId.Returns("bt-out:test");
        btProvider.IsAvailable.Returns(true);

        var manager = CreateManager(speaker);
        manager.RegisterProvider(btProvider);
        await manager.SetActiveAsync("bt-out:test");
        manager.Active?.ProviderId.Should().Be("bt-out:test");

        await manager.UnregisterProviderAsync("bt-out:test");

        manager.Active?.ProviderId.Should().Be("windows-speaker");
        manager.Providers.Should().NotContain(p => p.ProviderId == "bt-out:test");
    }

    [Fact]
    public async Task UnregisterProvider_DisposesProvider()
    {
        var btProvider = Substitute.For<IAudioOutputProvider>();
        btProvider.ProviderId.Returns("bt-out:test");

        var manager = CreateManager();
        manager.RegisterProvider(btProvider);

        await manager.UnregisterProviderAsync("bt-out:test");

        await btProvider.Received(1).DisposeAsync();
    }

    [Fact]
    public void ProvidersChanged_FiresOnRegister()
    {
        var manager = CreateManager();
        var fired = false;
        manager.ProvidersChanged += (_, _) => fired = true;

        var btProvider = Substitute.For<IAudioOutputProvider>();
        btProvider.ProviderId.Returns("bt-out:test");
        manager.RegisterProvider(btProvider);

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task ProvidersChanged_FiresOnUnregister()
    {
        var btProvider = Substitute.For<IAudioOutputProvider>();
        btProvider.ProviderId.Returns("bt-out:test");

        var manager = CreateManager();
        manager.RegisterProvider(btProvider);

        var fired = false;
        manager.ProvidersChanged += (_, _) => fired = true;

        await manager.UnregisterProviderAsync("bt-out:test");

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task UnregisterProvider_Noop_WhenNotFound()
    {
        var manager = CreateManager();

        await manager.UnregisterProviderAsync("nonexistent");

        manager.Providers.Should().BeEmpty();
    }
}
