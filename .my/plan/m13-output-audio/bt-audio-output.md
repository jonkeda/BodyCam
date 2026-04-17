# M13 — Bluetooth Audio Output

## Overview

BT audio output is the primary use case for BodyCam — the user wears smart glasses
with built-in speakers, and the AI assistant's voice plays through those glasses.
BT earbuds and headsets use the same routing mechanism.

From the OS perspective, BT glasses with speakers are just BT audio devices. They
pair via standard BT, advertise A2DP (Advanced Audio Distribution Profile) for
high-quality audio or HFP (Hands-Free Profile) for lower-latency bidirectional audio.

---

## BT Audio Profiles

### A2DP (Advanced Audio Distribution Profile)

High-quality **unidirectional** audio streaming (source → sink). Used for music,
media playback. Supports multiple codecs.

| Property | Value |
|----------|-------|
| Direction | Source → Sink (one-way) |
| Quality | High (SBC: 328kbps, AAC: 256kbps, aptX: 352kbps) |
| Latency | 100-200ms (codec-dependent) |
| Use case | AI voice output where latency is acceptable |

### HFP (Hands-Free Profile)

**Bidirectional** audio — used for phone calls. Lower quality, lower latency.
Routes both microphone input and speaker output through the BT device.

| Property | Value |
|----------|-------|
| Direction | Bidirectional |
| Quality | Lower (CVSD: 64kbps narrowband, mSBC: 128kbps wideband) |
| Latency | 30-80ms |
| Use case | Conversational AI with tight latency requirements |

### Which Profile for BodyCam?

**A2DP for voice output, HFP when also capturing input from glasses mic.**

For Phase 2 (output only), A2DP is the right choice — higher quality for the AI's
voice. When M12 adds BT microphone input, HFP may be preferred for lower round-trip
latency in conversational mode.

The `BluetoothAudioOutputProvider` should support both profiles and let the user
(or heuristics) choose:

```csharp
public enum BluetoothAudioProfile
{
    /// <summary>High-quality unidirectional audio (A2DP).</summary>
    HighQuality,

    /// <summary>Low-latency bidirectional audio (HFP).</summary>
    LowLatency
}
```

---

## Codec Selection

A2DP mandates SBC support. Better codecs are optional and negotiated during connection.

| Codec | Bit Rate | Latency | Quality | Availability |
|-------|----------|---------|---------|-------------|
| **SBC** | 198-345 kbps | ~150ms | Good | Mandatory (all A2DP devices) |
| **AAC** | 128-256 kbps | ~120ms | Better | Most devices, native on Android/iOS |
| **aptX** | 352 kbps | ~70ms | Better | Qualcomm chipsets |
| **aptX HD** | 576 kbps | ~80ms | Best | Qualcomm chipsets |
| **LC3** | Variable | ~20ms | Best | BT 5.2 LE Audio (future) |

For BodyCam's speech output (16-bit PCM mono, 24kHz), even SBC provides adequate
quality. The speech content type means we don't need audiophile-grade codecs.

**Recommendation:** Don't implement codec selection in Phase 2. Use whatever the OS
negotiates with the BT device (typically SBC or AAC). Add codec preferences in Phase 4
if latency testing shows it matters.

---

## Android Implementation

Android provides the richest BT audio API. Audio routing is done via
`AudioTrack.SetPreferredDevice()` with a BT `AudioDeviceInfo`.

### BT Device Enumeration

```csharp
#if ANDROID
using Android.Bluetooth;
using Android.Media;

public static class BluetoothAudioDeviceDiscovery
{
    /// <summary>
    /// Enumerate paired BT devices that support audio output (A2DP).
    /// </summary>
    public static IEnumerable<AudioDeviceInfo> GetBluetoothOutputDevices()
    {
        var audioManager = (AudioManager?)Android.App.Application.Context
            .GetSystemService(Android.Content.Context.AudioService);
        if (audioManager is null) yield break;

        var devices = audioManager.GetDevices(GetDevicesTargets.Outputs);
        foreach (var device in devices)
        {
            if (device.Type is AudioDeviceType.BluetoothA2dp
                            or AudioDeviceType.BluetoothSco)
            {
                yield return device;
            }
        }
    }
}
#endif
```

### BluetoothAudioOutputProvider (Android)

