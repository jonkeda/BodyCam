using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.Services;

public sealed class SourceProfileManagerTests
{
    private readonly FakeSettingsService _settings = new();

    // Stub profiles for testing manager logic without real managers
    private sealed class StubProfile : ISourceProfile
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public int Order { get; init; }
        public bool IsAvailable { get; set; } = true;
        public string? UnavailableReason => IsAvailable ? null : "(offline)";
        public int FallbackPriority { get; init; }
        public int ApplyCount { get; private set; }

        public Task ApplyAsync(CameraManager camera, AudioInputManager mic,
                                AudioOutputManager speaker, CancellationToken ct = default)
        {
            ApplyCount++;
            return Task.CompletedTask;
        }
    }

    private SourceProfileManager CreateManager(params StubProfile[] profiles)
    {
        // SourceProfileManager only calls ApplyAsync on profiles (which is stubbed),
        // so we pass null managers via Unsafe — they're never dereferenced in tests.
        return new SourceProfileManager(
            profiles,
            null!,  // CameraManager — stubbed profiles don't use it
            null!,  // AudioInputManager
            null!,  // AudioOutputManager
            _settings,
            NullLogger<SourceProfileManager>.Instance);
    }

    [Fact]
    public void AvailableProfiles_OrderedByOrder()
    {
        var custom = new StubProfile { Id = "custom", Order = 100, FallbackPriority = 0 };
        var phone = new StubProfile { Id = "phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", Order = 20, FallbackPriority = 100 };

        var mgr = CreateManager(custom, phone, glasses);

        mgr.AvailableProfiles.Select(p => p.Id).Should()
            .ContainInOrder("phone", "heycyan", "custom");
    }

    [Fact]
    public async Task ApplyProfileAsync_SetsActiveProfile()
    {
        var phone = new StubProfile { Id = "phone", Order = 10, FallbackPriority = 10 };
        var custom = new StubProfile { Id = "custom", Order = 100, FallbackPriority = 0 };
        var mgr = CreateManager(phone, custom);

        await mgr.ApplyProfileAsync("custom");

        mgr.ActiveProfile!.Id.Should().Be("custom");
    }

    [Fact]
    public async Task ApplyProfileAsync_PersistsProfileId()
    {
        var phone = new StubProfile { Id = "phone", Order = 10, FallbackPriority = 10 };
        var mgr = CreateManager(phone);

        await mgr.ApplyProfileAsync("phone");

        _settings.DeviceSettings.ActiveProfileId.Should().Be("phone");
    }

    [Fact]
    public async Task ApplyProfileAsync_CallsProfileApply()
    {
        var phone = new StubProfile { Id = "phone", Order = 10, FallbackPriority = 10 };
        var mgr = CreateManager(phone);

        await mgr.ApplyProfileAsync("phone");

        phone.ApplyCount.Should().Be(1);
    }

    [Fact]
    public async Task ApplyProfileAsync_FiresProfileChanged()
    {
        var phone = new StubProfile { Id = "phone", Order = 10, FallbackPriority = 10 };
        var mgr = CreateManager(phone);

        var fired = false;
        mgr.ProfileChanged += (_, _) => fired = true;

        await mgr.ApplyProfileAsync("phone");

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyProfileAsync_UnknownId_DoesNothing()
    {
        var phone = new StubProfile { Id = "phone", Order = 10, FallbackPriority = 10 };
        _settings.DeviceSettings = new DeviceSettings { ActiveProfileId = "phone" };
        var mgr = CreateManager(phone);

        await mgr.ApplyProfileAsync("nonexistent");

        mgr.ActiveProfile!.Id.Should().Be("phone", "should remain on previous profile");
        phone.ApplyCount.Should().Be(0);
    }

    // ── HandleDeviceConnectedAsync ──────────────────────────────────────────

    [Fact]
    public async Task HandleDeviceConnectedAsync_UpgradesToHigherPriority()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100, IsAvailable = false };
        var mgr = CreateManager(phone, glasses);

        await mgr.ApplyProfileAsync("phone");

        // Simulate glasses connecting
        glasses.IsAvailable = true;
        await mgr.HandleDeviceConnectedAsync();

        mgr.ActiveProfile!.Id.Should().Be("heycyan");
    }

    [Fact]
    public async Task HandleDeviceConnectedAsync_FiresAutoSwitchedEvent()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100, IsAvailable = false };
        var mgr = CreateManager(phone, glasses);
        await mgr.ApplyProfileAsync("phone");

        ProfileSwitchNotification? notification = null;
        mgr.AutoSwitched += (_, n) => notification = n;

        glasses.IsAvailable = true;
        await mgr.HandleDeviceConnectedAsync();

        notification.Should().NotBeNull();
        notification!.Reason.Should().Be(ProfileSwitchReason.DeviceConnected);
        notification.NewProfileName.Should().Be("HeyCyan Glasses");
        notification.OldProfileName.Should().Be("Phone");
    }

    [Fact]
    public async Task HandleDeviceConnectedAsync_DoesNotDowngrade()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };
        var mgr = CreateManager(phone, glasses);

        await mgr.ApplyProfileAsync("heycyan");
        await mgr.HandleDeviceConnectedAsync();

        mgr.ActiveProfile!.Id.Should().Be("heycyan", "should not downgrade to phone");
    }

    [Fact]
    public async Task HandleDeviceConnectedAsync_SkipsCustomProfile()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var custom = new StubProfile { Id = "custom", DisplayName = "Custom", Order = 100, FallbackPriority = 0 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };
        var mgr = CreateManager(phone, custom, glasses);

        await mgr.ApplyProfileAsync("custom");
        await mgr.HandleDeviceConnectedAsync();

        mgr.ActiveProfile!.Id.Should().Be("custom",
            "should not auto-switch away from Custom");
    }

    [Fact]
    public async Task HandleDeviceConnectedAsync_NoHigherPriority_KeepsCurrent()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var mgr = CreateManager(phone);

        await mgr.ApplyProfileAsync("phone");
        await mgr.HandleDeviceConnectedAsync();

        mgr.ActiveProfile!.Id.Should().Be("phone");
    }

    [Fact]
    public async Task HandleDeviceConnectedAsync_UpgradesToBtFromPhone()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var bt = new StubProfile { Id = "bluetooth", DisplayName = "Bluetooth", Order = 30, FallbackPriority = 50, IsAvailable = false };
        var mgr = CreateManager(phone, bt);

        await mgr.ApplyProfileAsync("phone");

        bt.IsAvailable = true;
        await mgr.HandleDeviceConnectedAsync();

        mgr.ActiveProfile!.Id.Should().Be("bluetooth");
    }

    // ── HandleDeviceDisconnectedAsync ────────────────────────────────────────

    [Fact]
    public async Task HandleDeviceDisconnectedAsync_FallsBackWhenCurrentUnavailable()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };
        var mgr = CreateManager(phone, glasses);

        await mgr.ApplyProfileAsync("heycyan");

        glasses.IsAvailable = false;
        await mgr.HandleDeviceDisconnectedAsync();

        mgr.ActiveProfile!.Id.Should().Be("phone");
    }

    [Fact]
    public async Task HandleDeviceDisconnectedAsync_FiresAutoSwitchedEvent()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };
        var mgr = CreateManager(phone, glasses);
        await mgr.ApplyProfileAsync("heycyan");

        ProfileSwitchNotification? notification = null;
        mgr.AutoSwitched += (_, n) => notification = n;

        glasses.IsAvailable = false;
        await mgr.HandleDeviceDisconnectedAsync();

        notification.Should().NotBeNull();
        notification!.Reason.Should().Be(ProfileSwitchReason.DeviceDisconnected);
        notification.NewProfileName.Should().Be("Phone");
        notification.OldProfileName.Should().Be("HeyCyan Glasses");
    }

    [Fact]
    public async Task HandleDeviceDisconnectedAsync_KeepsCurrentIfStillAvailable()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };
        var mgr = CreateManager(phone, glasses);

        await mgr.ApplyProfileAsync("heycyan");

        // Glasses still connected — disconnect is for some other device
        await mgr.HandleDeviceDisconnectedAsync();

        mgr.ActiveProfile!.Id.Should().Be("heycyan");
    }

    [Fact]
    public async Task HandleDeviceDisconnectedAsync_PicksHighestPriorityFallback()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var bt = new StubProfile { Id = "bluetooth", DisplayName = "Bluetooth", Order = 30, FallbackPriority = 50 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };
        var mgr = CreateManager(phone, bt, glasses);

        await mgr.ApplyProfileAsync("heycyan");
        glasses.IsAvailable = false;

        await mgr.HandleDeviceDisconnectedAsync();

        mgr.ActiveProfile!.Id.Should().Be("bluetooth",
            "BT has higher fallback priority than phone");
    }

    [Fact]
    public async Task HandleDeviceDisconnectedAsync_NoAvailable_DoesNothing()
    {
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };
        var mgr = CreateManager(glasses);
        await mgr.ApplyProfileAsync("heycyan");

        glasses.IsAvailable = false;
        await mgr.HandleDeviceDisconnectedAsync();

        mgr.ActiveProfile!.Id.Should().Be("heycyan",
            "no fallback available, stays on current even though unavailable");
    }

    // ── HandleDeviceChangedAsync (backward compat) ──────────────────────────

    [Fact]
    public async Task HandleDeviceChangedAsync_UpgradesWhenCurrentAvailable()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100, IsAvailable = false };
        var mgr = CreateManager(phone, glasses);
        await mgr.ApplyProfileAsync("phone");

        glasses.IsAvailable = true;
        await mgr.HandleDeviceChangedAsync();

        mgr.ActiveProfile!.Id.Should().Be("heycyan",
            "backward-compat HandleDeviceChangedAsync should auto-upgrade");
    }

    [Fact]
    public async Task HandleDeviceChangedAsync_FallsBackWhenCurrentUnavailable()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };
        var mgr = CreateManager(phone, glasses);
        await mgr.ApplyProfileAsync("heycyan");

        glasses.IsAvailable = false;
        await mgr.HandleDeviceChangedAsync();

        mgr.ActiveProfile!.Id.Should().Be("phone");
    }

    // ── InitializeAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_RestoresSavedProfile()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };

        _settings.DeviceSettings = new DeviceSettings { ActiveProfileId = "heycyan" };
        var mgr = CreateManager(phone, glasses);

        await mgr.InitializeAsync();

        mgr.ActiveProfile!.Id.Should().Be("heycyan");
        glasses.ApplyCount.Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_FallsBackWhenSavedUnavailable()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100, IsAvailable = false };

        _settings.DeviceSettings = new DeviceSettings { ActiveProfileId = "heycyan" };
        var mgr = CreateManager(phone, glasses);

        await mgr.InitializeAsync();

        mgr.ActiveProfile!.Id.Should().Be("phone",
            "heycyan unavailable → falls back to phone");
    }

    [Fact]
    public async Task InitializeAsync_FallsBackWhenSavedIdNotFound()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };

        _settings.DeviceSettings = new DeviceSettings { ActiveProfileId = "deleted-profile" };
        var mgr = CreateManager(phone);

        await mgr.InitializeAsync();

        mgr.ActiveProfile!.Id.Should().Be("phone");
    }

    [Fact]
    public async Task InitializeAsync_FiresAutoSwitchedOnFallback()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100, IsAvailable = false };

        _settings.DeviceSettings = new DeviceSettings { ActiveProfileId = "heycyan" };
        var mgr = CreateManager(phone, glasses);

        ProfileSwitchNotification? notification = null;
        mgr.AutoSwitched += (_, n) => notification = n;

        await mgr.InitializeAsync();

        notification.Should().NotBeNull();
        notification!.Reason.Should().Be(ProfileSwitchReason.StartupFallback);
        notification.NewProfileName.Should().Be("Phone");
    }

    [Fact]
    public async Task InitializeAsync_NoAutoSwitchedWhenSavedRestored()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        _settings.DeviceSettings = new DeviceSettings { ActiveProfileId = "phone" };
        var mgr = CreateManager(phone);

        ProfileSwitchNotification? notification = null;
        mgr.AutoSwitched += (_, n) => notification = n;

        await mgr.InitializeAsync();

        notification.Should().BeNull("no auto-switch when saved profile restores successfully");
    }

    // ── Constructor ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsActiveProfileFromSettings()
    {
        var phone = new StubProfile { Id = "phone", Order = 10, FallbackPriority = 10 };
        _settings.DeviceSettings = new DeviceSettings { ActiveProfileId = "phone" };

        var mgr = CreateManager(phone);

        mgr.ActiveProfile!.Id.Should().Be("phone");
    }

    [Fact]
    public void Constructor_UnknownSavedId_ActiveProfileIsNull()
    {
        var phone = new StubProfile { Id = "phone", Order = 10, FallbackPriority = 10 };
        _settings.DeviceSettings = new DeviceSettings { ActiveProfileId = "nonexistent" };

        var mgr = CreateManager(phone);

        mgr.ActiveProfile.Should().BeNull();
    }

    [Fact]
    public async Task ApplyProfileAsync_Twice_OnlyLastProfileActive()
    {
        var phone = new StubProfile { Id = "phone", Order = 10, FallbackPriority = 10 };
        var custom = new StubProfile { Id = "custom", Order = 100, FallbackPriority = 0 };
        var mgr = CreateManager(phone, custom);

        await mgr.ApplyProfileAsync("phone");
        await mgr.ApplyProfileAsync("custom");

        mgr.ActiveProfile!.Id.Should().Be("custom");
        _settings.DeviceSettings.ActiveProfileId.Should().Be("custom");
    }

    [Fact]
    public async Task ProfileChanged_FiresOnEachSwitch()
    {
        var phone = new StubProfile { Id = "phone", Order = 10, FallbackPriority = 10 };
        var custom = new StubProfile { Id = "custom", Order = 100, FallbackPriority = 0 };
        var mgr = CreateManager(phone, custom);

        var count = 0;
        mgr.ProfileChanged += (_, _) => count++;

        await mgr.ApplyProfileAsync("phone");
        await mgr.ApplyProfileAsync("custom");
        await mgr.ApplyProfileAsync("phone");

        count.Should().Be(3);
    }

    // ── Multi-device scenarios ──────────────────────────────────────────────

    [Fact]
    public async Task FullConnectDisconnectCycle_GlassesAndBt()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var bt = new StubProfile { Id = "bluetooth", DisplayName = "Bluetooth", Order = 30, FallbackPriority = 50, IsAvailable = false };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100, IsAvailable = false };
        var mgr = CreateManager(phone, bt, glasses);

        await mgr.ApplyProfileAsync("phone");

        // BT connects → upgrade to BT
        bt.IsAvailable = true;
        await mgr.HandleDeviceConnectedAsync();
        mgr.ActiveProfile!.Id.Should().Be("bluetooth");

        // Glasses connect → upgrade to glasses (higher priority)
        glasses.IsAvailable = true;
        await mgr.HandleDeviceConnectedAsync();
        mgr.ActiveProfile!.Id.Should().Be("heycyan");

        // Glasses disconnect → fall back to BT (next highest)
        glasses.IsAvailable = false;
        await mgr.HandleDeviceDisconnectedAsync();
        mgr.ActiveProfile!.Id.Should().Be("bluetooth");

        // BT disconnect → fall back to phone
        bt.IsAvailable = false;
        await mgr.HandleDeviceDisconnectedAsync();
        mgr.ActiveProfile!.Id.Should().Be("phone");
    }

    [Fact]
    public async Task MultipleBtDevices_PreferHigherPriority()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var bt = new StubProfile { Id = "bluetooth", DisplayName = "Bluetooth", Order = 30, FallbackPriority = 50 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };
        var mgr = CreateManager(phone, bt, glasses);

        // Start on glasses, both BT and phone available
        await mgr.ApplyProfileAsync("heycyan");

        // Glasses disconnect — should pick BT (priority 50) over phone (priority 10)
        glasses.IsAvailable = false;
        await mgr.HandleDeviceDisconnectedAsync();

        mgr.ActiveProfile!.Id.Should().Be("bluetooth");
    }

    [Fact]
    public async Task NotificationMessage_DeviceConnected_ContainsDeviceName()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100, IsAvailable = false };
        var mgr = CreateManager(phone, glasses);
        await mgr.ApplyProfileAsync("phone");

        ProfileSwitchNotification? notification = null;
        mgr.AutoSwitched += (_, n) => notification = n;

        glasses.IsAvailable = true;
        await mgr.HandleDeviceConnectedAsync();

        notification!.Message.Should().Contain("HeyCyan Glasses");
        notification.Message.Should().Contain("device connected");
    }

    [Fact]
    public async Task NotificationMessage_DeviceDisconnected_ContainsOldName()
    {
        var phone = new StubProfile { Id = "phone", DisplayName = "Phone", Order = 10, FallbackPriority = 10 };
        var glasses = new StubProfile { Id = "heycyan", DisplayName = "HeyCyan Glasses", Order = 20, FallbackPriority = 100 };
        var mgr = CreateManager(phone, glasses);
        await mgr.ApplyProfileAsync("heycyan");

        ProfileSwitchNotification? notification = null;
        mgr.AutoSwitched += (_, n) => notification = n;

        glasses.IsAvailable = false;
        await mgr.HandleDeviceDisconnectedAsync();

        notification!.Message.Should().Contain("HeyCyan Glasses");
        notification.Message.Should().Contain("disconnected");
    }
}
