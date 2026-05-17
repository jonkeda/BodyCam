using AVFoundation;
using BodyCam.Services.Audio;
using Foundation;
using Microsoft.Extensions.Logging;

namespace BodyCam.Platforms.iOS;

/// <summary>
/// iOS speaker provider using AVAudioEngine with AVAudioPlayerNode.
/// Shares the same engine with PlatformMicProvider so VoiceProcessingIO can correlate mic+speaker.
/// </summary>
public sealed class PhoneSpeakerProvider : IAudioOutputProvider, IDisposable
{
    private readonly AVAudioEngine _engine;
    private readonly ILogger<PhoneSpeakerProvider> _logger;
    private readonly object _lock = new();
    private AVAudioPlayerNode? _playerNode;
    private int _sampleRate;
    
    // Phase 5.4: Track recent output for fade-out
    private readonly Queue<byte> _recentSamples = new();
    private const int MaxRecentSamplesMs = 50;

    public string DisplayName => "iPhone Speaker";
    public string ProviderId => "phone-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }

    public int EstimatedOutputLatencyMs
    {
        get
        {
            // iOS built-in speaker latency: ~25-40ms typical
            var session = AVAudioSession.SharedInstance();
            double latency = session.OutputLatency + session.IOBufferDuration;
            return (int)(latency * 1000) + 10; // Add 10ms for safety margin
        }
    }

    public event EventHandler? Disconnected;
    public event EventHandler? OutputRouteChanged;

    public PhoneSpeakerProvider(AVAudioEngine engine, ILogger<PhoneSpeakerProvider> logger)
    {
        _engine = engine;
        _logger = logger;

        // Subscribe to route change notifications
        NSNotificationCenter.DefaultCenter.AddObserver(
            AVAudioSession.RouteChangeNotification,
            OnRouteChange);
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        _sampleRate = sampleRate;

        // Create player node
        _playerNode = new AVAudioPlayerNode();
        _engine.AttachNode(_playerNode);

        // Connect to main mixer
        var format = new AVAudioFormat(sampleRate, 1); // Mono
        _engine.Connect(_playerNode, _engine.MainMixerNode, format);

        // Engine should already be running if mic is active; if not, start it
        if (!_engine.IsRunning)
        {
            _engine.Prepare();
            NSError? error;
            bool started = _engine.StartAndReturnError(out error);
            if (!started || error != null)
            {
                _logger.LogError("Failed to start AVAudioEngine for output: {Error}", error?.LocalizedDescription);
                throw new InvalidOperationException($"Failed to start audio engine: {error?.LocalizedDescription}");
            }
        }

        _playerNode.Play();
        IsPlaying = true;
        _logger.LogInformation("iOS speaker started at {SampleRate}Hz", sampleRate);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;

        _playerNode?.Stop();
        if (_playerNode != null)
        {
            _engine.DisconnectNodeOutput(_playerNode);
            _engine.DetachNode(_playerNode);
            _playerNode.Dispose();
            _playerNode = null;
        }

        IsPlaying = false;
        _logger.LogInformation("iOS speaker stopped");
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_playerNode == null || !IsPlaying) return Task.CompletedTask;

        // Phase 5.4: Track recent samples for fade-out
        lock (_lock)
        {
            foreach (byte b in pcmData)
                _recentSamples.Enqueue(b);

            int maxRecentBytes = _sampleRate * 2 * MaxRecentSamplesMs / 1000;
            while (_recentSamples.Count > maxRecentBytes)
                _recentSamples.Dequeue();
        }

        // Convert PCM16 to AVAudioPCMBuffer
        int frameCount = pcmData.Length / 2;
        var format = new AVAudioFormat(_sampleRate, 1); // Mono
        var buffer = new AVAudioPcmBuffer(format, (uint)frameCount);
        if (buffer == null) return Task.CompletedTask;

        buffer.FrameLength = (uint)frameCount;

        unsafe
        {
            float* samples = (float*)buffer.FloatChannelData[0].ToPointer();
            for (int i = 0; i < frameCount; i++)
            {
                short pcm = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
                samples[i] = pcm / 32768f;
            }
        }

        _playerNode.ScheduleBuffer(buffer, null);
        return Task.CompletedTask;
    }

    public void ClearBuffer()
    {
        _playerNode?.Stop();
        _playerNode?.Play();
    }

    /// <summary>
    /// Phase 5.4: Fade out the last chunk to prevent audible click on interruption.
    /// </summary>
    public async Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
    {
        if (_playerNode is null || !IsPlaying)
        {
            ClearBuffer();
            return;
        }

        byte[] fadeChunk;
        lock (_lock)
        {
            if (_recentSamples.Count == 0)
            {
                ClearBuffer();
                return;
            }

            // Take up to fadeMs worth of recent samples
            int fadeSamples = Math.Min(_sampleRate * fadeMs / 1000, _recentSamples.Count / 2);
            int fadeBytes = fadeSamples * 2;
            fadeChunk = _recentSamples.TakeLast(fadeBytes).ToArray();
        }

        // Apply linear fade-out
        for (int i = 0; i < fadeChunk.Length / 2; i++)
        {
            short sample = BitConverter.ToInt16(fadeChunk, i * 2);
            float gain = 1.0f - ((float)i / (fadeChunk.Length / 2));
            short faded = (short)(sample * gain);
            BitConverter.TryWriteBytes(fadeChunk.AsSpan(i * 2), faded);
        }

        // Schedule the fade chunk
        int frameCount = fadeChunk.Length / 2;
        var format = new AVAudioFormat(_sampleRate, 1);
        var buffer = new AVAudioPcmBuffer(format, (uint)frameCount);
        if (buffer != null)
        {
            buffer.FrameLength = (uint)frameCount;
            unsafe
            {
                float* samples = (float*)buffer.FloatChannelData[0].ToPointer();
                for (int i = 0; i < frameCount; i++)
                {
                    short pcm = BitConverter.ToInt16(fadeChunk, i * 2);
                    samples[i] = pcm / 32768f;
                }
            }
            _playerNode.ScheduleBuffer(buffer, null);
        }

        await Task.Delay(fadeMs, ct);

        // Now clear
        ClearBuffer();
    }

    private void OnRouteChange(NSNotification notification)
    {
        _logger.LogInformation("Audio route changed");
        OutputRouteChanged?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        NSNotificationCenter.DefaultCenter.RemoveObserver(this);
        if (IsPlaying)
        {
            _ = StopAsync();
        }
    }
}