```csharp
namespace BodyCam.Services.Audio;

#if ANDROID
using Android.Media;

/// <summary>
/// Audio output to a Bluetooth audio device on Android.
/// Routes audio via AudioTrack.SetPreferredDevice() to the specific BT device.
/// </summary>
public class BluetoothAudioOutputProvider : IAudioOutputProvider, IDisposable
{
    private AudioDeviceInfo? _device;
    private AudioTrack? _audioTrack;
    private AudioManager? _audioManager;
    private AudioDeviceCallback? _deviceCallback;

    public string DisplayName => _device is not null
        ? $"BT: {_device.ProductName}"
        : "Bluetooth Audio";
    public string ProviderId => "bt-audio";
    public string? DeviceId => _device?.Id.ToString();
    public bool IsAvailable => _device is not null;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    /// <summary>
    /// Set the target BT device. Call before StartAsync.
    /// </summary>
    public void SetDevice(AudioDeviceInfo device)
    {
        _device = device;
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying || _device is null) return Task.CompletedTask;

        _audioManager = (AudioManager?)Android.App.Application.Context
            .GetSystemService(Android.Content.Context.AudioService);

        int bufferSize = AudioTrack.GetMinBufferSize(
            sampleRate,
            ChannelOut.Mono,
            Encoding.Pcm16bit);

        // Use larger buffer for BT to handle jitter
        bufferSize = Math.Max(bufferSize, sampleRate * 2 / 10); // At least 200ms

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

        // Route audio to the specific BT device
        _audioTrack.SetPreferredDevice(_device);
        _audioTrack.Play();

        // Monitor for device disconnection
        RegisterDeviceCallback();

        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;
        UnregisterDeviceCallback();
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

    private void RegisterDeviceCallback()
    {
        if (_audioManager is null) return;

        _deviceCallback = new AudioOutputDeviceCallback(this);
        _audioManager.RegisterAudioDeviceCallback(_deviceCallback, null);
    }

    private void UnregisterDeviceCallback()
    {
        if (_audioManager is null || _deviceCallback is null) return;
        _audioManager.UnregisterAudioDeviceCallback(_deviceCallback);
        _deviceCallback = null;
    }

    internal void OnDeviceRemoved(AudioDeviceInfo device)
    {
        if (_device is not null && device.Id == _device.Id)
        {
            IsPlaying = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        UnregisterDeviceCallback();
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
    /// Callback to detect BT device disconnection.
    /// </summary>
    private class AudioOutputDeviceCallback : AudioDeviceCallback
    {
        private readonly BluetoothAudioOutputProvider _provider;

        public AudioOutputDeviceCallback(BluetoothAudioOutputProvider provider)
        {
            _provider = provider;
        }

        public override void OnAudioDevicesRemoved(AudioDeviceInfo[]? removedDevices)
        {
            if (removedDevices is null) return;
            foreach (var device in removedDevices)
            {
                if (device.Type is AudioDeviceType.BluetoothA2dp
                                or AudioDeviceType.BluetoothSco)
                {
                    _provider.OnDeviceRemoved(device);
                }
            }
        }
    }
}
#endif
```

### Audio Focus Management (Android)

Android requires apps to request **audio focus** before playing audio. Without it,
other apps may play simultaneously or the system may not route audio correctly.

```csharp
#if ANDROID
using Android.Media;

public static class AudioFocusHelper
{
    private static AudioFocusRequestClass? _focusRequest;

    /// <summary>
    /// Request audio focus for speech playback.
    /// Uses AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK — other apps lower their volume.
    /// </summary>
    public static AudioFocusRequest RequestSpeechFocus(AudioManager audioManager)
    {
        _focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.GainTransientMayDuck)
            .SetAudioAttributes(
                new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Media)!
                    .SetContentType(AudioContentType.Speech)!
                    .Build()!)
            .Build();

        return audioManager.RequestAudioFocus(_focusRequest);
    }

    /// <summary>
    /// Abandon audio focus when done speaking.
    /// </summary>
    public static void AbandonFocus(AudioManager audioManager)
    {
        if (_focusRequest is not null)
            audioManager.AbandonAudioFocusRequest(_focusRequest);
    }
}
#endif
```

**When to request focus:**
- `AudioOutputManager.StartAsync()` → request `AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK`
- `AudioOutputManager.StopAsync()` → abandon focus
- The `MAY_DUCK` flag means other apps (music, podcasts) lower their volume instead
  of pausing — appropriate for an AI assistant that speaks in short bursts.

---

## Windows Implementation

On Windows, BT audio devices appear as standard audio output devices. NAudio's
`WaveOut.GetCapabilities()` lists them alongside other audio outputs. The approach
is simpler — just select the right device number.

### BT Device Enumeration (Windows)

