# M13 Phase 2 — Bluetooth Audio Output

**Status:** NOT STARTED  
**Prerequisite:** M13 Phase 1 (Audio Output Abstraction) — `IAudioOutputProvider`,
`AudioOutputManager`, `WindowsSpeakerProvider`, `PhoneSpeakerProvider` all wired into DI  
**Depends on:** M12 Phase 2 (BT Audio Input) — `AudioResampler`, `BluetoothDeviceInfo`,
hot-plug patterns on `AudioInputManager` established  
**Goal:** Implement `WindowsBluetoothAudioOutputProvider` and
`AndroidBluetoothAudioOutputProvider` for routing AI voice output to paired BT devices
(smart glasses, earbuds, headsets). Device enumeration, hot-plug, auto-reconnect on
saved preference, and fallback on disconnect.

---

## Current State (After Phase 1)

| Component | Status |
|-----------|--------|
| `IAudioOutputProvider` | Defined — `StartAsync(sampleRate)`, `PlayChunkAsync`, `ClearBuffer`, `Disconnected` event |
| `AudioOutputManager` | Active, manages one provider, `IAudioOutputService` backward compat |
| `WindowsSpeakerProvider` | ProviderId `"windows-speaker"`, NAudio WaveOutEvent + BufferedWaveProvider |
| `PhoneSpeakerProvider` | ProviderId `"phone-speaker"`, Android AudioTrack |
| Settings audio output picker | Shows "System Speaker" / "Phone Speaker" only |
| `AudioOutputManager.RegisterProvider()` | **Not yet implemented** (Phase 1 uses DI-injected `IEnumerable<IAudioOutputProvider>`) |
| `AudioResampler` | Exists from M12 Phase 2 — linear interpolation, PCM16 mono |
| `BluetoothDeviceInfo` | Exists from M12 Phase 2 — record with DeviceId, Name, ProviderId, IsConnected |

**Key gap:** `AudioOutputManager` stores providers in `IReadOnlyList<IAudioOutputProvider>`
from constructor DI. BT output devices arrive dynamically — after app startup, when a
headset is paired/connected. We need `RegisterProvider` / `UnregisterProviderAsync` /
`ProvidersChanged` on `AudioOutputManager`, mirroring the pattern already added to
`AudioInputManager` in M12 Phase 2.

---

## Deliverables

### New Files

| File | Purpose |
|------|---------|
| `Platforms/Windows/Audio/WindowsBluetoothAudioOutputProvider.cs` | BT A2DP playback via WasapiOut on BT render endpoint |
| `Platforms/Windows/Audio/WindowsBluetoothOutputEnumerator.cs` | Discovers BT audio render endpoints via MMDevice API + hot-plug |
| `Platforms/Android/Audio/AndroidBluetoothAudioOutputProvider.cs` | BT A2DP playback via AudioTrack with `SetPreferredDevice` |
| `Platforms/Android/Audio/AndroidBluetoothOutputEnumerator.cs` | Discovers BT audio output devices via AudioManager + BroadcastReceiver |

### Modified Files

| File | Change |
|------|--------|
| `Services/Audio/AudioOutputManager.cs` | Change `_providers` to mutable `List`, add `RegisterProvider()`, `UnregisterProviderAsync()`, `ProvidersChanged` event, auto-switch on saved pref match |
| `MauiProgram.cs` | Register BT output enumerators in DI (`#if WINDOWS` / `#elif ANDROID`) |
| `MainPage.xaml.cs` | Initialize BT output enumerator (ScanAndRegister + StartListening) alongside BT input enumerator |
| `ViewModels/SettingsViewModel.cs` | Subscribe to `AudioOutputManager.ProvidersChanged`, refresh output picker |

### Unchanged Files

| File | Why Unchanged |
|------|---------------|
| `Agents/VoiceOutputAgent.cs` | Consumes `IAudioOutputService` — `AudioOutputManager` handles switching |
| `Services/MicrophoneCoordinator.cs` | Audio output unrelated to mic coordination |
| `Orchestration/AgentOrchestrator.cs` | Audio handled downstream via VoiceOutputAgent |
| `Services/Audio/IAudioOutputProvider.cs` | Interface already has everything needed |
| `Services/Audio/AudioResampler.cs` | Reused as-is for sample rate conversion |

---

## Implementation Waves

### Wave 1: AudioOutputManager Hot-Plug API

