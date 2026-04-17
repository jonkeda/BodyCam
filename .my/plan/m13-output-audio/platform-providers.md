# M13 — Platform Audio Output Providers

## Overview

Phase 1 wraps the existing platform-specific `IAudioOutputService` implementations
into `IAudioOutputProvider` providers. This is a mechanical refactoring — the audio
code itself doesn't change, it just moves behind the new abstraction.

---

## WindowsSpeakerProvider

Wraps the existing `WindowsAudioOutputService` (NAudio WaveOutEvent + BufferedWaveProvider).

```csharp
namespace BodyCam.Services.Audio;

#if WINDOWS
using NAudio.Wave;

/// <summary>
/// Audio output via the Windows default speaker using NAudio.
/// Wraps the existing WaveOutEvent + BufferedWaveProvider pattern.
/// </summary>
public class WindowsSpeakerProvider : IAudioOutputProvider, IDisposable
{
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _buffer;

    public string DisplayName => "Laptop Speaker";
    public string ProviderId => "windows-speaker";
    public string? DeviceId => null; // Default system output
    public bool IsAvailable => true; // Always available on Windows
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        var waveFormat = new WaveFormat(sampleRate, 16, 1);
        _buffer = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(30),
            DiscardOnBufferOverflow = false
        };

        _waveOut = new WaveOutEvent { DesiredLatency = 200 };
        _waveOut.Init(_buffer);
        _waveOut.Play();

        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;

        _waveOut?.Stop();
        _buffer?.ClearBuffer();
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_buffer is null || !IsPlaying) return;

        // Back-pressure: wait for the buffer to drain if it's nearly full.
        var maxFill = _buffer.BufferLength - pcmData.Length;
        while (_buffer.BufferedBytes > maxFill)
        {
            await Task.Delay(20, ct);
            if (_buffer is null || !IsPlaying) return;
        }

        _buffer.AddSamples(pcmData, 0, pcmData.Length);
    }

    public void ClearBuffer()
    {
        _buffer?.ClearBuffer();
    }

    public void Dispose()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _buffer = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
#endif
```

### Changes from `WindowsAudioOutputService`

| Aspect | Before | After |
|--------|--------|-------|
| Interface | `IAudioOutputService` | `IAudioOutputProvider` |
| Sample rate | Read from `AppSettings` | Passed to `StartAsync(sampleRate)` |
| Constructor | `AppSettings` injection | Parameterless |
| Dispose | `IDisposable` | `IDisposable` + `IAsyncDisposable` |
| Everything else | Identical | Identical |

---

## PhoneSpeakerProvider

Wraps the existing `AndroidAudioOutputService` (AudioTrack).

```csharp
namespace BodyCam.Services.Audio;

#if ANDROID
using Android.Media;

/// <summary>
/// Audio output via the Android phone speaker using AudioTrack.
/// Wraps the existing AudioTrack pattern with Media/Speech audio attributes.
/// </summary>
public class PhoneSpeakerProvider : IAudioOutputProvider, IDisposable
{
    private AudioTrack? _audioTrack;

    public string DisplayName => "Phone Speaker";
    public string ProviderId => "phone-speaker";
    public string? DeviceId => null;
    public bool IsAvailable => true; // Always available on Android
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        int bufferSize = AudioTrack.GetMinBufferSize(
            sampleRate,
            ChannelOut.Mono,
            Encoding.Pcm16bit);

        _audioTrack = new AudioTrack(
            new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Speech)!
                .Build()!,
            new AudioFormat.Builder()
                .SetSampleRate(sampleRate)!
                .SetChannelMask(ChannelOut.Mono)!
                .SetEncoding(Encoding.Pcm16bit)!
                .Build()!,
            bufferSize,
            AudioTrackMode.Stream,
            AudioManager.AudioSessionIdGenerate);

        _audioTrack.Play();
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;
        _audioTrack?.Stop();
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_audioTrack is null || !IsPlaying) return Task.CompletedTask;
        _audioTrack.Write(pcmData, 0, pcmData.Length);
        return Task.CompletedTask;
    }

    public void ClearBuffer()
    {
        _audioTrack?.Flush();
    }

    public void Dispose()
    {
        _audioTrack?.Stop();
        _audioTrack?.Release();
        _audioTrack = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
#endif
```