```csharp
#if WINDOWS
using NAudio.Wave;
using System.Runtime.InteropServices;

public static class BluetoothAudioDeviceDiscovery
{
    /// <summary>
    /// Enumerate BT audio output devices on Windows.
    /// BT devices appear as regular WaveOut devices — we identify them
    /// by checking the device name (contains "Bluetooth" or known BT device names).
    /// </summary>
    public static IEnumerable<(int DeviceNumber, string Name)> GetBluetoothOutputDevices()
    {
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            // NAudio truncates device names to 31 chars. For better enumeration,
            // use MMDevice API (NAudio.CoreAudioApi) in production.
            yield return (i, caps.ProductName);
        }
    }

    /// <summary>
    /// Better enumeration using CoreAudioApi — provides full device names and
    /// can distinguish BT from USB from internal speakers.
    /// </summary>
    public static IEnumerable<(string DeviceId, string Name, bool IsBluetooth)>
        GetOutputDevicesViaMMDevice()
    {
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(
            NAudio.CoreAudioApi.DataFlow.Render,
            NAudio.CoreAudioApi.DeviceState.Active);

        foreach (var device in devices)
        {
            // Check device properties for BT indicator
            var isBluetooth = device.FriendlyName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
                || device.DeviceFriendlyName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

            yield return (device.ID, device.FriendlyName, isBluetooth);
        }
    }
}
#endif
```

### BluetoothAudioOutputProvider (Windows)

On Windows, the BT audio provider is structurally identical to `UsbAudioOutputProvider`
(both use NAudio WaveOutEvent with a device number). The difference is device discovery
and display name.

For a cleaner approach, use `WasapiOut` with `MMDevice` for explicit device selection
instead of WaveOut device numbers:

```csharp
namespace BodyCam.Services.Audio;

#if WINDOWS
using NAudio.CoreAudioApi;
using NAudio.Wave;

/// <summary>
/// Audio output to a specific BT audio device on Windows via WASAPI.
/// </summary>
public class BluetoothAudioOutputProvider : IAudioOutputProvider, IDisposable
{
    private readonly MMDevice _mmDevice;
    private WasapiOut? _wasapiOut;
    private BufferedWaveProvider? _buffer;

    public string DisplayName => $"BT: {_mmDevice.FriendlyName}";
    public string ProviderId => "bt-audio";
    public string? DeviceId => _mmDevice.ID;
    public bool IsAvailable => _mmDevice.State == DeviceState.Active;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public BluetoothAudioOutputProvider(MMDevice device)
    {
        _mmDevice = device;
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

        // Use WASAPI for explicit device selection
        // Shared mode for compatibility, 200ms latency
        _wasapiOut = new WasapiOut(_mmDevice, AudioClientShareMode.Shared, true, 200);
        _wasapiOut.Init(_buffer);
        _wasapiOut.Play();

        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;
        _wasapiOut?.Stop();
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
        _wasapiOut?.Stop();
        _wasapiOut?.Dispose();
        _wasapiOut = null;
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

---

## Latency Considerations

BT audio adds inherent latency compared to wired/local speakers. This affects the
conversational feel of the AI assistant.

### Latency Breakdown

```
PCM data generated by Realtime API
  → Network transit:          ~50ms
  → VoiceOutputAgent buffer:  ~0ms (pass-through)
  → AudioOutputManager:      ~0ms (pass-through)
  → Provider buffer fill:     ~20ms
  → BT codec encoding:        ~5-15ms (SBC)
  → BT transmission:          ~10-30ms
  → BT codec decoding:        ~5-15ms
  → DAC + speaker:             ~5ms
                              ─────────
  Total additional latency:    ~45-85ms over local speaker
```

For speech output, 45-85ms additional latency is acceptable — human conversation
has natural pauses of 200-500ms between turns.

### Mitigations

1. **Larger BT buffer** — Use 200ms buffer minimum for BT providers to absorb jitter.
   The existing 30-second `BufferDuration` for NAudio is fine; the key is the
   `DesiredLatency` / WASAPI latency parameter.

2. **Pre-buffer before playback** — Accumulate 100-200ms of audio data before starting
   playback to prevent underruns. This adds upfront latency but prevents stuttering.

   ```csharp
   // In BluetoothAudioOutputProvider.PlayChunkAsync:
   private int _prebufferBytes;
   private bool _prebufferComplete;

   public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
   {
       if (_buffer is null) return Task.CompletedTask;

       _buffer.AddSamples(pcmData, 0, pcmData.Length);

       if (!_prebufferComplete)
       {
           _prebufferBytes += pcmData.Length;
           // Start playback after 150ms of audio is buffered
           // 24000 Hz * 2 bytes * 0.15s = 7200 bytes
           if (_prebufferBytes >= _sampleRate * 2 * 150 / 1000)
           {
               _wasapiOut?.Play();
               _prebufferComplete = true;
           }
       }

       return Task.CompletedTask;
   }
   ```

3. **Codec preference** — If the device supports aptX (70ms vs SBC's 150ms),
   prefer it. This is OS-level configuration, not something we control in code.

---

## Volume Control

Each provider should expose volume control. BT devices often have their own
volume (hardware volume on the glasses) independent of the OS volume.

```csharp
/// <summary>
/// Optional extension for providers that support volume control.
/// </summary>
public interface IVolumeControl
{
    /// <summary>Volume level from 0.0 (silent) to 1.0 (max).</summary>
    float Volume { get; set; }