Extend `AudioOutputManager` with dynamic provider registration — mirroring the pattern
already proven on `AudioInputManager` in M12 Phase 2. No BT code yet.

**1.1 — Change `_providers` from `IReadOnlyList` to mutable `List`**

```csharp
// AudioOutputManager.cs — change field + constructor
private readonly List<IAudioOutputProvider> _providers;

public AudioOutputManager(IEnumerable<IAudioOutputProvider> providers, ISettingsService settings, AppSettings appSettings)
{
    _providers = providers.ToList();
    _settings = settings;
    _appSettings = appSettings;
}

public IReadOnlyList<IAudioOutputProvider> Providers => _providers.AsReadOnly();
```

**1.2 — Add `ProvidersChanged` event**

```csharp
/// <summary>Fires when providers are added or removed (BT connect/disconnect).</summary>
public event EventHandler? ProvidersChanged;
```

**1.3 — Add `RegisterProvider`**

```csharp
/// <summary>
/// Register a dynamically discovered provider (e.g. BT speaker connected after startup).
/// Auto-switches if this is the user's saved preference and the current active is a
/// platform default ("windows-speaker" or "phone-speaker").
/// </summary>
public void RegisterProvider(IAudioOutputProvider provider)
{
    if (_providers.Any(p => p.ProviderId == provider.ProviderId))
        return;

    _providers.Add(provider);
    ProvidersChanged?.Invoke(this, EventArgs.Empty);

    // Auto-switch if user's saved preference matches and we're on platform fallback
    var platformIds = new[] { "windows-speaker", "phone-speaker" };
    if (provider.ProviderId == _settings.ActiveAudioOutputProvider
        && (_active is null || platformIds.Contains(_active.ProviderId)))
    {
        _ = SetActiveAsync(provider.ProviderId);
    }
}
```

**1.4 — Add `UnregisterProviderAsync`**

```csharp
/// <summary>
/// Remove a dynamically discovered provider (e.g. BT speaker disconnected).
/// If it was active, falls back to platform default.
/// </summary>
public async Task UnregisterProviderAsync(string providerId)
{
    var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
    if (provider is null) return;

    if (_active?.ProviderId == providerId)
    {
        await FallbackToDefaultAsync();
    }

    _providers.Remove(provider);
    await provider.DisposeAsync();
    ProvidersChanged?.Invoke(this, EventArgs.Empty);
}
```

**1.5 — Update `SetActiveAsync` to search mutable list**

The existing `SetActiveAsync` searches `_providers` which is now a `List<>` — no
change needed since `FirstOrDefault` works on both `IReadOnlyList` and `List`.
However, confirm `FallbackToDefaultAsync` still works (it already searches
`_providers` for first available).

**Verify:** Build succeeds. Existing DI behavior unchanged (constructor-injected
providers still work). `RegisterProvider` / `UnregisterProviderAsync` are callable
but not yet called from anywhere.

---

### Wave 2: Windows Bluetooth Audio Output

**2.1 — Create `WindowsBluetoothAudioOutputProvider`**

Routes PCM playback to a BT audio render endpoint using WASAPI. BT speakers on
Windows appear as standard MMDevice render endpoints with device IDs containing
`BTHENUM` or `Bluetooth`.

```csharp
// Platforms/Windows/Audio/WindowsBluetoothAudioOutputProvider.cs
namespace BodyCam.Platforms.Windows.Audio;

using NAudio.CoreAudioApi;
using NAudio.Wave;
using BodyCam.Services.Audio;

/// <summary>
/// Audio output to a specific BT audio device on Windows via WASAPI.
/// Mirrors WindowsSpeakerProvider but targets a specific BT MMDevice endpoint.
/// </summary>
public class WindowsBluetoothAudioOutputProvider : IAudioOutputProvider, IDisposable
{
    private readonly MMDevice _mmDevice;
    private WasapiOut? _wasapiOut;
    private BufferedWaveProvider? _buffer;

    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable => _mmDevice.State == DeviceState.Active;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public WindowsBluetoothAudioOutputProvider(MMDevice device)
    {
        _mmDevice = device;
        DisplayName = $"BT: {device.FriendlyName}";
        ProviderId = $"bt-out:{device.ID}";
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

        // WASAPI shared mode with 200ms latency — matches WindowsSpeakerProvider
        _wasapiOut = new WasapiOut(_mmDevice, AudioClientShareMode.Shared, true, 200);
        _wasapiOut.PlaybackStopped += OnPlaybackStopped;
        _wasapiOut.Init(_buffer);
        _wasapiOut.Play();

        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsPlaying) return Task.CompletedTask;

        if (_wasapiOut is not null)
            _wasapiOut.PlaybackStopped -= OnPlaybackStopped;
        _wasapiOut?.Stop();
        _buffer?.ClearBuffer();
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_buffer is null || !IsPlaying) return;

        // Back-pressure: wait if buffer is nearly full (same pattern as WindowsSpeakerProvider)
        var maxFill = _buffer.BufferLength - pcmData.Length;
        while (_buffer.BufferedBytes > maxFill)
        {
            await Task.Delay(20, ct);
            if (_buffer is null || !IsPlaying) return;
        }

        _buffer.AddSamples(pcmData, 0, pcmData.Length);
    }

    public void ClearBuffer() => _buffer?.ClearBuffer();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            // BT device disconnected or WASAPI error
            IsPlaying = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_wasapiOut is not null)
            _wasapiOut.PlaybackStopped -= OnPlaybackStopped;
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
```

