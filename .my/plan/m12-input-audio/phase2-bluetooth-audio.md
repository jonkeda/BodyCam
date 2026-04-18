# M12 Phase 2 — Bluetooth Audio

**Status:** COMPLETE  
**Prerequisite:** M12 Phase 1 (Audio Input Abstraction) — `IAudioInputProvider`,
`AudioInputManager`, `PlatformMicProvider` all wired into DI  
**Goal:** Implement `BluetoothAudioProvider` for BT HFP/SCO devices on Windows and
Android. Device enumeration, auto-connect to paired glasses, fallback on disconnect,
and SCO-to-app-rate resampling.

---

## Current State (After Phase 1)

| Component | Status |
|-----------|--------|
| `IAudioInputProvider` | Defined, implemented by `PlatformMicProvider` |
| `AudioInputManager` | Active, manages one provider, `IAudioInputService` backward compat |
| Settings audio picker | Shows "System Microphone" / "Phone Microphone" only |
| `AudioInputManager.RegisterProvider()` | Not yet implemented (Phase 1 uses DI-injected `IEnumerable<IAudioInputProvider>`) |

**Key gap:** The current `AudioInputManager` takes `IEnumerable<IAudioInputProvider>`
via constructor DI. BT devices arrive dynamically — after app startup, when a headset
is paired/connected. We need a `RegisterProvider` / `UnregisterProviderAsync` API on
`AudioInputManager` to support hot-plug.

---

## Deliverables

### New Files

| File | Purpose |
|------|---------|
| `Platforms/Windows/Audio/WindowsBluetoothAudioProvider.cs` | BT HFP capture via WasapiCapture on BT endpoint |
| `Platforms/Android/Audio/AndroidBluetoothAudioProvider.cs` | BT SCO capture via AudioRecord with SCO routing |
| `Platforms/Android/Audio/ScoStateReceiver.cs` | BroadcastReceiver for SCO connection state |
| `Services/Audio/AudioResampler.cs` | Resample PCM16 between sample rates (e.g. 16kHz→24kHz) |
| `Services/Audio/BluetoothDeviceInfo.cs` | Lightweight record for BT device metadata |
| `Platforms/Windows/Audio/WindowsBluetoothEnumerator.cs` | Discovers BT audio endpoints via MMDevice API |
| `Platforms/Android/Audio/AndroidBluetoothEnumerator.cs` | Discovers paired BT HFP devices + connection listener |

### Modified Files

| File | Change |
|------|--------|
| `Services/Audio/AudioInputManager.cs` | Add `RegisterProvider()`, `UnregisterProviderAsync()`, auto-switch on late-connect |
| `MauiProgram.cs` | Register BT enumerators in DI |
| `Platforms/Android/AndroidManifest.xml` | Add `BLUETOOTH_CONNECT`, `MODIFY_AUDIO_SETTINGS` permissions |
| `ViewModels/SettingsViewModel.cs` | Refresh picker when BT device connects/disconnects |

### Unchanged Files

| File | Why Unchanged |
|------|---------------|
| `Agents/VoiceInputAgent.cs` | Consumes `IAudioInputService` — `AudioInputManager` handles switching |
| `Services/MicrophoneCoordinator.cs` | No awareness of provider source |
| `Orchestration/AgentOrchestrator.cs` | Audio handled downstream |

---

## Implementation Waves

### Wave 1: AudioResampler + AudioInputManager Hot-Plug

Create the audio resampling utility and extend `AudioInputManager` with dynamic
provider registration — no BT code yet.

**1.1 — Create `AudioResampler`**

BT SCO audio is 8kHz (CVSD) or 16kHz (mSBC). The app runs at 24kHz. We need a
simple PCM16 resampler that doesn't depend on platform APIs (so it's testable).

```csharp
// Services/Audio/AudioResampler.cs
namespace BodyCam.Services.Audio;

/// <summary>
/// Resamples 16-bit signed PCM mono between sample rates using linear interpolation.
/// Suitable for voice-quality audio (not music). Zero allocations beyond the output buffer.
/// </summary>
public static class AudioResampler
{
    /// <summary>
    /// Resample PCM16 mono audio from <paramref name="sourceRate"/> to <paramref name="targetRate"/>.
    /// </summary>
    public static byte[] Resample(byte[] pcm16, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate)
            return pcm16;

        int sourceSamples = pcm16.Length / 2;
        int targetSamples = (int)((long)sourceSamples * targetRate / sourceRate);
        var output = new byte[targetSamples * 2];

        double ratio = (double)(sourceSamples - 1) / (targetSamples - 1);

        for (int i = 0; i < targetSamples; i++)
        {
            double srcPos = i * ratio;
            int srcIdx = (int)srcPos;
            double frac = srcPos - srcIdx;

            short s0 = BitConverter.ToInt16(pcm16, srcIdx * 2);
            short s1 = (srcIdx + 1 < sourceSamples)
                ? BitConverter.ToInt16(pcm16, (srcIdx + 1) * 2)
                : s0;

            short interpolated = (short)(s0 + (s1 - s0) * frac);
            BitConverter.TryWriteBytes(output.AsSpan(i * 2), interpolated);
        }

        return output;
    }
}
```

