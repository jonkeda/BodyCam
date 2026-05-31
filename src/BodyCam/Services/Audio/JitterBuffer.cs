using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio;

/// <summary>
/// Adaptive jitter buffer for audio output. Absorbs irregular delivery cadence
/// from the Realtime API by maintaining a target depth (40–200 ms). Grows on
/// underrun, shrinks on overflow, with cooldown to prevent oscillation.
/// </summary>
public sealed class JitterBuffer : IDisposable
{
    private const int MinDepthMs = 40;
    private const int MaxDepthMs = 200;
    private const int StepMs = 50;
    private const int CooldownSec = 5;

    private readonly Channel<byte[]> _queue;
    private readonly ILogger<JitterBuffer> _logger;
    private int _targetDepthMs = MinDepthMs;
    private DateTime _lastChange = DateTime.MinValue;
    private long _underruns, _overflows;
    private volatile bool _disposed;

    public int CurrentTargetMs => _targetDepthMs;
    public long Underruns => Interlocked.Read(ref _underruns);
    public long Overflows => Interlocked.Read(ref _overflows);

    public JitterBuffer(ILogger<JitterBuffer> logger)
    {
        _logger = logger;
        _queue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Enqueue a PCM chunk for buffered playback.
    /// </summary>
    public async ValueTask EnqueueAsync(byte[] pcm, CancellationToken ct = default)
    {
        if (_disposed) return;
        await _queue.Writer.WriteAsync(pcm, ct);
    }

    /// <summary>
    /// Drain the jitter buffer to the provider, monitoring fill level and
    /// adapting target depth on underrun/overflow events.
    /// </summary>
    public async Task DrainToProviderAsync(
        IAudioOutputProvider provider,
        int sampleRate,
        Action<byte[]>? beforePlayChunk,
        CancellationToken ct)
    {
        var reader = _queue.Reader;
        var chunks = new Queue<byte[]>();
        int bytesPerMs = sampleRate * 2 / 1000; // PCM16 mono

        while (!ct.IsCancellationRequested && !_disposed)
        {
            // Initial fill: wait until we have at least targetDepthMs of audio
            int targetBytes = _targetDepthMs * bytesPerMs;
            int currentBytes = chunks.Sum(c => c.Length);

            while (currentBytes < targetBytes && !_disposed)
            {
                if (await reader.WaitToReadAsync(ct))
                {
                    if (reader.TryRead(out var chunk))
                    {
                        chunks.Enqueue(chunk);
                        currentBytes += chunk.Length;
                    }
                }
                else
                {
                    // Channel closed
                    return;
                }
            }

            // Play chunks as they're available
            while (chunks.Count > 0 && !_disposed && !ct.IsCancellationRequested)
            {
                var chunk = chunks.Dequeue();
                beforePlayChunk?.Invoke(chunk);
                await provider.PlayChunkAsync(chunk, ct);

                currentBytes -= chunk.Length;

                // Check for underrun: queue empty when we expected data
                if (chunks.Count == 0 && reader.TryRead(out var nextChunk))
                {
                    chunks.Enqueue(nextChunk);
                    currentBytes += nextChunk.Length;
                }
                else if (chunks.Count == 0)
                {
                    // Underrun detected
                    OnUnderrun();
                    // Wait for more data
                    if (await reader.WaitToReadAsync(ct))
                    {
                        if (reader.TryRead(out var refillChunk))
                        {
                            chunks.Enqueue(refillChunk);
                            currentBytes += refillChunk.Length;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                // Check for overflow: buffered audio exceeds 2× target
                if (currentBytes > targetBytes * 2)
                {
                    OnOverflow();
                }

                // Opportunistically read more chunks without blocking
                while (reader.TryRead(out var extraChunk))
                {
                    chunks.Enqueue(extraChunk);
                    currentBytes += extraChunk.Length;
                }
            }
        }
    }

    public Task DrainToProviderAsync(IAudioOutputProvider provider, int sampleRate, CancellationToken ct)
        => DrainToProviderAsync(provider, sampleRate, beforePlayChunk: null, ct);

    /// <summary>
    /// Clear all buffered audio and reset adaptive target to minimum.
    /// Call on interruption or route change.
    /// </summary>
    public void Clear()
    {
        while (_queue.Reader.TryRead(out _)) { }
        _targetDepthMs = MinDepthMs;
        _lastChange = DateTime.UtcNow;
        _logger.LogDebug("Jitter buffer cleared, target reset to {TargetMs}ms", MinDepthMs);
    }

    private void OnUnderrun()
    {
        Interlocked.Increment(ref _underruns);

        if (DateTime.UtcNow - _lastChange < TimeSpan.FromSeconds(CooldownSec))
            return;

        if (_targetDepthMs < MaxDepthMs)
        {
            _targetDepthMs = Math.Min(_targetDepthMs + StepMs, MaxDepthMs);
            _lastChange = DateTime.UtcNow;
            _logger.LogInformation("Jitter buffer underrun, grew to {TargetMs}ms", _targetDepthMs);
        }
    }

    private void OnOverflow()
    {
        Interlocked.Increment(ref _overflows);

        if (DateTime.UtcNow - _lastChange < TimeSpan.FromSeconds(CooldownSec))
            return;

        if (_targetDepthMs > MinDepthMs)
        {
            _targetDepthMs = Math.Max(_targetDepthMs - StepMs, MinDepthMs);
            _lastChange = DateTime.UtcNow;
            _logger.LogInformation("Jitter buffer overflow, shrunk to {TargetMs}ms", _targetDepthMs);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _queue.Writer.TryComplete();
    }
}