### Changes from `AndroidAudioOutputService`

| Aspect | Before | After |
|--------|--------|-------|
| Interface | `IAudioOutputService` | `IAudioOutputProvider` |
| Sample rate | Read from `AppSettings` | Passed to `StartAsync(sampleRate)` |
| Constructor | `AppSettings` injection | Parameterless |
| Everything else | Identical | Identical |

---

## StubAudioOutputProvider

Stub for unsupported platforms (iOS, macOS, etc.).

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// No-op audio output for unsupported platforms.
/// </summary>
public class StubAudioOutputProvider : IAudioOutputProvider
{
    public string DisplayName => "No Audio Output";
    public string ProviderId => "stub";
    public string? DeviceId => null;
    public bool IsAvailable => false;
    public bool IsPlaying { get; private set; }

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
        => Task.CompletedTask;

    public void ClearBuffer() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

---

## USB Audio Output Provider

USB speakers and headsets appear as standard audio output devices on both platforms.

### Windows (NAudio Device Selection)

On Windows, USB audio devices appear as WaveOut devices. NAudio enumerates them
with `WaveOut.DeviceCount` / `WaveOut.GetCapabilities(n)`.

```csharp
namespace BodyCam.Services.Audio;

#if WINDOWS
using NAudio.Wave;

/// <summary>
/// Audio output to a specific USB audio device on Windows.
/// Uses NAudio WaveOutEvent with explicit device selection.
/// </summary>
public class UsbAudioOutputProvider : IAudioOutputProvider, IDisposable
{
    private readonly int _deviceNumber;
    private readonly string _deviceName;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _buffer;

    public string DisplayName => $"USB: {_deviceName}";
    public string ProviderId => "usb-audio";
    public string? DeviceId { get; }
    public bool IsAvailable { get; private set; } = true;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public UsbAudioOutputProvider(int deviceNumber, string deviceName, string deviceId)
    {
        _deviceNumber = deviceNumber;
        _deviceName = deviceName;
        DeviceId = deviceId;
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        var waveFormat = new WaveFormat(sampleRate, 16, 1);
        _buffer = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(30),
            DiscardOnBufferOverflow = false
        };

        _waveOut = new WaveOutEvent
        {
            DeviceNumber = _deviceNumber,
            DesiredLatency = 200
        };
        _waveOut.Init(_buffer);
        _waveOut.Play();

        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;
        _waveOut?.Stop();
        _buffer?.ClearBuffer();
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_buffer is null || !IsPlaying) return;

        var maxFill = _buffer.BufferLength - pcmData.Length;
        while (_buffer.BufferedBytes > maxFill)
        {
            await Task.Delay(20, ct);
            if (_buffer is null || !IsPlaying) return;
        }

        _buffer.AddSamples(pcmData, 0, pcmData.Length);
    }

    public void ClearBuffer() => _buffer?.ClearBuffer();

    public void Dispose()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _buffer = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Enumerate all USB/non-default audio output devices on Windows.
    /// </summary>
    public static IEnumerable<UsbAudioOutputProvider> EnumerateDevices()
    {
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            // Skip device 0 (system default) — that's WindowsSpeakerProvider's territory
            if (i > 0)
            {
                yield return new UsbAudioOutputProvider(i, caps.ProductName, $"waveout-{i}");
            }
        }
    }
}
#endif
```

### Android (USB Audio Class)

On Android, USB audio devices are handled through `AudioDeviceInfo` with type
`AudioDeviceType.UsbDevice`. The approach is to use `AudioTrack.SetPreferredDevice()`
to route audio to the specific USB device.

```csharp
namespace BodyCam.Services.Audio;

#if ANDROID
using Android.Media;

/// <summary>
/// Audio output to a USB audio device on Android.
/// Uses AudioTrack with explicit device routing via SetPreferredDevice.
/// </summary>
public class UsbAudioOutputProvider : IAudioOutputProvider, IDisposable
{
    private readonly AudioDeviceInfo _device;
    private AudioTrack? _audioTrack;

    public string DisplayName => $"USB: {_device.ProductName}";
    public string ProviderId => "usb-audio";
    public string? DeviceId => _device.Id.ToString();
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public UsbAudioOutputProvider(AudioDeviceInfo device)
    {
        _device = device;
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        int bufferSize = AudioTrack.GetMinBufferSize(
            sampleRate,
            ChannelOut.Mono,
            Encoding.Pcm16bit);

        _audioTrack = new AudioTrack(
            new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Speech)!
                .Build()!,
            new AudioFormat.Builder()
                .SetSampleRate(sampleRate)!
                .SetChannelMask(ChannelOut.Mono)!
                .SetEncoding(Encoding.Pcm16bit)!
                .Build()!,
            bufferSize,
            AudioTrackMode.Stream,
            AudioManager.AudioSessionIdGenerate);

        // Route audio to the specific USB device
        _audioTrack.SetPreferredDevice(_device);
        _audioTrack.Play();
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;
        _audioTrack?.Stop();
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_audioTrack is null || !IsPlaying) return Task.CompletedTask;
        _audioTrack.Write(pcmData, 0, pcmData.Length);
        return Task.CompletedTask;
    }

    public void ClearBuffer() => _audioTrack?.Flush();

    public void Dispose()
    {
        _audioTrack?.Stop();
        _audioTrack?.Release();
        _audioTrack = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Enumerate USB audio output devices on Android.
    /// </summary>
    public static IEnumerable<UsbAudioOutputProvider> EnumerateDevices()
    {
        var audioManager = (AudioManager?)Android.App.Application.Context
            .GetSystemService(Android.Content.Context.AudioService);
        if (audioManager is null) yield break;

        var devices = audioManager.GetDevices(GetDevicesTargets.Outputs);
        foreach (var device in devices)
        {
            if (device.Type == AudioDeviceType.UsbDevice)
                yield return new UsbAudioOutputProvider(device);
        }
    }
}
#endif
```

---

## Device Enumeration Service

A helper service to discover available audio output devices across all provider
types at runtime. This feeds the settings UI picker.

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// Enumerates all available audio output devices across provider types.
/// Used by the settings UI to populate the output device picker.
/// </summary>
public class AudioDeviceEnumerator
{
    private readonly IEnumerable<IAudioOutputProvider> _staticProviders;

