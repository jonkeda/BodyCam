using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Audio.WebRtcApm;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.Services.Audio;

public sealed class AudioOutputManagerAecTests
{
    [Fact]
    public async Task PlayChunkAsync_FeedsRenderReferenceBeforeProviderPlayback_WhenProviderNeedsAec()
    {
        var order = new List<string>();
        var provider = new OrderedOutputProvider(
            DirectSpeaker(),
            onPlay: () => order.Add("provider"));
        var aec = Substitute.For<IAecProcessor>();
        aec.When(x => x.FeedRenderReference(Arg.Any<byte[]>()))
            .Do(_ => order.Add("aec"));
        var manager = CreateManager(provider, aec, enableJitterBuffer: false);
        var chunk = new byte[] { 1, 2, 3, 4 };

        await manager.SetActiveAsync(provider.ProviderId);
        await manager.StartAsync();
        await manager.PlayChunkAsync(chunk);

        order.Should().Equal("aec", "provider");
        aec.Received(1).FeedRenderReference(Arg.Is<byte[]>(bytes => bytes.SequenceEqual(chunk)));
    }

    [Fact]
    public async Task PlayChunkAsync_DoesNotFeedRenderReference_WhenProviderIsAcousticallyIsolated()
    {
        var provider = new OrderedOutputProvider(Headset());
        var aec = Substitute.For<IAecProcessor>();
        var manager = CreateManager(provider, aec, enableJitterBuffer: false);

        await manager.SetActiveAsync(provider.ProviderId);
        await manager.StartAsync();
        await manager.PlayChunkAsync(new byte[] { 1, 2, 3, 4 });

        aec.DidNotReceive().FeedRenderReference(Arg.Any<byte[]>());
    }

    [Fact]
    public async Task StartAsync_RefreshesAecDelayAfterProviderStarts()
    {
        var provider = new StartSensitiveLatencyProvider();
        var aec = Substitute.For<IAecProcessor>();
        var manager = CreateManager(provider, aec, enableJitterBuffer: false);

        await manager.SetActiveAsync(provider.ProviderId);
        await manager.StartAsync();

        aec.Received().UpdateStreamDelay(80);
        aec.Received().UpdateStreamDelay(200);
    }

    private static AudioOutputManager CreateManager(
        IAudioOutputProvider provider,
        IAecProcessor aec,
        bool enableJitterBuffer)
    {
        var settings = Substitute.For<ISettingsService>();
        return new AudioOutputManager(
            [provider],
            settings,
            new AppSettings { EnableJitterBuffer = enableJitterBuffer },
            NullLogger<AudioOutputManager>.Instance,
            aec);
    }

    private static AudioOutputCapabilities DirectSpeaker(int latencyMs = 80) => new(
        EchoPathKind.DirectDeviceSpeaker,
        NeedsEchoCancellation: true,
        IsAcousticallyIsolated: false,
        SupportsRenderReference: true,
        EstimatedOutputLatencyMs: latencyMs);

    private static AudioOutputCapabilities Headset(int latencyMs = 40) => new(
        EchoPathKind.IsolatedHeadset,
        NeedsEchoCancellation: false,
        IsAcousticallyIsolated: true,
        SupportsRenderReference: false,
        EstimatedOutputLatencyMs: latencyMs);

    private sealed class OrderedOutputProvider : IAudioOutputProvider
    {
        private readonly Action? _onPlay;

        public OrderedOutputProvider(AudioOutputCapabilities outputCapabilities, Action? onPlay = null)
        {
            OutputCapabilities = outputCapabilities;
            _onPlay = onPlay;
        }

        public string DisplayName => "Ordered output";
        public string ProviderId => "ordered-output";
        public AudioOutputCapabilities OutputCapabilities { get; }
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public int EstimatedOutputLatencyMs => OutputCapabilities.EstimatedOutputLatencyMs;
        public event EventHandler? OutputRouteChanged;
        public event EventHandler? Disconnected;

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

        public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
        {
            _onPlay?.Invoke();
            return Task.CompletedTask;
        }

        public void ClearBuffer()
        {
        }

        public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StartSensitiveLatencyProvider : IAudioOutputProvider
    {
        public string DisplayName => "Start-sensitive output";
        public string ProviderId => "start-sensitive-output";
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public int EstimatedOutputLatencyMs => IsPlaying ? 200 : 80;
        public AudioOutputCapabilities OutputCapabilities => DirectSpeaker(EstimatedOutputLatencyMs);
        public event EventHandler? OutputRouteChanged;
        public event EventHandler? Disconnected;

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

        public void ClearBuffer()
        {
        }

        public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
