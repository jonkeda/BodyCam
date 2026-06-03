using BodyCam.Models;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Input;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

namespace BodyCam.Tests.Services;

public sealed class AppRuntimeCoordinatorTests
{
    [Fact]
    public async Task StartAsync_InitializesRuntimeServicesAndSourceProfile()
    {
        var fixture = CreateFixture(savedProfileId: "custom");

        await fixture.Coordinator.StartAsync();

        fixture.Coordinator.IsStarted.Should().BeTrue();
        fixture.ButtonMappingStore.LoadCount.Should().Be(1);
        fixture.CameraManager.Active.Should().Be(fixture.Camera);
        fixture.AudioInputManager.Active.Should().Be(fixture.Mic);
        fixture.AudioOutputManager.Active.Should().Be(fixture.Speaker);
        fixture.Buttons.IsActive.Should().BeTrue();
        fixture.SourceProfileManager.ActiveProfile!.Id.Should().Be("custom");
        fixture.Settings.DeviceSettings.Active.CameraProviderId.Should().Be("test-camera");
        fixture.Settings.DeviceSettings.Active.AudioInputProviderId.Should().Be("test-mic");
        fixture.Settings.DeviceSettings.Active.AudioOutputProviderId.Should().Be("test-speaker");
    }

    [Fact]
    public async Task StartAsync_IsIdempotent()
    {
        var fixture = CreateFixture(savedProfileId: "custom");

        await fixture.Coordinator.StartAsync();
        await fixture.Coordinator.StartAsync();

        fixture.ButtonMappingStore.LoadCount.Should().Be(1);
        fixture.Camera.StartCount.Should().Be(1);
        fixture.Mic.StartCount.Should().Be(0, "initialization selects the mic but does not start capture");
        fixture.Speaker.StartCount.Should().Be(0, "initialization selects the speaker but does not start playback");
        fixture.Buttons.StartCount.Should().Be(1);
        fixture.Profiles.Single(p => p.Id == "custom").ApplyCount.Should().Be(1);
    }

    [Fact]
    public async Task AudioProviderChange_ReevaluatesSourceProfilePolicy()
    {
        var phone = new StubSourceProfile
        {
            Id = "phone",
            DisplayName = "Phone",
            Order = 10,
            FallbackPriority = 10,
        };
        var bluetooth = new StubSourceProfile
        {
            Id = "bluetooth",
            DisplayName = "Bluetooth",
            Order = 20,
            FallbackPriority = 50,
            IsAvailable = false,
        };
        var fixture = CreateFixture(savedProfileId: "phone", phone, bluetooth);
        await fixture.Coordinator.StartAsync();

        bluetooth.IsAvailable = true;
        fixture.AudioInputManager.RegisterProvider(new CountingAudioInputProvider("bt:AA:BB:CC:DD:EE:FF"));

        await WaitUntilAsync(() => fixture.SourceProfileManager.ActiveProfile?.Id == "bluetooth");

        fixture.SourceProfileManager.ActiveProfile!.Id.Should().Be("bluetooth");
        bluetooth.ApplyCount.Should().Be(1);
    }

    [Fact]
    public async Task GlassesConnect_ReevaluatesSourceProfilePolicy()
    {
        var phone = new StubSourceProfile
        {
            Id = "phone",
            DisplayName = "Phone",
            Order = 10,
            FallbackPriority = 10,
        };
        var glassesProfile = new StubSourceProfile
        {
            Id = "heycyan-glasses",
            DisplayName = "HeyCyan Glasses",
            Order = 20,
            FallbackPriority = 100,
            IsAvailable = false,
        };
        var glassesManager = CreateGlassesManager();
        var fixture = CreateFixtureCore(
            savedProfileId: "phone",
            profiles: [phone, glassesProfile],
            configureServices: services => services.AddSingleton(glassesManager));
        await fixture.Coordinator.StartAsync();

        glassesProfile.IsAvailable = true;
        await glassesManager.ConnectAsync(
            new HeyCyanDeviceInfo("HeyCyan Glasses", "AA:BB:CC:DD:EE:FF", -42),
            CancellationToken.None);

        await WaitUntilAsync(() => fixture.SourceProfileManager.ActiveProfile?.Id == "heycyan-glasses");

        fixture.SourceProfileManager.ActiveProfile!.Id.Should().Be("heycyan-glasses");
        glassesProfile.ApplyCount.Should().Be(1);
    }