    public AudioDeviceEnumerator(IEnumerable<IAudioOutputProvider> staticProviders)
    {
        _staticProviders = staticProviders;
    }

    /// <summary>
    /// Returns all available audio output providers, including dynamically
    /// discovered USB and BT devices.
    /// </summary>
    public IReadOnlyList<IAudioOutputProvider> GetAvailableDevices()
    {
        var devices = new List<IAudioOutputProvider>();

        // Static providers (phone speaker, windows speaker, stub)
        devices.AddRange(_staticProviders.Where(p => p.IsAvailable));

        // Dynamic providers (USB devices, BT devices)
        // These are discovered at runtime and may change
#if WINDOWS
        devices.AddRange(UsbAudioOutputProvider.EnumerateDevices());
#elif ANDROID
        devices.AddRange(UsbAudioOutputProvider.EnumerateDevices());
#endif

        return devices;
    }
}
```

---

## File Layout

After Phase 1, the new files in `Services/Audio/`:

```
Services/
  Audio/
    IAudioOutputProvider.cs
    AudioOutputManager.cs
    AudioDeviceEnumerator.cs
    StubAudioOutputProvider.cs
Platforms/
  Windows/
    WindowsSpeakerProvider.cs       ← replaces WindowsAudioOutputService.cs
    UsbAudioOutputProvider.cs       ← new (Phase 3)
  Android/
    PhoneSpeakerProvider.cs         ← replaces AndroidAudioOutputService.cs
    UsbAudioOutputProvider.cs       ← new (Phase 3)
```

The old files (`WindowsAudioOutputService.cs`, `AndroidAudioOutputService.cs`,
`AudioOutputService.cs`) are deleted after migration is verified.