**1.2 — Add dynamic registration to `AudioInputManager`**

```csharp
// Add to AudioInputManager.cs

private readonly List<IAudioInputProvider> _dynamicProviders = new();

public IReadOnlyList<IAudioInputProvider> Providers
    => _providers.Concat(_dynamicProviders).ToList().AsReadOnly();

/// <summary>
/// Register a dynamically discovered provider (e.g. BT device connected after startup).
/// Auto-switches if this is the user's saved preference and the current active is "platform".
/// </summary>
public void RegisterProvider(IAudioInputProvider provider)
{
    if (Providers.Any(p => p.ProviderId == provider.ProviderId))
        return;

    _dynamicProviders.Add(provider);

    // Auto-switch if user's saved preference matches and we're on platform fallback
    if (provider.ProviderId == _settings.ActiveAudioInputProvider
        && (_active is null || _active.ProviderId == "platform"))
    {
        _ = SetActiveAsync(provider.ProviderId);
    }
}

/// <summary>
/// Remove a dynamically discovered provider (e.g. BT device disconnected).
/// If it was active, falls back to platform.
/// </summary>
public async Task UnregisterProviderAsync(string providerId)
{
    var provider = _dynamicProviders.FirstOrDefault(p => p.ProviderId == providerId);
    if (provider is null) return;

    if (_active?.ProviderId == providerId)
    {
        await FallbackToPlatformAsync();
    }

    _dynamicProviders.Remove(provider);
    await provider.DisposeAsync();
}
```

**1.3 — Add `BluetoothDeviceInfo` record**

```csharp
// Services/Audio/BluetoothDeviceInfo.cs
namespace BodyCam.Services.Audio;

/// <summary>
/// Lightweight metadata for a discovered Bluetooth audio device.
/// </summary>
public record BluetoothDeviceInfo
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public required string ProviderId { get; init; }
    public bool IsConnected { get; init; }
}
```

**Verify:** Build succeeds. `AudioInputManager` compiles with new methods.
Existing DI behavior unchanged (providers from `IEnumerable` still work).

---

### Wave 2: Windows Bluetooth Audio

**2.1 — Create `WindowsBluetoothAudioProvider`**

Captures audio from a BT HFP endpoint using WasapiCapture. BT devices on Windows
appear as standard MMDevice audio endpoints.

```csharp
// Platforms/Windows/Audio/WindowsBluetoothAudioProvider.cs
namespace BodyCam.Platforms.Windows.Audio;

public class WindowsBluetoothAudioProvider : IAudioInputProvider, IDisposable
{
    private readonly MMDevice _device;
    private readonly AppSettings _settings;
    private WasapiCapture? _capture;
    private readonly WaveFormat _targetFormat;

    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable => _device.State == DeviceState.Active;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    // Constructor takes MMDevice + AppSettings
    // StartAsync() creates WasapiCapture, DataAvailable resamples SCO→app rate
    // StopAsync() stops capture
    // OnRecordingStopped — fires Disconnected on error
}
```

Key implementation details:
- WasapiCapture auto-detects the BT device's native format (likely 16kHz mono mSBC)
- `OnDataAvailable` converts to app format: channel downmix if needed, resample
  via `AudioResampler.Resample()` to `_settings.SampleRate`
- `OnRecordingStopped` with non-null exception → fire `Disconnected` event

See [bt-audio.md](bt-audio.md) for full implementation code.

**2.2 — Create `WindowsBluetoothEnumerator`**

Discovers BT audio capture endpoints via NAudio's `MMDeviceEnumerator` and registers
them with `AudioInputManager`.