Key design decisions:
- `ProviderId` uses `"bt-out:{device.ID}"` prefix (vs `"bt:{device.ID}"` for input)
  to distinguish output from input providers on the same physical device.
- `PlaybackStopped` with exception → fires `Disconnected` → `AudioOutputManager`
  falls back to platform speaker. Mirrors `OnRecordingStopped` pattern from
  `WindowsBluetoothAudioProvider` (input).
- Same back-pressure polling loop as `WindowsSpeakerProvider` — waits when buffer
  is nearly full to prevent `InvalidOperationException`.
- 200ms WASAPI latency in shared mode — acceptable for speech output, matches
  the existing `WindowsSpeakerProvider`.

**2.2 — Create `WindowsBluetoothOutputEnumerator`**

Discovers BT audio render endpoints and registers them with `AudioOutputManager`.
Mirrors `WindowsBluetoothEnumerator` (input) but uses `DataFlow.Render` instead
of `DataFlow.Capture`.

```csharp
// Platforms/Windows/Audio/WindowsBluetoothOutputEnumerator.cs
namespace BodyCam.Platforms.Windows.Audio;

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using BodyCam.Services.Audio;

/// <summary>
/// Discovers BT audio output (render) endpoints via MMDevice API and registers
/// them with AudioOutputManager. Listens for hot-plug events.
/// </summary>
public class WindowsBluetoothOutputEnumerator : IDisposable
{
    private readonly AudioOutputManager _manager;
    private readonly MMDeviceEnumerator _enumerator;
    private DeviceNotificationClient? _notificationClient;

    public WindowsBluetoothOutputEnumerator(AudioOutputManager manager)
    {
        _manager = manager;
        _enumerator = new MMDeviceEnumerator();
    }

    /// <summary>
    /// Scan active render endpoints for BT devices and register providers.
    /// </summary>
    public void ScanAndRegister()
    {
        var devices = _enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        foreach (var device in devices)
        {
            if (!IsBluetoothDevice(device)) continue;

            var providerId = $"bt-out:{device.ID}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            var provider = new WindowsBluetoothAudioOutputProvider(device);
            _manager.RegisterProvider(provider);
        }
    }

    /// <summary>
    /// Start listening for BT device connect/disconnect events.
    /// </summary>
    public void StartListening()
    {
        _notificationClient = new DeviceNotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    public void StopListening()
    {
        if (_notificationClient is not null)
        {
            _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            _notificationClient = null;
        }
    }

    private static bool IsBluetoothDevice(MMDevice device)
    {
        var id = device.ID ?? string.Empty;
        return id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        StopListening();
        _enumerator.Dispose();
    }

    private class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly WindowsBluetoothOutputEnumerator _owner;

        public DeviceNotificationClient(WindowsBluetoothOutputEnumerator owner)
            => _owner = owner;

        public void OnDeviceAdded(string deviceId) => _owner.ScanAndRegister();

        public void OnDeviceRemoved(string deviceId)
        {
            var providerId = $"bt-out:{deviceId}";
            _ = _owner._manager.UnregisterProviderAsync(providerId);
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Active)
                _owner.ScanAndRegister();
            else
            {
                var providerId = $"bt-out:{deviceId}";
                _ = _owner._manager.UnregisterProviderAsync(providerId);
            }
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
```

