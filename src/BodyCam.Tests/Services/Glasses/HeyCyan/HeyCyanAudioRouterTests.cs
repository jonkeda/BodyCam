using BodyCam.Services.Audio;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// Unit tests for <see cref="HeyCyanAudioRouter"/>.
/// </summary>
public sealed class HeyCyanAudioRouterTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = new();

    [Fact]
    public async Task Connected_RoutesBothManagers()
    {
        var (session, input, output, router) = CreateTestRouter();

        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await Task.Delay(20); // Let async-void handler complete

        input.ActiveProviderId.Should().Be("heycyan-glasses");
        output.ActiveProviderId.Should().Be("heycyan-glasses");
    }

    [Fact]
    public async Task Disconnected_RestoresPreviousProviders()
    {
        var (session, input, output, router) = CreateTestRouter();

        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await Task.Delay(20);

        session.RaiseDisconnected();
        await Task.Delay(20);

        input.ActiveProviderId.Should().Be("platform");
        output.ActiveProviderId.Should().Be("platform");
    }

    [Fact]
    public async Task TransferMode_DoesNotFlipRouting()
    {
        var (session, input, output, router) = CreateTestRouter();

        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await Task.Delay(20);

        session.RaiseTransferMode();
        await Task.Delay(20);

        input.ActiveProviderId.Should().Be("heycyan-glasses", "transfer mode should not restore previous provider");
        output.ActiveProviderId.Should().Be("heycyan-glasses", "transfer mode should not restore previous provider");
    }

    [Fact]
    public async Task RapidToggle_DoesNotLeakSnapshotIntoHeyCyan()
    {
        var (session, input, output, router) = CreateTestRouter();

        // Rapidly toggle connect/disconnect 25 times
        for (var i = 0; i < 25; i++)
        {
            session.RaiseConnected("AA:BB:CC:DD:EE:FF");
            await Task.Delay(2);
            session.RaiseDisconnected();
            await Task.Delay(2);
        }

        // Give final disconnect time to process
        await Task.Delay(20);

        input.ActiveProviderId.Should().Be("platform", "should end on platform after final disconnect");
        output.ActiveProviderId.Should().Be("platform", "should end on platform after final disconnect");
    }

    [Fact]
    public async Task ConnectDisconnectConnect_CapturesNewSnapshot()
    {
        var (session, input, output, router) = CreateTestRouter();

        // First connection: snapshot "platform"
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await Task.Delay(20);
        input.ActiveProviderId.Should().Be("heycyan-glasses");

        // Disconnect: restore "platform"
        session.RaiseDisconnected();
        await Task.Delay(20);
        input.ActiveProviderId.Should().Be("platform");

        // Manually change to a different provider (simulating user choice)
        await input.SetActiveProviderAsync("usb-mic");
        await output.SetActiveProviderAsync("usb-speaker");

        // Second connection: snapshot "usb-mic" / "usb-speaker"
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await Task.Delay(20);
        input.ActiveProviderId.Should().Be("heycyan-glasses");

        // Disconnect: should restore "usb-mic" / "usb-speaker"
        session.RaiseDisconnected();
        await Task.Delay(20);
        input.ActiveProviderId.Should().Be("usb-mic");
        output.ActiveProviderId.Should().Be("usb-speaker");
    }

    [Fact]
    public async Task DisposeAsync_UnsubscribesFromSession()
    {
        var (session, input, output, router) = CreateTestRouter();

        await router.DisposeAsync();

        // After dispose, state changes should not trigger routing
        session.RaiseConnected("AA:BB:CC:DD:EE:FF");
        await Task.Delay(20);

        input.ActiveProviderId.Should().Be("platform", "dispose should prevent routing");
        output.ActiveProviderId.Should().Be("platform", "dispose should prevent routing");
    }

    private (FakeHeyCyanSession session, AudioInputManager input, AudioOutputManager output, HeyCyanAudioRouter router)
        CreateTestRouter()
    {
        var session = new FakeHeyCyanSession();
        var settings = new FakeSettingsService { ActiveAudioInputProvider = "platform", ActiveAudioOutputProvider = "platform" };

        // Create stub providers for the managers
        var platformInput = new StubAudioInputProvider("platform");
        var heycyanInput = new StubAudioInputProvider("heycyan-glasses");
        var usbInput = new StubAudioInputProvider("usb-mic");

        var platformOutput = new StubAudioOutputProvider("platform");
        var heycyanOutput = new StubAudioOutputProvider("heycyan-glasses");
        var usbOutput = new StubAudioOutputProvider("usb-speaker");

        var input = new AudioInputManager(new[] { platformInput, heycyanInput, usbInput }, settings, NullLogger<AudioInputManager>.Instance);
        var output = new AudioOutputManager(new[] { platformOutput, heycyanOutput, usbOutput }, settings, new AppSettings(), NullLogger<AudioOutputManager>.Instance);

        // Initialize managers to "platform"
        _ = input.SetActiveProviderAsync("platform");
        _ = output.SetActiveProviderAsync("platform");

        var router = new HeyCyanAudioRouter(session, input, output, NullLogger<HeyCyanAudioRouter>.Instance);

        _disposables.Add(router);
        _disposables.Add(input);
        _disposables.Add(output);

        return (session, input, output, router);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }
    }

    /// <summary>
    /// Minimal stub audio input provider for router testing.
    /// </summary>
    private sealed class StubAudioInputProvider : IAudioInputProvider
    {
        public StubAudioInputProvider(string providerId) => ProviderId = providerId;

        public string DisplayName => ProviderId;
        public string ProviderId { get; }
        public bool IsAvailable => true;
        public bool IsCapturing { get; private set; }

        public event EventHandler<byte[]>? AudioChunkAvailable;
        public event EventHandler? Disconnected;

        public Task StartAsync(CancellationToken ct = default)
        {
            IsCapturing = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsCapturing = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>
    /// Minimal stub audio output provider for router testing.
    /// </summary>
    private sealed class StubAudioOutputProvider : IAudioOutputProvider
    {
        public StubAudioOutputProvider(string providerId) => ProviderId = providerId;

        public string DisplayName => ProviderId;
        public string ProviderId { get; }
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public int EstimatedOutputLatencyMs => 50;

        public event EventHandler? Disconnected;
        public event EventHandler? OutputRouteChanged;

        public Task StartAsync(int sampleRate, CancellationToken ct = default)
        {
            IsPlaying = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsPlaying = false;
            return Task.CompletedTask;
        }

        public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default) => Task.CompletedTask;
        public void ClearBuffer() { }
        public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
        {
            ClearBuffer();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => default;
    }
}