    private static RuntimeFixture CreateFixture(
        string savedProfileId,
        params StubSourceProfile[] profiles)
        => CreateFixtureCore(savedProfileId, profiles.Length == 0 ? null : profiles, null);

    private static RuntimeFixture CreateFixtureCore(
        string savedProfileId,
        StubSourceProfile[]? profiles = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var settings = new FakeSettingsService
        {
            ActiveCameraProvider = "test-camera",
            ActiveAudioInputProvider = "test-mic",
            ActiveAudioOutputProvider = "test-speaker",
            DeviceSettings = new DeviceSettings { ActiveProfileId = savedProfileId },
        };

        var camera = new CountingCameraProvider("test-camera");
        var mic = new CountingAudioInputProvider("test-mic");
        var speaker = new CountingAudioOutputProvider("test-speaker");
        var buttons = new CountingButtonProvider();
        var buttonMappings = new CountingButtonMappingStore();

        var cameraManager = new CameraManager(
            [camera],
            settings,
            new DefaultCameraSelector(),
            NullLogger<CameraManager>.Instance);
        var audioInputManager = new AudioInputManager(
            [mic],
            settings,
            NullLogger<AudioInputManager>.Instance);
        var audioOutputManager = new AudioOutputManager(
            [speaker],
            settings,
            new AppSettings(),
            NullLogger<AudioOutputManager>.Instance);
        var buttonInputManager = new ButtonInputManager(
            [buttons],
            new ActionMap(),
            NullLogger<ButtonInputManager>.Instance);

        profiles ??=
        [
            new StubSourceProfile
            {
                Id = "custom",
                DisplayName = "Custom",
                Order = 100,
                FallbackPriority = 0,
            },
        ];

        var sourceProfileManager = new SourceProfileManager(
            profiles,
            cameraManager,
            audioInputManager,
            audioOutputManager,
            settings,
            NullLogger<SourceProfileManager>.Instance);

        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        var serviceProvider = services.BuildServiceProvider();

        var coordinator = new AppRuntimeCoordinator(
            cameraManager,
            audioInputManager,
            audioOutputManager,
            buttonInputManager,
            buttonMappings,
            sourceProfileManager,
            serviceProvider,
            NullLogger<AppRuntimeCoordinator>.Instance);

        return new RuntimeFixture(
            coordinator,
            settings,
            cameraManager,
            audioInputManager,
            audioOutputManager,
            sourceProfileManager,
            buttonMappings,
            camera,
            mic,
            speaker,
            buttons,
            profiles);
    }