**Verify:** Build succeeds on Windows target. `WindowsBluetoothAudioOutputProvider`
compiles with NAudio references. `WindowsBluetoothOutputEnumerator` compiles with
MMDevice API. Neither integrated into DI yet.

---

### Wave 3: Android Bluetooth Audio Output

**3.1 — Create `AndroidBluetoothAudioOutputProvider`**

Routes PCM playback to a BT audio device on Android using `AudioTrack.SetPreferredDevice()`.
BT output devices appear as `AudioDeviceInfo` with type `BluetoothA2dp` or `BluetoothSco`.

```csharp
// Platforms/Android/Audio/AndroidBluetoothAudioOutputProvider.cs
namespace BodyCam.Platforms.Android.Audio;

using Android.Content;
using Android.Media;
using BodyCam.Services.Audio;

/// <summary>
/// Audio output to a specific BT audio device on Android.
/// Routes audio via AudioTrack.SetPreferredDevice() to the specific BT device.
/// Monitors for device disconnection via AudioDeviceCallback.
/// </summary>
public class AndroidBluetoothAudioOutputProvider : IAudioOutputProvider, IDisposable
{
    private readonly AudioDeviceInfo _device;
    private readonly Context _context;
    private AudioTrack? _audioTrack;
    private AudioManager? _audioManager;
    private OutputDeviceCallback? _deviceCallback;

    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable => true; // Device availability tracked by enumerator
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public AndroidBluetoothAudioOutputProvider(AudioDeviceInfo device, Context context)
    {
        _device = device;
        _context = context;
        DisplayName = $"BT: {device.ProductName}";
        ProviderId = $"bt-out:{device.Id}";
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        if (IsPlaying) return Task.CompletedTask;

        _audioManager = (AudioManager?)_context.GetSystemService(Context.AudioService);

        int bufferSize = AudioTrack.GetMinBufferSize(
            sampleRate,
            ChannelOut.Mono,
            Android.Media.Encoding.Pcm16bit);

        // Larger buffer for BT to absorb jitter — at least 200ms
        bufferSize = Math.Max(bufferSize, sampleRate * 2 / 5); // 200ms @ 16-bit mono

        _audioTrack = new AudioTrack(
            new AudioAttributes.Builder()!
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Speech)!
                .Build()!,
            new AudioFormat.Builder()!
                .SetSampleRate(sampleRate)!
                .SetChannelMask(ChannelOut.Mono)!
                .SetEncoding(Android.Media.Encoding.Pcm16bit)!
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
        _deviceCallback = new OutputDeviceCallback(this);
        _audioManager.RegisterAudioDeviceCallback(_deviceCallback, null);
    }

    private void UnregisterDeviceCallback()
    {
        if (_audioManager is null || _deviceCallback is null) return;
        _audioManager.UnregisterAudioDeviceCallback(_deviceCallback);
        _deviceCallback = null;
    }

    internal void OnDeviceRemoved(AudioDeviceInfo removedDevice)
    {
        if (removedDevice.Id == _device.Id)
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
    /// Callback to detect BT audio output device disconnection.
    /// </summary>
    private class OutputDeviceCallback : AudioDeviceCallback
    {
        private readonly AndroidBluetoothAudioOutputProvider _provider;

        public OutputDeviceCallback(AndroidBluetoothAudioOutputProvider provider)
            => _provider = provider;

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
```

Key design decisions:
- Uses `SetPreferredDevice(_device)` for explicit BT device routing — Android handles
  A2DP codec negotiation (SBC/AAC) transparently. No manual codec selection needed.
- Buffer size: at least 200ms to absorb BT jitter. `GetMinBufferSize` returns the
  hardware minimum; we use whichever is larger.
- `AudioUsageKind.Media` + `AudioContentType.Speech` — tells Android this is speech
  output, which may enable speech-optimized codec settings on some devices.
- Disconnect detection via `AudioDeviceCallback.OnAudioDevicesRemoved()` — fires
  `Disconnected` → `AudioOutputManager` falls back to phone speaker.
