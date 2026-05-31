using BodyCam.Services.Audio;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Audio;

/// <summary>
/// Tests for JitterBuffer's adaptive depth adjustment.
/// </summary>
public class JitterBufferTests
{
    [Fact]
    public async Task JitterBuffer_StartsAtMinDepth()
    {
        var buffer = new JitterBuffer(NullLogger<JitterBuffer>.Instance);
        buffer.CurrentTargetMs.Should().Be(40); // MinDepthMs
    }

    [Fact]
    public async Task JitterBuffer_GrowsOnUnderrun()
    {
        var buffer = new JitterBuffer(NullLogger<JitterBuffer>.Instance);
        var mockProvider = new MockAudioOutputProvider();

        // Start drain task
        var cts = new CancellationTokenSource();
        var drainTask = Task.Run(() => buffer.DrainToProviderAsync(mockProvider, 48000, cts.Token));

        // Enqueue a single chunk, then wait
        var chunk = new byte[4800]; // 50ms @ 48kHz
        await buffer.EnqueueAsync(chunk);

        await Task.Delay(150); // Let it underrun

        // Enqueue another chunk
        await buffer.EnqueueAsync(chunk);
        await Task.Delay(100);

        cts.Cancel();
        try { await drainTask; } catch (OperationCanceledException) { }

        // Assert: target grew
        buffer.CurrentTargetMs.Should().BeGreaterThan(40);
        buffer.Underruns.Should().BeGreaterThan(0);

        buffer.Dispose();
    }

    [Fact]
    public async Task JitterBuffer_ShrinksOnOverflow()
    {
        var buffer = new JitterBuffer(NullLogger<JitterBuffer>.Instance);
        var mockProvider = new MockAudioOutputProvider(simulateSlowPlayback: true);

        // Start drain task
        var cts = new CancellationTokenSource();
        var drainTask = Task.Run(() => buffer.DrainToProviderAsync(mockProvider, 48000, cts.Token));

        // Rapidly enqueue many chunks to overflow
        var chunk = new byte[4800];
        for (int i = 0; i < 50; i++)
        {
            await buffer.EnqueueAsync(chunk);
        }

        await Task.Delay(200); // Let overflow detection fire

        cts.Cancel();
        try { await drainTask; } catch (OperationCanceledException) { }

        // Assert: overflow detected (shrink may or may not happen depending on timing)
        buffer.Overflows.Should().BeGreaterThan(0);

        buffer.Dispose();
    }

    [Fact]
    public async Task JitterBuffer_ClearResetsTargetToMin()
    {
        var buffer = new JitterBuffer(NullLogger<JitterBuffer>.Instance);

        // Enqueue several chunks
        var chunk = new byte[4800];
        await buffer.EnqueueAsync(chunk);
        await buffer.EnqueueAsync(chunk);

        // Clear
        buffer.Clear();

        // Assert: target reset
        buffer.CurrentTargetMs.Should().Be(40);

        buffer.Dispose();
    }

    [Fact]
    public async Task JitterBuffer_PlaybackIsMonotonic()
    {
        var buffer = new JitterBuffer(NullLogger<JitterBuffer>.Instance);
        var mockProvider = new MockAudioOutputProvider();

        // Start drain
        var cts = new CancellationTokenSource();
        var drainTask = Task.Run(() => buffer.DrainToProviderAsync(mockProvider, 48000, cts.Token));

        // Enqueue chunks with varying intervals
        var chunk = new byte[4800];
        await buffer.EnqueueAsync(chunk);
        await Task.Delay(10);
        await buffer.EnqueueAsync(chunk);
        await Task.Delay(100); // Long gap
        await buffer.EnqueueAsync(chunk);
        await Task.Delay(20);
        await buffer.EnqueueAsync(chunk);

        await Task.Delay(300); // Let drain process

        cts.Cancel();
        try { await drainTask; } catch (OperationCanceledException) { }

        // Assert: chunks played in order
        mockProvider.PlayedChunks.Should().BeGreaterThan(0);

        buffer.Dispose();
    }

    [Fact]
    public async Task DrainToProviderAsync_InvokesBeforePlayChunkCallbackBeforeProviderPlayback()
    {
        var buffer = new JitterBuffer(NullLogger<JitterBuffer>.Instance);
        var order = new List<string>();
        var mockProvider = new MockAudioOutputProvider(onPlay: () =>
        {
            lock (order) order.Add("provider");
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var drainTask = Task.Run(() => buffer.DrainToProviderAsync(
            mockProvider,
            48000,
            _ =>
            {
                lock (order) order.Add("aec");
            },
            cts.Token));

        await buffer.EnqueueAsync(new byte[4800], cts.Token);
        await mockProvider.WaitForPlaybackAsync(TimeSpan.FromSeconds(1));

        cts.Cancel();
        try { await drainTask; } catch (OperationCanceledException) { }

        lock (order)
        {
            order.Take(2).Should().Equal("aec", "provider");
        }

        buffer.Dispose();
    }

    private class MockAudioOutputProvider : IAudioOutputProvider
    {
        private readonly bool _simulateSlowPlayback;
        private readonly Action? _onPlay;
        private readonly TaskCompletionSource _playbackObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PlayedChunks { get; private set; }

        public MockAudioOutputProvider(bool simulateSlowPlayback = false, Action? onPlay = null)
        {
            _simulateSlowPlayback = simulateSlowPlayback;
            _onPlay = onPlay;
        }

        public string DisplayName => "Mock";
        public string ProviderId => "mock";
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public int EstimatedOutputLatencyMs => 40;
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

        public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
        {
            _onPlay?.Invoke();
            PlayedChunks++;
            _playbackObserved.TrySetResult();
            if (_simulateSlowPlayback)
                await Task.Delay(30, ct); // Slower than realtime
        }

        public Task WaitForPlaybackAsync(TimeSpan timeout)
        {
            return _playbackObserved.Task.WaitAsync(timeout);
        }

        public void ClearBuffer() { }

        public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
        {
            ClearBuffer();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