    /// <summary>Whether the output is muted.</summary>
    bool IsMuted { get; set; }
}
```

### Implementation Examples

```csharp
// Windows — via NAudio WaveOutEvent/WasapiOut
public float Volume
{
    get => _waveOut?.Volume ?? 1.0f;
    set { if (_waveOut is not null) _waveOut.Volume = Math.Clamp(value, 0f, 1f); }
}

// Android — via AudioTrack
public float Volume
{
    get => _volume;
    set
    {
        _volume = Math.Clamp(value, 0f, 1f);
        _audioTrack?.SetVolume(_volume);
    }
}
```

---

## Disconnection Handling

BT devices disconnect unpredictably — glasses run out of battery, user walks out
of range, BT stack crashes. The provider must detect this and fire `Disconnected`.

### Detection Mechanisms

**Android:**
- `AudioDeviceCallback.OnAudioDevicesRemoved()` — fires when BT device disconnects
- `BluetoothDevice.ActionAclDisconnected` broadcast receiver — lower-level BT event
- `AudioTrack` write errors — if `Write()` returns error codes

**Windows:**
- `MMNotificationClient.OnDeviceStateChanged()` — fires when device state changes
- WASAPI `PlaybackStopped` event with `StoppedEventArgs.Exception` — device removal
- Periodic `MMDevice.State` polling as fallback

### Disconnection Flow

```
BT device turns off
  → OS notifies app (AudioDeviceCallback / MMNotificationClient)
  → BluetoothAudioOutputProvider.OnDeviceRemoved()
    → IsPlaying = false
    → Disconnected?.Invoke(this, EventArgs.Empty)
      → AudioOutputManager.OnProviderDisconnected()
        → FallbackToDefaultAsync()
          → SetActiveAsync("phone-speaker")   // or "windows-speaker"
            → Phone/laptop speaker starts
              → Next audio delta plays through local speaker
```

**User experience:** The AI's voice briefly stutters or gaps for ~500ms during the
switch, then continues through the phone/laptop speaker. A toast notification tells
the user: "BT audio disconnected — switched to phone speaker."

---

## BT + M12 Coordination

M12 handles BT audio **input** (microphone on glasses). When both input and output
are on the same BT device, we should use HFP (bidirectional) instead of A2DP (output
only) + separate SCO (input only) to avoid profile conflicts.

### Profile Selection Logic

```csharp
/// <summary>
/// Determines the best BT profile given the current input and output configuration.
/// </summary>
public static BluetoothAudioProfile SelectProfile(
    bool btInputActive,
    bool btOutputActive,
    string? inputDeviceId,
    string? outputDeviceId)
{
    // Same BT device for both input and output → HFP
    if (btInputActive && btOutputActive
        && inputDeviceId == outputDeviceId)
    {
        return BluetoothAudioProfile.LowLatency; // HFP
    }

    // Output only → A2DP for higher quality
    if (btOutputActive && !btInputActive)
    {
        return BluetoothAudioProfile.HighQuality; // A2DP
    }

    // Different devices → A2DP for output, SCO for input
    return BluetoothAudioProfile.HighQuality; // A2DP
}
```

This coordination is implemented in Phase 2 when M12 BT input is also ready. For
Phase 2 (output only), always use A2DP.

---

## Testing Strategy

### Unit Tests
- `AudioOutputManager` fallback logic
- Profile selection logic
- Volume clamping

### Integration Tests
- Provider lifecycle (Start → PlayChunk → Stop → Start again)
- Disconnection → fallback flow (mock `Disconnected` event)
- Buffer clear during interruption

### Manual Testing (BT devices)
- Pair BT earbuds, verify audio routes correctly
- Disconnect BT during playback, verify fallback to phone speaker
- Reconnect BT, verify manual re-selection works
- Test with BT glasses (when available)
- Measure end-to-end latency with BT vs local speaker

### Test Devices
| Device | Profile | Purpose |
|--------|---------|---------|
| Any BT earbuds | A2DP | Basic BT audio output |
| BT speaker | A2DP | Shared listening / demo |
| BT headset with mic | A2DP + HFP | Test profile switching |
| Smart glasses | A2DP / HFP | Primary target device |