- No SCO routing needed for output — A2DP handles high-quality unidirectional output.
  SCO is only needed when the same BT device provides both input and output
  (handled by M12's input provider).

**3.2 — Create `AndroidBluetoothOutputEnumerator`**

Discovers BT audio output devices via `AudioManager.GetDevices(Outputs)` and registers
them with `AudioOutputManager`. Listens for BT connection state changes via
`BluetoothDevice.ActionAclConnected` / `ActionAclDisconnected` broadcasts.

```csharp
// Platforms/Android/Audio/AndroidBluetoothOutputEnumerator.cs
namespace BodyCam.Platforms.Android.Audio;

using Android.Bluetooth;
using Android.Content;
using Android.Media;
using BodyCam.Services.Audio;

/// <summary>
/// Discovers BT audio output devices on Android and registers them with
/// AudioOutputManager. Listens for BT connection state changes.
/// </summary>
public class AndroidBluetoothOutputEnumerator : IDisposable
{
    private readonly AudioOutputManager _manager;
    private readonly Context _context;
    private readonly AudioManager? _audioManager;
    private BluetoothOutputReceiver? _receiver;

    public AndroidBluetoothOutputEnumerator(AudioOutputManager manager)
    {
        _manager = manager;
        _context = Android.App.Application.Context;
        _audioManager = (AudioManager?)_context.GetSystemService(Context.AudioService);
    }

    /// <summary>
    /// Scan for BT audio output devices and register providers.
    /// </summary>
    public void ScanAndRegister()
    {
        if (_audioManager is null) return;

        var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs);
        if (devices is null) return;

        foreach (var device in devices)
        {
            if (device.Type is not (AudioDeviceType.BluetoothA2dp
                                or AudioDeviceType.BluetoothSco))
                continue;

            var providerId = $"bt-out:{device.Id}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            var provider = new AndroidBluetoothAudioOutputProvider(device, _context);
            _manager.RegisterProvider(provider);
        }
    }

    /// <summary>
    /// Start listening for BT device connection/disconnection.
    /// </summary>
    public void StartListening()
    {
        _receiver = new BluetoothOutputReceiver(this);
        var filter = new IntentFilter();
        filter.AddAction(BluetoothDevice.ActionAclConnected);
        filter.AddAction(BluetoothDevice.ActionAclDisconnected);
        _context.RegisterReceiver(_receiver, filter);
    }

    public void StopListening()
    {
        if (_receiver is not null)
        {
            _context.UnregisterReceiver(_receiver);
            _receiver = null;
        }
    }

    public void Dispose()
    {
        StopListening();
    }

    /// <summary>
    /// BroadcastReceiver for BT connection state changes.
    /// </summary>
    private class BluetoothOutputReceiver : BroadcastReceiver
    {
        private readonly AndroidBluetoothOutputEnumerator _owner;

        public BluetoothOutputReceiver(AndroidBluetoothOutputEnumerator owner)
            => _owner = owner;

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent is null) return;

            switch (intent.Action)
            {
                case BluetoothDevice.ActionAclConnected:
                    // New BT device connected — rescan to pick up A2DP output
                    _owner.ScanAndRegister();
                    break;

                case BluetoothDevice.ActionAclDisconnected:
                    // BT device disconnected — the provider's AudioDeviceCallback
                    // fires Disconnected, which triggers AudioOutputManager fallback.
                    // Rescan to remove stale providers.
                    _owner.ScanAndRegister();
                    break;
            }
        }
    }
}
```

Key differences from input enumerator (`AndroidBluetoothEnumerator`):
- Uses `AudioManager.GetDevices(Outputs)` instead of `BluetoothAdapter.BondedDevices`
  — the `AudioDeviceInfo` objects have the device ID needed for `SetPreferredDevice`.
- Filters by `BluetoothA2dp` / `BluetoothSco` audio device types (not HFP UUIDs).
- Listens to `ActionAclConnected` / `ActionAclDisconnected` (general BT ACL events)
  rather than `ActionConnectionStateChanged` (HFP-specific). A2DP devices may not
  advertise HFP UUIDs.
- No additional Android permissions needed — `BLUETOOTH_CONNECT` was already added
  in M12 Phase 2 for BT input.

**Verify:** Build succeeds on Android target. Both provider and enumerator compile.
Neither integrated into DI yet.

---

### Wave 4: DI Registration + Settings UI Refresh

**4.1 — Register BT output enumerators in `MauiProgram.cs`**

Add alongside existing BT input enumerator registrations:

```csharp
// After existing BT input enumerator registration
#if WINDOWS
builder.Services.AddSingleton<WindowsBluetoothOutputEnumerator>();
#elif ANDROID
builder.Services.AddSingleton<AndroidBluetoothOutputEnumerator>();
#endif
```

**4.2 — Initialize BT output enumerator in `MainPage.xaml.cs`**

Add BT output init alongside existing BT input init in the `Loaded` handler:

```csharp
// After BT input enumerator init
#if WINDOWS
var btOutputEnum = services.GetRequiredService<WindowsBluetoothOutputEnumerator>();
btOutputEnum.ScanAndRegister();
btOutputEnum.StartListening();
#elif ANDROID
var btOutputEnum = services.GetRequiredService<AndroidBluetoothOutputEnumerator>();
btOutputEnum.ScanAndRegister();
btOutputEnum.StartListening();
#endif
```

**4.3 — Subscribe to `ProvidersChanged` in `SettingsViewModel`**

Add alongside existing `AudioInputManager.ProvidersChanged` subscription:

```csharp
// In SettingsViewModel constructor
_audioOutputManager.ProvidersChanged += (_, _) =>
{
    OnPropertyChanged(nameof(AudioOutputProviders));
    OnPropertyChanged(nameof(SelectedAudioOutputProvider));
};
```

**Verify:** App starts. Settings page shows BT output devices when paired.
Selecting a BT device routes AI voice output through it. Disconnecting the BT
device falls back to system/phone speaker. Reconnecting re-appears in the picker.

---

## Test Plan

### Unit Tests (BodyCam.Tests)

| Test | Validates |
|------|-----------|
| `AudioOutputManager_RegisterProvider_AddsToList` | Provider appears in `Providers` after registration |
| `AudioOutputManager_RegisterProvider_IgnoresDuplicate` | Same ProviderId not added twice |
| `AudioOutputManager_RegisterProvider_AutoSwitches` | Auto-switches when saved pref matches and current is platform |
| `AudioOutputManager_UnregisterProvider_FallsBack` | Falls back to platform when active provider unregistered |
| `AudioOutputManager_UnregisterProvider_DisposesProvider` | `DisposeAsync` called on removed provider |
| `AudioOutputManager_ProvidersChanged_FiresOnRegister` | Event fires when provider registered |
| `AudioOutputManager_ProvidersChanged_FiresOnUnregister` | Event fires when provider unregistered |
| `AudioOutputManager_UnregisterProvider_Noop_WhenNotFound` | No exception when unregistering unknown provider |

### Integration Tests (manual)

| Scenario | Expected |
|----------|----------|
| Pair BT earbuds → open settings | BT device appears in output picker |
| Select BT output → start session → AI responds | Audio plays through BT device |
| Disconnect BT during playback | Audio falls back to system/phone speaker within ~500ms |
| Reconnect BT → check settings | BT device re-appears in picker |
| Select BT output → restart app | BT output restored from saved preference |
| Interrupt AI mid-speech with BT output active | ClearBuffer stops playback immediately |

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| WASAPI shared mode doesn't work with some BT drivers | Fall back to exclusive mode or WaveOutEvent |
| BT A2DP latency too high for conversational AI | Acceptable for speech output (45-85ms additional). HFP profile coordination deferred to Phase 4 |
| Android `SetPreferredDevice` silently fails | Check `AudioTrack.RoutedDevice` after play to verify routing took effect |
| Disconnect callback fires late on Android | Both `AudioDeviceCallback` and `BroadcastReceiver` provide redundant detection |
| Provider ProviderId collision between input and output | Input uses `"bt:{id}"`, output uses `"bt-out:{id}"` — distinct namespaces |
| Same physical BT device for both input and output | Each provider type manages its own routing. Profile conflicts (A2DP vs HFP) deferred to Phase 4 |

---

## Exit Criteria

- [ ] `AudioOutputManager` supports `RegisterProvider`, `UnregisterProviderAsync`, `ProvidersChanged`
- [ ] `WindowsBluetoothAudioOutputProvider` plays PCM through BT render endpoint via WASAPI
- [ ] `WindowsBluetoothOutputEnumerator` discovers BT render endpoints + hot-plug
- [ ] `AndroidBluetoothAudioOutputProvider` plays PCM through BT device via AudioTrack + SetPreferredDevice
- [ ] `AndroidBluetoothOutputEnumerator` discovers BT output devices + BroadcastReceiver
- [ ] Settings audio output picker shows BT devices when paired
- [ ] Auto-switch to saved BT preference when BT device connects
- [ ] Fallback to platform speaker when BT device disconnects
- [ ] ClearBuffer (interruption handling) works through BT output providers
- [ ] Build succeeds on both Windows and Android targets
- [ ] 303+ existing tests pass (no regressions)
- [ ] Hot-plug unit tests pass