    private static HeyCyanGlassesDeviceManager CreateGlassesManager()
    {
        var session = new FakeHeyCyanSessionWithVersion();
        var transfer = new FakeHeyCyanMediaTransfer();
        var btInput = new FakeBluetoothAudioInputProvider(["AA:BB:CC:DD:EE:FF"]);
        var btOutput = new FakeBluetoothAudioOutputProvider(["AA:BB:CC:DD:EE:FF"]);
        var camera = new HeyCyanCameraProvider(
            session,
            transfer,
            NullLogger<HeyCyanCameraProvider>.Instance,
            photoSettleDelay: TimeSpan.Zero);
        var mic = new HeyCyanAudioInputProvider(
            session,
            btInput,
            NullLogger<HeyCyanAudioInputProvider>.Instance);
        var speaker = new HeyCyanAudioOutputProvider(
            session,
            btOutput,
            NullLogger<HeyCyanAudioOutputProvider>.Instance);
        var button = new HeyCyanButtonProvider(
            session,
            NullLogger<HeyCyanButtonProvider>.Instance);

        return new HeyCyanGlassesDeviceManager(
            session,
            camera,
            mic,
            speaker,
            button,
            transfer,
            new FakeSettingsService(),
            NullLogger<HeyCyanGlassesDeviceManager>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed record RuntimeFixture(
        AppRuntimeCoordinator Coordinator,
        FakeSettingsService Settings,
        CameraManager CameraManager,
        AudioInputManager AudioInputManager,
        AudioOutputManager AudioOutputManager,
        SourceProfileManager SourceProfileManager,
        CountingButtonMappingStore ButtonMappingStore,
        CountingCameraProvider Camera,
        CountingAudioInputProvider Mic,
        CountingAudioOutputProvider Speaker,
        CountingButtonProvider Buttons,
        IReadOnlyList<StubSourceProfile> Profiles);

    private sealed class StubSourceProfile : ISourceProfile
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public int Order { get; init; }
        public bool IsAvailable { get; set; } = true;
        public string? UnavailableReason => IsAvailable ? null : "(offline)";
        public int FallbackPriority { get; init; }
        public int ApplyCount { get; private set; }

        public Task ApplyAsync(
            CameraManager camera,
            AudioInputManager mic,
            AudioOutputManager speaker,
            CancellationToken ct = default)
        {
            ApplyCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingButtonMappingStore : IButtonMappingStore
    {
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }

        public ButtonAction Get(string providerId, string buttonId, ButtonGesture gesture)
            => ButtonAction.None;

        public void Set(string providerId, string buttonId, ButtonGesture gesture, ButtonAction action)
        {
        }

        public void Clear(string providerId, string buttonId, ButtonGesture gesture)
        {
        }

        public Task LoadAsync()
        {
            LoadCount++;
            return Task.CompletedTask;
        }

        public Task SaveAsync()
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingCameraProvider : ICameraProvider
    {
        public CountingCameraProvider(string providerId) => ProviderId = providerId;

        public string DisplayName => "Counting Camera";
        public string ProviderId { get; }
        public bool IsAvailable => true;
        public bool SupportsVideoRecording => false;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public event EventHandler? Disconnected;

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
            => Task.FromResult<byte[]?>([]);

        public async IAsyncEnumerable<byte[]> StreamFramesAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingAudioInputProvider : IAudioInputProvider
    {
        public CountingAudioInputProvider(string providerId) => ProviderId = providerId;

        public string DisplayName => "Counting Mic";
        public string ProviderId { get; }
        public AudioInputCapabilities InputCapabilities => AudioInputCapabilities.Default;
        public bool IsAvailable => true;
        public bool IsCapturing { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public event EventHandler<byte[]>? AudioChunkAvailable;
        public event EventHandler? Disconnected;

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCount++;
            IsCapturing = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            IsCapturing = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingAudioOutputProvider : IAudioOutputProvider
    {
        public CountingAudioOutputProvider(string providerId) => ProviderId = providerId;

        public string DisplayName => "Counting Speaker";
        public string ProviderId { get; }
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public int SampleRate { get; private set; }
        public int EstimatedOutputLatencyMs => 50;
        public AudioOutputCapabilities OutputCapabilities => AudioOutputCapabilities.Unknown(EstimatedOutputLatencyMs);
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public event EventHandler? Disconnected;
        public event EventHandler? OutputRouteChanged;

        public Task StartAsync(int sampleRate, CancellationToken ct = default)
        {
            StartCount++;
            SampleRate = sampleRate;
            IsPlaying = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            IsPlaying = false;
            return Task.CompletedTask;
        }

        public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
            => Task.CompletedTask;

        public void ClearBuffer()
        {
        }

        public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingButtonProvider : IButtonInputProvider
    {
        public string DisplayName => "Counting Buttons";
        public string ProviderId => "test-buttons";
        public bool IsAvailable => true;
        public bool IsActive { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public event EventHandler<RawButtonEvent>? RawButtonEvent;
        public event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;
        public event EventHandler? Disconnected;

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCount++;
            IsActive = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            IsActive = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
