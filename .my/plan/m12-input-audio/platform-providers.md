# M12 — Platform Providers (Phone, Laptop, USB)

## Goal

Wrap the existing `WindowsAudioInputService` and `AndroidAudioInputService` into
`IAudioInputProvider` implementations. Add USB audio device support on Windows.

---

## PlatformMicProvider

Wraps the existing platform-specific `IAudioInputService` implementations into the
new `IAudioInputProvider` interface. This is the default provider and fallback target.

### Windows Implementation

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// Audio input from the default system microphone.
/// Windows: wraps NAudio WaveInEvent (from WindowsAudioInputService).
/// </summary>
public class PlatformMicProvider : IAudioInputProvider, IDisposable
{
    private readonly AppSettings _settings;
    private WaveInEvent? _waveIn;

    public string DisplayName => "System Microphone";
    public string ProviderId => "platform";
    public bool IsAvailable => true; // System mic is always "available"
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public PlatformMicProvider(AppSettings settings)
    {
        _settings = settings;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return Task.CompletedTask;

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(_settings.SampleRate, 16, 1),
            BufferMilliseconds = _settings.ChunkDurationMs
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
        _waveIn.StartRecording();
        IsCapturing = true;

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;

        _waveIn?.StopRecording();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        var chunk = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
        AudioChunkAvailable?.Invoke(this, chunk);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsCapturing = false;
        if (e.Exception is not null)
            Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
```

### Android Implementation

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// Audio input from the default platform microphone.
/// Android: wraps AudioRecord with VoiceCommunication source.
/// </summary>
public class PlatformMicProvider : IAudioInputProvider, IDisposable
{
    private readonly AppSettings _settings;
    private AudioRecord? _audioRecord;
    private CancellationTokenSource? _recordCts;
    private Task? _recordTask;

    public string DisplayName => "Phone Microphone";
    public string ProviderId => "platform";
    public bool IsAvailable => true;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public PlatformMicProvider(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return;

        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Microphone>();
            if (status != PermissionStatus.Granted)
                throw new PermissionException("Microphone permission denied.");
        }

        int bufferSize = AudioRecord.GetMinBufferSize(
            _settings.SampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit);

        _audioRecord = new AudioRecord(
            AudioSource.VoiceCommunication,
            _settings.SampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit,
            bufferSize);

        _audioRecord.StartRecording();
        IsCapturing = true;

        _recordCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _recordTask = Task.Run(() => RecordLoopAsync(_recordCts.Token));
    }

    private async Task RecordLoopAsync(CancellationToken ct)
    {
        int chunkBytes = _settings.SampleRate * 2 * _settings.ChunkDurationMs / 1000;
        var buffer = new byte[chunkBytes];

        while (!ct.IsCancellationRequested
            && _audioRecord?.RecordingState == RecordState.Recording)
        {
            int bytesRead = await _audioRecord.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                var chunk = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
                AudioChunkAvailable?.Invoke(this, chunk);
            }
        }
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;

        _recordCts?.Cancel();
        _audioRecord?.Stop();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _recordCts?.Cancel();
        _audioRecord?.Stop();
        _audioRecord?.Release();
        _audioRecord = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
```

### Platform-Specific File Placement

Same pattern as the existing platform implementations:

```
Services/
  Audio/
    IAudioInputProvider.cs          ← shared
    AudioInputManager.cs            ← shared
    AudioResampler.cs               ← shared
Platforms/
  Windows/
    PlatformMicProvider.cs          ← Windows NAudio implementation
  Android/
    PlatformMicProvider.cs          ← Android AudioRecord implementation
```

The MAUI build system picks the right file per platform via `Platforms/<OS>/` folder
convention.

---

## USB Audio Provider (Windows)

On Windows, USB microphones and headsets appear as standard audio endpoints. NAudio's
`MMDeviceEnumerator` can enumerate all audio capture devices including USB.

```csharp
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BodyCam.Services.Audio;

/// <summary>
/// Audio input from a specific USB audio device on Windows.
/// Uses NAudio MMDevice API for device enumeration and WasapiCapture for recording.
/// </summary>
public class WindowsUsbAudioProvider : IAudioInputProvider, IDisposable
{
    private readonly AppSettings _settings;
    private readonly MMDevice _device;
    private WasapiCapture? _capture;
    private BufferedWaveProvider? _bufferedProvider;
    private WaveFormat _targetFormat;

    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable => _device.State == DeviceState.Active;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public WindowsUsbAudioProvider(MMDevice device, AppSettings settings)
    {
        _device = device;
        _settings = settings;
        DisplayName = device.FriendlyName;
        ProviderId = $"usb:{device.ID}";
        _targetFormat = new WaveFormat(settings.SampleRate, 16, 1);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return Task.CompletedTask;

        _capture = new WasapiCapture(_device)
        {
            WaveFormat = _device.AudioClient.MixFormat
        };

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        IsCapturing = true;

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;

        _capture?.StopRecording();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        // Convert from device format to target PCM16 mono
        var chunk = ConvertToTargetFormat(e.Buffer, e.BytesRecorded);
        if (chunk.Length > 0)
            AudioChunkAvailable?.Invoke(this, chunk);
    }

    private byte[] ConvertToTargetFormat(byte[] buffer, int bytesRecorded)
    {
        // If device format matches target, pass through
        if (_capture?.WaveFormat.SampleRate == _targetFormat.SampleRate
            && _capture.WaveFormat.BitsPerSample == 16
            && _capture.WaveFormat.Channels == 1)
        {
            var chunk = new byte[bytesRecorded];
            Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRecorded);
            return chunk;
        }

        // Use NAudio's resampling: device format → PCM16 mono at target rate
        using var inputStream = new RawSourceWaveStream(
            new MemoryStream(buffer, 0, bytesRecorded), _capture!.WaveFormat);
        using var resampler = new MediaFoundationResampler(inputStream, _targetFormat);
        resampler.ResamplerQuality = 60;

        using var ms = new MemoryStream();
        var readBuffer = new byte[4096];
        int read;
        while ((read = resampler.Read(readBuffer, 0, readBuffer.Length)) > 0)
            ms.Write(readBuffer, 0, read);

        return ms.ToArray();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsCapturing = false;
        if (e.Exception is not null)
            Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
```

### USB Device Enumeration

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// Enumerates USB and other audio capture devices on Windows.
/// Registers discovered devices as IAudioInputProvider instances with AudioInputManager.
/// </summary>
public class WindowsAudioDeviceEnumerator : IDisposable
{
    private readonly AudioInputManager _manager;
    private readonly AppSettings _settings;
    private readonly MMDeviceEnumerator _enumerator;
    private readonly NotificationClient _notificationClient;

    public WindowsAudioDeviceEnumerator(AudioInputManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
        _enumerator = new MMDeviceEnumerator();

        // Listen for device connect/disconnect
        _notificationClient = new NotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    /// <summary>
    /// Scan for audio capture devices and register non-default ones as providers.
    /// Called at startup and when devices change.
    /// </summary>
    public void ScanAndRegister()
    {
        var devices = _enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

        foreach (var device in devices)
        {
            // Skip the default device — that's handled by PlatformMicProvider
            var providerId = $"usb:{device.ID}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            var provider = new WindowsUsbAudioProvider(device, _settings);
            _manager.RegisterProvider(provider);
        }
    }

    private class NotificationClient : IMMNotificationClient
    {
        private readonly WindowsAudioDeviceEnumerator _owner;

        public NotificationClient(WindowsAudioDeviceEnumerator owner) => _owner = owner;

        public void OnDeviceAdded(string deviceId) => _owner.ScanAndRegister();

        public void OnDeviceRemoved(string deviceId)
        {
            var providerId = $"usb:{deviceId}";
            _ = _owner._manager.UnregisterProviderAsync(providerId);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Active)
                _owner.ScanAndRegister();
            else
                OnDeviceRemoved(deviceId);
        }

        // Not needed for our purposes
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        _enumerator.Dispose();
    }
}
```

### USB Audio on Android

On Android, USB audio devices are handled transparently by the Android audio system.
When a USB headset is plugged in, Android routes audio through it automatically (or
based on `AudioManager` policy). No separate provider is needed — `PlatformMicProvider`
with `AudioSource.VoiceCommunication` will use the USB mic if Android routes to it.

For explicit USB device selection on Android, use `AudioManager.SetCommunicationDevice()`:

```csharp
// Android 12+ (API 31+)
var audioManager = (AudioManager)Android.App.Application.Context
    .GetSystemService(Android.Content.Context.AudioService)!;

var devices = audioManager.GetDevices(AudioDeviceType.Input);
var usbDevice = devices.FirstOrDefault(d => d.Type == AudioDeviceType.UsbDevice);

if (usbDevice is not null)
    audioManager.SetCommunicationDevice(usbDevice);
```

This can be integrated into `PlatformMicProvider` as an optional device selection step
before calling `StartAsync`.

---

## Removing Old Implementations

After Phase 1, the old implementations are replaced:

| Old File | Action |
|----------|--------|
| `Services/IAudioInputService.cs` | **Keep** — `AudioInputManager` implements it |
| `Services/AudioInputService.cs` (stub) | **Remove** — replaced by `AudioInputManager` |
| `Platforms/Windows/WindowsAudioInputService.cs` | **Remove** — replaced by `PlatformMicProvider` |
| `Platforms/Android/AndroidAudioInputService.cs` | **Remove** — replaced by `PlatformMicProvider` |

### MauiProgram.cs Changes

```csharp
// BEFORE
#if WINDOWS
builder.Services.AddSingleton<IAudioInputService, WindowsAudioInputService>();
#elif ANDROID
builder.Services.AddSingleton<IAudioInputService, AndroidAudioInputService>();
#else
builder.Services.AddSingleton<IAudioInputService, AudioInputService>();
#endif

// AFTER
builder.Services.AddSingleton<IAudioInputProvider, PlatformMicProvider>();
builder.Services.AddSingleton<AudioInputManager>();
builder.Services.AddSingleton<IAudioInputService>(sp => sp.GetRequiredService<AudioInputManager>());

#if WINDOWS
// USB device enumerator registers additional providers dynamically
builder.Services.AddSingleton<WindowsAudioDeviceEnumerator>();
#endif
```

---

## WiFi Audio Provider

For glasses that stream audio over WiFi (Phase 4), a `WifiAudioProvider` receives
PCM chunks over a network stream. The protocol depends on the glasses model:

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// Receives audio from glasses streaming over WiFi.
/// Base class — subclass per glasses protocol.
/// </summary>
public abstract class WifiAudioProvider : IAudioInputProvider
{
    private readonly AppSettings _settings;
    private CancellationTokenSource? _streamCts;

    public abstract string DisplayName { get; }
    public abstract string ProviderId { get; }
    public abstract bool IsAvailable { get; }
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    protected WifiAudioProvider(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return;

        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsCapturing = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await ReceiveAudioLoopAsync(_streamCts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                IsCapturing = false;
            }
        });
    }

    public Task StopAsync()
    {
        _streamCts?.Cancel();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Override to implement protocol-specific audio stream reception.
    /// Call EmitChunk() with PCM16 mono data at AppSettings.SampleRate.
    /// </summary>
    protected abstract Task ReceiveAudioLoopAsync(CancellationToken ct);

    protected void EmitChunk(byte[] pcm16Chunk)
        => AudioChunkAvailable?.Invoke(this, pcm16Chunk);

    protected void EmitChunkWithResample(byte[] rawPcm16, int sourceRate)
    {
        var resampled = AudioResampler.Resample(rawPcm16, sourceRate, _settings.SampleRate);
        AudioChunkAvailable?.Invoke(this, resampled);
    }

    public virtual ValueTask DisposeAsync()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

Concrete WiFi audio providers (per glasses model) go in Phase 4. Example:

```csharp
/// <summary>
/// Audio from glasses that stream raw PCM over TCP.
/// </summary>
public class TcpAudioProvider : WifiAudioProvider
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;

    public override string DisplayName => $"WiFi Glasses ({_host})";
    public override string ProviderId => $"wifi:{_host}:{_port}";
    public override bool IsAvailable => true; // Try to connect on Start

    public TcpAudioProvider(string host, int port, AppSettings settings) : base(settings)
    {
        _host = host;
        _port = port;
    }

    protected override async Task ReceiveAudioLoopAsync(CancellationToken ct)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(_host, _port, ct);

        var stream = _client.GetStream();
        var buffer = new byte[4096];

        while (!ct.IsCancellationRequested)
        {
            int bytesRead = await stream.ReadAsync(buffer, ct);
            if (bytesRead == 0) break; // Connection closed

            var chunk = new byte[bytesRead];
            Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
            EmitChunk(chunk);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await base.DisposeAsync();
    }
}
```