```csharp
// Platforms/Windows/Audio/WindowsBluetoothEnumerator.cs
namespace BodyCam.Platforms.Windows.Audio;

public class WindowsBluetoothEnumerator : IDisposable
{
    private readonly AudioInputManager _manager;
    private readonly AppSettings _settings;
    private readonly MMDeviceEnumerator _enumerator;
    private NotificationClient? _notificationClient;

    public WindowsBluetoothEnumerator(AudioInputManager manager, AppSettings settings)
    {
        _manager = manager;
        _settings = settings;
        _enumerator = new MMDeviceEnumerator();
    }

    /// <summary>
    /// Scan active capture endpoints for BT devices and register them.
    /// </summary>
    public void ScanAndRegister()
    {
        var devices = _enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

        foreach (var device in devices)
        {
            if (!IsBluetoothDevice(device)) continue;

            var providerId = $"bt:{device.ID}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            var provider = new WindowsBluetoothAudioProvider(device, _settings);
            _manager.RegisterProvider(provider);
        }
    }

    /// <summary>
    /// Start listening for device connect/disconnect events.
    /// </summary>
    public void StartListening()
    {
        _notificationClient = new NotificationClient(this);
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

    // MMDevice notification callbacks → ScanAndRegister on add,
    // UnregisterProviderAsync on remove
    private class NotificationClient : IMMNotificationClient { ... }
}
```

Detection logic: BT device IDs on Windows contain `BTHENUM` in the device path.
This reliably distinguishes BT from USB/built-in audio devices.

**2.3 — Register in DI (Windows)**

```csharp
// MauiProgram.cs — inside #if WINDOWS block
builder.Services.AddSingleton<WindowsBluetoothEnumerator>();
```

Initialize in app startup after `AudioInputManager.InitializeAsync()`:

```csharp
// MainPage.xaml.cs Loaded handler (after audioInputManager.InitializeAsync())
var btEnum = app.Services.GetService<WindowsBluetoothEnumerator>();
btEnum?.ScanAndRegister();
btEnum?.StartListening();
```

**Verify:** With a BT headset paired on Windows:
- Settings picker shows "BT: {Device Name}" in addition to "System Microphone"
- Selecting BT device → audio captured from BT mic
- Disconnecting BT headset → automatic fallback to system mic

---

### Wave 3: Android Bluetooth Audio

**3.1 — Add Android permissions**

```xml
<!-- Platforms/Android/AndroidManifest.xml -->
<uses-permission android:name="android.permission.BLUETOOTH" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
<uses-permission android:name="android.permission.MODIFY_AUDIO_SETTINGS" />
```

`BLUETOOTH_CONNECT` requires runtime permission on Android 12+ (API 31):

```csharp
if (OperatingSystem.IsAndroidVersionAtLeast(31))
{
    var status = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
    if (status != PermissionStatus.Granted)
        status = await Permissions.RequestAsync<Permissions.Bluetooth>();
}
```

**3.2 — Create `ScoStateReceiver`**

```csharp
// Platforms/Android/Audio/ScoStateReceiver.cs
namespace BodyCam.Platforms.Android.Audio;

/// <summary>
/// BroadcastReceiver that completes a TaskCompletionSource when BT SCO connects.
/// Used by AndroidBluetoothAudioProvider to wait for SCO link before recording.
/// </summary>
internal class ScoStateReceiver : BroadcastReceiver
{
    private readonly TaskCompletionSource _tcs;

    public ScoStateReceiver(TaskCompletionSource tcs) => _tcs = tcs;

    public override void OnReceive(Context? context, Intent? intent)
    {
        var state = intent?.GetIntExtra(AudioManager.ExtraScoAudioState, -1);
        if (state == (int)ScoAudioState.Connected)
            _tcs.TrySetResult();
        else if (state == (int)ScoAudioState.Error)
            _tcs.TrySetException(new InvalidOperationException("BT SCO connection failed."));
    }
}
```

**3.3 — Create `AndroidBluetoothAudioProvider`**

Routes audio to BT SCO and captures via AudioRecord.

Key differences from `PlatformMicProvider`:
- `StartAsync` requests BT SCO routing before creating AudioRecord
- Android 12+: `AudioManager.SetCommunicationDevice()` (new API)
- Android <12: `AudioManager.StartBluetoothSco()` + wait for `ScoStateReceiver`
- SCO audio is 8kHz (CVSD) or 16kHz (mSBC) — resample via `AudioResampler`
- `StopAsync` releases SCO routing via `ClearCommunicationDevice()` or `StopBluetoothSco()`
- Enable `NoiseSuppressor` and `AcousticEchoCanceler` AudioEffects if available

