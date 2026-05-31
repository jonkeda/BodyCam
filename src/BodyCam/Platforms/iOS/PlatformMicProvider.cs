using AudioToolbox;
using AVFoundation;
using BodyCam.Services;
using BodyCam.Services.Audio;
using Foundation;
using Microsoft.Extensions.Logging;

namespace BodyCam.Platforms.iOS;

/// <summary>
/// iOS microphone provider using AVAudioEngine with VoiceProcessingIO.
/// VoiceProcessingIO provides hardware-accelerated AEC, NS, and AGC.
/// </summary>
public sealed class PlatformMicProvider : IAudioInputProvider, IDisposable
{
    private readonly AppSettings _settings;
    private readonly ILogger<PlatformMicProvider> _logger;
    private readonly AVAudioEngine _engine;
    private bool _isSessionConfigured;

    public string DisplayName => "iPhone Microphone";
    public string ProviderId => "platform";
    public AudioInputCapabilities InputCapabilities => new(
        HasPlatformEchoCancellation: _settings.IosUsePlatformAecOnly,
        PlatformEchoCancellationActive: _settings.IosUsePlatformAecOnly,
        EstimatedInputLatencyMs: 0);
    public bool IsAvailable => true;
    public bool IsCapturing { get; private set; }

    /// <summary>True if VoiceProcessingIO is active (provides native AEC).</summary>
    public bool HasPlatformAec => IsCapturing && _settings.IosUsePlatformAecOnly;

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public PlatformMicProvider(AppSettings settings, AVAudioEngine engine, ILogger<PlatformMicProvider> logger)
    {
        _settings = settings;
        _engine = engine;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return;

        await ConfigureAudioSessionAsync();

        var inputNode = _engine.InputNode;
        var format = inputNode.GetBusOutputFormat(0);

        // Enable VoiceProcessingIO for hardware AEC
        if (_settings.IosUsePlatformAecOnly)
        {
            bool success = inputNode.SetVoiceProcessingEnabled(true, out NSError? vpError);
            if (!success || vpError != null)
            {
                _logger.LogWarning("Failed to enable VoiceProcessingIO: {Error}", vpError?.LocalizedDescription);
            }
            else
            {
                _logger.LogInformation("VoiceProcessingIO enabled for hardware AEC");
            }
        }

        // Calculate frame size for the target chunk duration
        uint frameLength = (uint)(_settings.SampleRate * _settings.ChunkDurationMs / 1000);

        inputNode.InstallTapOnBus(0, frameLength, format, (buffer, when) =>
        {
            if (buffer == null) return;

            // Convert AudioBufferList to PCM16
            var audioBuffer = buffer.AudioBufferList;
            if (audioBuffer.Count == 0) return;

            var firstBuffer = audioBuffer[0];
            int frameCount = (int)buffer.FrameLength;
            int sampleRate = (int)format.SampleRate;

            // Resample if hardware rate differs from target
            byte[] pcm16Data;
            if (sampleRate == _settings.SampleRate)
            {
                pcm16Data = ConvertToMono16(firstBuffer, frameCount);
            }
            else
            {
                // TODO: Use PolyphaseFirResampler if sampleRate != _settings.SampleRate
                // For now, assume 48k matches
                pcm16Data = ConvertToMono16(firstBuffer, frameCount);
                _logger.LogWarning("Sample rate mismatch: {Actual} vs {Expected}", sampleRate, _settings.SampleRate);
            }

            AudioChunkAvailable?.Invoke(this, pcm16Data);
        });

        _engine.Prepare();
        NSError? startError;
        bool started = _engine.StartAndReturnError(out startError);
        if (!started || startError != null)
        {
            _logger.LogError("Failed to start AVAudioEngine: {Error}", startError?.LocalizedDescription);
            throw new InvalidOperationException($"Failed to start audio engine: {startError?.LocalizedDescription}");
        }

        IsCapturing = true;
        _logger.LogInformation("iOS mic started (VoiceProcessingIO={Enabled})", _settings.IosUsePlatformAecOnly);
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;

        _engine.InputNode.RemoveTapOnBus(0);
        _engine.Stop();
        IsCapturing = false;
        _logger.LogInformation("iOS mic stopped");
        return Task.CompletedTask;
    }

    private async Task ConfigureAudioSessionAsync()
    {
        if (_isSessionConfigured) return;

        var session = AVAudioSession.SharedInstance();
        NSError? error;

        // .playAndRecord allows simultaneous input and output
        // .voiceChat mode engages the VoiceProcessingIO audio unit
        session.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DuckOthers | AVAudioSessionCategoryOptions.DefaultToSpeaker, out error);
        if (error != null)
        {
            _logger.LogWarning("SetCategory error: {Error}", error.LocalizedDescription);
        }

        session.SetMode(AVAudioSession.ModeVoiceChat, out error);
        if (error != null)
        {
            _logger.LogWarning("SetMode error: {Error}", error.LocalizedDescription);
        }

        session.SetActive(true, out error);
        if (error != null)
        {
            _logger.LogWarning("SetActive error: {Error}", error.LocalizedDescription);
        }

        _isSessionConfigured = true;
        _logger.LogInformation("iOS audio session configured (category=PlayAndRecord, mode=VoiceChat)");
    }

    private static byte[] ConvertToMono16(AudioBuffer buffer, int frameCount)
    {
        // AVFoundation typically provides float samples; convert to PCM16
        var pcm16 = new byte[frameCount * 2];
        unsafe
        {
            float* samples = (float*)buffer.Data.ToPointer();
            for (int i = 0; i < frameCount; i++)
            {
                float sample = samples[i];
                short pcm = (short)Math.Clamp(sample * 32768f, -32768, 32767);
                pcm16[i * 2] = (byte)(pcm & 0xFF);
                pcm16[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
            }
        }
        return pcm16;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (IsCapturing)
        {
            _ = StopAsync();
        }
    }
}