```csharp
// Platforms/Android/Audio/AndroidBluetoothAudioProvider.cs
namespace BodyCam.Platforms.Android.Audio;

public class AndroidBluetoothAudioProvider : IAudioInputProvider, IDisposable
{
    private readonly AppSettings _settings;
    private readonly Context _context;
    private readonly BluetoothDevice _btDevice;
    private AudioRecord? _audioRecord;

    public string DisplayName { get; }       // "BT: {device.Name}"
    public string ProviderId { get; }         // "bt:{device.Address}"
    public bool IsAvailable { get; private set; } = true;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    // StartAsync: request permissions → route SCO → create AudioRecord → start loop
    // RecordLoopAsync: read buffer → resample → emit AudioChunkAvailable
    // StopAsync: cancel loop → stop AudioRecord → release SCO
}
```

See [bt-audio.md](bt-audio.md) for full implementation.

**3.4 — Create `AndroidBluetoothEnumerator`**

```csharp
// Platforms/Android/Audio/AndroidBluetoothEnumerator.cs
namespace BodyCam.Platforms.Android.Audio;

public class AndroidBluetoothEnumerator : IDisposable
{
    private readonly AudioInputManager _manager;
    private readonly AppSettings _settings;
    private readonly Context _context;
    private BluetoothHeadsetReceiver? _receiver;

    // ScanAndRegister: enumerate BluetoothAdapter.BondedDevices, filter by HFP UUID
    // StartListening: register BroadcastReceiver for ActionConnectionStateChanged
    // StopListening: unregister receiver
}
```

HFP device detection uses UUID prefix matching:
- `0000111e` — Handsfree (HFP)
- `0000111f` — Handsfree Audio Gateway
- `00001108` — Headset (HSP)

**3.5 — Register in DI (Android)**

```csharp
// MauiProgram.cs — inside #elif ANDROID block
builder.Services.AddSingleton<AndroidBluetoothEnumerator>();
```

**Verify:** With a BT headset paired on Android:
- Settings picker shows "BT: {Device Name}"
- Audio routed through SCO when BT device selected
- Fallback to phone mic on disconnect

---

### Wave 4: Settings UI Refresh + Auto-Connect

**4.1 — Refresh audio picker on device change**

`AudioInputManager` needs an event for when providers change:

```csharp
// Add to AudioInputManager
public event EventHandler? ProvidersChanged;

// In RegisterProvider():
ProvidersChanged?.Invoke(this, EventArgs.Empty);

// In UnregisterProviderAsync():
ProvidersChanged?.Invoke(this, EventArgs.Empty);
```

`SettingsViewModel` subscribes and refreshes the picker:

```csharp
// SettingsViewModel constructor
_audioInputManager.ProvidersChanged += (_, _) =>
{
    OnPropertyChanged(nameof(AudioInputProviders));
    OnPropertyChanged(nameof(SelectedAudioInputProvider));
};
```

**4.2 — Auto-connect on late BT arrival**

Already implemented in Wave 1.2 via `RegisterProvider()` auto-switch logic:
when a BT device connects that matches the user's saved preference, and the
current active is "platform" (fallback), auto-switch to the BT device.

**4.3 — Connection status indicator (optional)**

Add a visual indicator in the settings page showing connection state:

```xml
<HorizontalStackLayout>
    <Label Text="{Binding ActiveAudioInputName}" FontSize="14" />
    <Label Text="●" FontSize="12"
           TextColor="{Binding ActiveAudioInputConnected,
                       Converter={StaticResource BoolToColorConverter}}" />
</HorizontalStackLayout>
```

**Verify:** 
- Pair BT headset while app is running → appears in picker
- Unpair/disconnect → removed from picker, falls back to system mic
- Restart app with BT headset preference saved → auto-connects on startup

---

## Audio Quality & Format Handling

### SCO Sample Rate Matrix

| HFP Version | Codec | Native Rate | Resampled To | Quality |
|-------------|-------|-------------|--------------|---------|
| 1.5 | CVSD | 8 kHz | 24 kHz | Telephone quality |
| 1.6+ | mSBC | 16 kHz | 24 kHz | Wideband speech |
| 1.8+ | LC3 | 32 kHz | 24 kHz | Super-wideband (downsample) |

Resampling is handled by `AudioResampler.Resample()` — linear interpolation,
sufficient for voice (not music). All output matches `AppSettings.SampleRate`.

### Echo Cancellation

| Platform | Mechanism | Notes |
|----------|-----------|-------|
| Android | `AudioSource.VoiceCommunication` enables AEC | Also enables noise suppression |
| Android | `AcousticEchoCanceler` AudioEffect | Explicit backup if VoiceCommunication not enough |
| Windows | Audio driver DSP | WasapiCapture with system processing enabled (default) |

### Audio Format Invariant

All providers must emit PCM16 mono at `AppSettings.SampleRate` (default 24000).
BT providers handle the conversion internally — consumers never see 8kHz or 16kHz data.

---

## Error Handling

| Scenario | Behavior |
|----------|----------|
| BT device unavailable at startup | Skip, use platform fallback, log warning |
| SCO connection timeout (>5s) | Throw `InvalidOperationException`, caller handles fallback |
| BT device disconnects mid-capture | `OnRecordingStopped` fires `Disconnected`, manager falls back |
| Permission denied (Android BT) | Throw `PermissionException`, provider stays unavailable |
| No HFP-capable BT devices found | No providers registered, picker shows platform only |
| Resampler receives empty buffer | Return empty array, no event emitted |

---

## Test Plan

### Unit Tests (BodyCam.Tests)

| Test | Validates |
|------|-----------|
| `AudioResampler_16kTo24k_CorrectLength` | Output sample count = input * (24000/16000) |
| `AudioResampler_8kTo24k_CorrectLength` | Output sample count = input * (24000/8000) |
| `AudioResampler_SameRate_PassThrough` | Returns input unchanged |
| `AudioResampler_PreservesSilence` | All-zero input → all-zero output |
| `AudioResampler_PreservesSineWave` | Known sine wave at 1kHz, verify frequency preserved |
| `AudioInputManager_RegisterProvider_AddsToList` | New provider appears in `Providers` |
| `AudioInputManager_RegisterProvider_IgnoresDuplicate` | Same `ProviderId` not added twice |
| `AudioInputManager_RegisterProvider_AutoSwitches` | Saved pref matches, platform active → switches |
| `AudioInputManager_UnregisterProvider_FallsBack` | Active provider removed → platform |
| `AudioInputManager_UnregisterProvider_DisposesProvider` | `DisposeAsync` called on removed provider |
| `AudioInputManager_ProvidersChanged_FiresOnRegister` | Event fires when provider added |
| `AudioInputManager_ProvidersChanged_FiresOnUnregister` | Event fires when provider removed |

### Integration Tests (Manual — Requires BT Hardware)

| Scenario | Expected |
|----------|----------|
| Pair BT headset → open app → check picker | BT device listed alongside system mic |
| Select BT device → start Realtime session → speak | Audio from BT mic, transcribed correctly |
| Mid-session: turn off BT headset | Automatic fallback to system mic, session continues |
| Start app without BT → pair mid-session | BT device appears in picker, auto-switches if saved pref |
| Select BT → stop session → restart app | BT device re-selected automatically on startup |
| Android: deny BT permission → select BT device | Error shown, stays on platform mic |

### Regression

All Phase 1 tests continue to pass. Existing voice pipeline works with platform mic
when no BT device is available. No changes to `VoiceInputAgent` or `MicrophoneCoordinator`.

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| `WasapiCapture` format mismatch on BT endpoint | Use device's `MixFormat`, resample output |
| SCO not connecting on some Android devices | 5-second timeout, log warning, fallback |
| `RegisterProvider` called from BroadcastReceiver thread | Manager methods are thread-safe (lock on `_dynamicProviders`) |
| Hot-plug causes picker UI flicker | Debounce `ProvidersChanged` event (50ms) |
| Resampler aliasing on 8kHz→24kHz | Acceptable for voice — speech bandwidth is <4kHz anyway |
| Different BT stack behavior per Android OEM | Test on multiple devices; fall back gracefully |
| `AudioResampler` performance | Linear interpolation is O(n), negligible for 50ms chunks |

---

## Exit Criteria

- [ ] `AudioResampler` resamples PCM16 between arbitrary sample rates
- [ ] `AudioInputManager` supports dynamic `RegisterProvider` / `UnregisterProviderAsync`
- [ ] `AudioInputManager.ProvidersChanged` event fires on register/unregister
- [ ] Windows: `WindowsBluetoothAudioProvider` captures audio from BT HFP endpoint
- [ ] Windows: `WindowsBluetoothEnumerator` discovers BT audio devices via MMDevice API
- [ ] Windows: BT device connect/disconnect detected and picker updated
- [ ] Android: `AndroidBluetoothAudioProvider` captures audio via SCO routing
- [ ] Android: `AndroidBluetoothEnumerator` discovers paired HFP devices
- [ ] Android: BT permissions requested at runtime (API 31+)
- [ ] Android: SCO audio resampled from 8/16kHz to app sample rate
- [ ] Auto-connect to saved BT preference on app startup
- [ ] Auto-switch on late BT connect if saved preference matches
- [ ] Fallback to platform mic on BT disconnect
- [ ] Settings picker refreshes when BT devices appear/disappear
- [ ] All Phase 1 tests pass (no regression)
- [ ] AudioResampler unit tests pass
- [ ] Dynamic registration unit tests pass
