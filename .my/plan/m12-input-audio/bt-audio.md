# M12 — Bluetooth Audio Input

## Goal

Support Bluetooth microphones (smart glasses, BT headsets) as audio input sources.
BT audio devices use the HFP (Hands-Free Profile) for bidirectional voice audio,
with SCO (Synchronous Connection-Oriented) links for the actual audio data.

---

## Bluetooth Audio Fundamentals

### Profiles

| Profile | Full Name | Audio | Direction | Use Case |
|---------|-----------|-------|-----------|----------|
| **HFP** | Hands-Free Profile | 8/16kHz mono | Bidirectional | Phone calls, voice assistant |
| **A2DP** | Advanced Audio Distribution | Up to 48kHz stereo | One-way (playback) | Music, media |
| **HSP** | Headset Profile | 8kHz mono | Bidirectional | Legacy headsets |

**We need HFP** for microphone input. A2DP is playback-only and doesn't provide mic
access. When we route audio to a BT device via HFP/SCO, the OS switches from A2DP
(high quality playback) to HFP (lower quality bidirectional) — this is expected behavior.

### SCO Audio Format

HFP SCO audio is typically:
- **CVSD codec**: 8kHz, 8-bit (HFP 1.5 and earlier)
- **mSBC codec**: 16kHz, 16-bit mono (HFP 1.6+ "Wideband Speech")

Both are significantly lower quality than the default 24kHz we configure. The provider
must resample from 8kHz or 16kHz to the app's configured sample rate using
`AudioResampler`.

---

## Windows — BluetoothAudioProvider

On Windows, BT audio devices appear as standard audio endpoints in the MMDevice API.
When a BT headset connects via HFP, Windows creates a capture endpoint for it.

### Implementation

```csharp
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BodyCam.Services.Audio;

/// <summary>
/// Audio input from a Bluetooth HFP device on Windows.
/// Uses NAudio MMDevice API to capture from the BT SCO endpoint.
/// </summary>
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

    public WindowsBluetoothAudioProvider(MMDevice device, AppSettings settings)
    {
        _device = device;
        _settings = settings;
        DisplayName = $"BT: {device.FriendlyName}";
        ProviderId = $"bt:{device.ID}";
        _targetFormat = new WaveFormat(settings.SampleRate, 16, 1);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return Task.CompletedTask;

        // WasapiCapture works with BT HFP endpoints
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

        // BT SCO audio is typically 8kHz or 16kHz — resample to target
        var chunk = ConvertToTargetFormat(e.Buffer, e.BytesRecorded);
        if (chunk.Length > 0)
            AudioChunkAvailable?.Invoke(this, chunk);
    }

    private byte[] ConvertToTargetFormat(byte[] buffer, int bytesRecorded)
    {
        var sourceFormat = _capture!.WaveFormat;

        if (sourceFormat.SampleRate == _targetFormat.SampleRate
            && sourceFormat.BitsPerSample == 16
            && sourceFormat.Channels == 1)
        {
            var chunk = new byte[bytesRecorded];
            Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRecorded);
            return chunk;
        }

        // Resample BT SCO audio to app sample rate
        using var inputStream = new RawSourceWaveStream(
            new MemoryStream(buffer, 0, bytesRecorded), sourceFormat);
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

### Windows BT Device Detection

BT audio endpoints can be detected via the same `WindowsAudioDeviceEnumerator`
from [platform-providers.md](platform-providers.md). BT devices can be distinguished
from USB/built-in devices by inspecting the `EndpointFormFactor` property:

```csharp
/// <summary>
/// Determines if an MMDevice is a Bluetooth audio device.
/// </summary>
private static bool IsBluetoothDevice(MMDevice device)
{
    try
    {
        // Check the device interface path for Bluetooth identifiers
        var id = device.ID ?? string.Empty;
        return id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
        return false;
    }
}
```

Update `WindowsAudioDeviceEnumerator.ScanAndRegister()` to create either
`WindowsBluetoothAudioProvider` or `WindowsUsbAudioProvider` based on this check:

```csharp
public void ScanAndRegister()
{
    var devices = _enumerator
        .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

    foreach (var device in devices)
    {
        // Skip if already registered
        var isbt = IsBluetoothDevice(device);
        var providerId = isbt ? $"bt:{device.ID}" : $"usb:{device.ID}";

        if (_manager.Providers.Any(p => p.ProviderId == providerId))
            continue;

        IAudioInputProvider provider = isbt
            ? new WindowsBluetoothAudioProvider(device, _settings)
            : new WindowsUsbAudioProvider(device, _settings);

        _manager.RegisterProvider(provider);
    }
}
```

---

## Android — BluetoothAudioProvider

On Android, Bluetooth audio routing uses `AudioManager` and `BluetoothHeadset` profile.
The key challenge is switching the audio system from A2DP to SCO for mic access.

### Implementation

```csharp
using Android.Bluetooth;
using Android.Content;
using Android.Media;
using BodyCam.Services;

namespace BodyCam.Services.Audio;

/// <summary>
/// Audio input from a Bluetooth HFP device on Android.
/// Routes audio through BT SCO and captures via AudioRecord.
/// </summary>
public class AndroidBluetoothAudioProvider : IAudioInputProvider, IDisposable
{
    private readonly AppSettings _settings;
    private readonly Context _context;
    private AudioRecord? _audioRecord;
    private CancellationTokenSource? _recordCts;
    private Task? _recordTask;
    private BluetoothDevice? _btDevice;

    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable { get; private set; }
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public AndroidBluetoothAudioProvider(
        BluetoothDevice btDevice,
        AppSettings settings,
        Context context)
    {
        _btDevice = btDevice;
        _settings = settings;
        _context = context;
        DisplayName = $"BT: {btDevice.Name ?? "Unknown"}";
        ProviderId = $"bt:{btDevice.Address}";
        IsAvailable = true;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return;

        // Request permissions
        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Microphone>();
            if (status != PermissionStatus.Granted)
                throw new PermissionException("Microphone permission denied.");
        }

        var audioManager = (AudioManager)_context
            .GetSystemService(Context.AudioService)!;

        // Route audio to BT SCO
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            // Android 12+: Use setCommunicationDevice
            var devices = audioManager.GetDevices(AudioDeviceType.Input);
            var btInputDevice = devices.FirstOrDefault(d =>
                d.Type == AudioDeviceType.BluetoothSco
                && d.Address == _btDevice?.Address);

            if (btInputDevice is not null)
                audioManager.SetCommunicationDevice(btInputDevice);
            else
                throw new InvalidOperationException("BT audio device not found as input.");
        }
        else
        {
            // Android <12: Use legacy SCO APIs
            audioManager.BluetoothScoOn = true;
            audioManager.StartBluetoothSco();

            // Wait for SCO connection
            await WaitForScoConnectionAsync(ct);
        }

        // SCO audio is typically 8kHz or 16kHz
        int scoSampleRate = 16000; // mSBC (HFP 1.6+)
        int bufferSize = AudioRecord.GetMinBufferSize(
            scoSampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit);

        _audioRecord = new AudioRecord(
            AudioSource.VoiceCommunication,
            scoSampleRate,
            ChannelIn.Mono,
            Encoding.Pcm16bit,
            bufferSize);

        _audioRecord.StartRecording();
        IsCapturing = true;

        _recordCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _recordTask = Task.Run(() => RecordLoopAsync(scoSampleRate, _recordCts.Token));
    }

    private async Task RecordLoopAsync(int sourceSampleRate, CancellationToken ct)
    {
        int chunkBytes = sourceSampleRate * 2 * _settings.ChunkDurationMs / 1000;
        var buffer = new byte[chunkBytes];

        while (!ct.IsCancellationRequested
            && _audioRecord?.RecordingState == RecordState.Recording)
        {
            int bytesRead = await _audioRecord.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                var chunk = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);

                // Resample from SCO rate to app rate
                if (sourceSampleRate != _settings.SampleRate)
                    chunk = AudioResampler.Resample(chunk, sourceSampleRate, _settings.SampleRate);

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

        // Release SCO
        var audioManager = (AudioManager)_context
            .GetSystemService(Context.AudioService)!;

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            audioManager.ClearCommunicationDevice();
        }
        else
        {
            audioManager.StopBluetoothSco();
            audioManager.BluetoothScoOn = false;
        }

        return Task.CompletedTask;
    }

    private async Task WaitForScoConnectionAsync(CancellationToken ct)
    {
        // Wait up to 5 seconds for SCO to connect
        var tcs = new TaskCompletionSource();
        var receiver = new ScoStateReceiver(tcs);

        var filter = new IntentFilter(AudioManager.ActionScoAudioStateUpdated);
        _context.RegisterReceiver(receiver, filter);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            timeoutCts.Token.Register(() => tcs.TrySetCanceled());

            await tcs.Task;
        }
        finally
        {
            _context.UnregisterReceiver(receiver);
        }
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

/// <summary>
/// BroadcastReceiver that completes when BT SCO audio is connected.
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

---

## BT Device Enumeration

### Windows

BT devices appear as audio endpoints via `MMDeviceEnumerator`. The
`WindowsAudioDeviceEnumerator` from [platform-providers.md](platform-providers.md)
handles this — BT devices are detected by their device ID path containing `BTHENUM`.

### Android

```csharp
using Android.Bluetooth;

namespace BodyCam.Services.Audio;

/// <summary>
/// Enumerates paired Bluetooth audio devices on Android
/// and registers them as IAudioInputProvider instances.
/// </summary>
public class AndroidBluetoothDeviceEnumerator
{
    private readonly AudioInputManager _manager;
    private readonly AppSettings _settings;
    private readonly Context _context;
    private BluetoothHeadsetReceiver? _receiver;

    public AndroidBluetoothDeviceEnumerator(
        AudioInputManager manager,
        AppSettings settings,
        Context context)
    {
        _manager = manager;
        _settings = settings;
        _context = context;
    }

    /// <summary>
    /// Scan paired BT devices for HFP-capable devices and register them.
    /// </summary>
    public void ScanAndRegister()
    {
        var adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter is null || !adapter.IsEnabled) return;

        var paired = adapter.BondedDevices ?? Enumerable.Empty<BluetoothDevice>();

        foreach (var device in paired)
        {
            // Check if device supports HFP (Handsfree or Headset UUID)
            var uuids = device.GetUuids();
            bool hasHfp = uuids?.Any(u =>
                u.Uuid?.ToString()?.StartsWith("0000111e", StringComparison.OrdinalIgnoreCase) == true // HFP
                || u.Uuid?.ToString()?.StartsWith("0000111f", StringComparison.OrdinalIgnoreCase) == true // HFP AG
                || u.Uuid?.ToString()?.StartsWith("00001108", StringComparison.OrdinalIgnoreCase) == true // HSP
            ) ?? false;

            if (!hasHfp) continue;

            var providerId = $"bt:{device.Address}";
            if (_manager.Providers.Any(p => p.ProviderId == providerId))
                continue;

            var provider = new AndroidBluetoothAudioProvider(device, _settings, _context);
            _manager.RegisterProvider(provider);
        }
    }

    /// <summary>
    /// Start listening for BT headset connect/disconnect events.
    /// </summary>
    public void StartListening()
    {
        _receiver = new BluetoothHeadsetReceiver(this);
        var filter = new IntentFilter();
        filter.AddAction(BluetoothHeadset.ActionConnectionStateChanged);
        filter.AddAction(BluetoothDevice.ActionBondStateChanged);
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

    private class BluetoothHeadsetReceiver : BroadcastReceiver
    {
        private readonly AndroidBluetoothDeviceEnumerator _owner;

        public BluetoothHeadsetReceiver(AndroidBluetoothDeviceEnumerator owner)
            => _owner = owner;

        public override void OnReceive(Context? context, Intent? intent)
        {
            var action = intent?.Action;

            if (action == BluetoothHeadset.ActionConnectionStateChanged)
            {
                var state = intent!.GetIntExtra(BluetoothProfile.ExtraState, -1);
                var device = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice)
                    as BluetoothDevice;

                if (device is null) return;

                if (state == (int)ProfileState.Connected)
                {
                    _owner.ScanAndRegister();
                }
                else if (state == (int)ProfileState.Disconnected)
                {
                    var providerId = $"bt:{device.Address}";
                    _ = _owner._manager.UnregisterProviderAsync(providerId);
                }
            }
            else if (action == BluetoothDevice.ActionBondStateChanged)
            {
                // New device paired — rescan
                _owner.ScanAndRegister();
            }
        }
    }
}
```

---

## Auto-Connect to Paired Glasses

When BodyCam starts, it should automatically connect to the user's preferred BT audio
device (likely their smart glasses). This is driven by settings:

```csharp
// In AudioInputManager.InitializeAsync()
public async Task InitializeAsync(CancellationToken ct = default)
{
    // 1. Enumerate BT devices (registers providers)
    _btEnumerator?.ScanAndRegister();

    // 2. Restore saved provider
    var savedId = _settings.ActiveAudioInputProvider ?? "platform";

    // 3. If saved provider is a BT device, try to connect
    var provider = _providers.FirstOrDefault(p => p.ProviderId == savedId && p.IsAvailable);

    if (provider is null && savedId.StartsWith("bt:"))
    {
        // BT device not available — fall back to platform
        _logger.LogWarning("Saved BT device '{Id}' not available, using platform mic", savedId);
        provider = _providers.FirstOrDefault(p => p.ProviderId == "platform");
    }

    provider ??= _providers.FirstOrDefault(p => p.ProviderId == "platform");

    if (provider is not null)
        await SetActiveAsync(provider.ProviderId, ct);
}
```

### Late-Connect Scenario

If glasses connect _after_ app startup (e.g. user turns on glasses after app is running):

1. `BluetoothHeadsetReceiver.OnReceive()` fires with `ProfileState.Connected`
2. `ScanAndRegister()` registers the new `BluetoothAudioProvider`
3. If the saved `ActiveAudioInputProvider` matches the newly connected device,
   `AudioInputManager` could auto-switch:

```csharp
// In AudioInputManager.RegisterProvider()
public void RegisterProvider(IAudioInputProvider provider)
{
    if (_providers.Any(p => p.ProviderId == provider.ProviderId))
        return;

    _providers.Add(provider);
    _logger.LogInformation("Registered audio input: {Provider}", provider.DisplayName);

    // Auto-switch if this is the user's saved preference and no BT device is active
    if (provider.ProviderId == _settings.ActiveAudioInputProvider
        && (_active is null || _active.ProviderId == "platform"))
    {
        _ = SetActiveAsync(provider.ProviderId);
    }
}
```

---

## Audio Quality Considerations

### SCO Sample Rate Limitations

| HFP Version | Codec | Sample Rate | Quality |
|-------------|-------|-------------|---------|
| HFP 1.5 | CVSD | 8 kHz | Telephone quality |
| HFP 1.6+ | mSBC | 16 kHz | Wideband speech |
| HFP 1.8+ | LC3 | 32 kHz | Super-wideband |

Most smart glasses support HFP 1.6+ (mSBC at 16kHz). The provider resamples to
the app's configured rate (default 24kHz) using `AudioResampler`. This introduces
no new frequency content above 8kHz but ensures the sample rate matches what the
Realtime API expects.

### Echo Cancellation

When using BT glasses, echo from the glasses speakers can feed back into the glasses
mic. Android's `AudioSource.VoiceCommunication` enables AEC (Acoustic Echo Cancellation)
by default. On Windows, AEC is handled by the audio driver.

For cases where platform AEC is insufficient, consider:
- Using `AudioEffect` on Android to explicitly enable `AcousticEchoCanceler`
- NAudio's `WasapiCapture` with `AudioClientStreamFlags.EnableRawCapture` disabled
  (keeps system DSP including AEC)

### Noise Suppression

`AudioSource.VoiceCommunication` on Android also enables noise suppression. On Windows,
this is driver-dependent. Consider adding `NoiseSuppressor` AudioEffect on Android
explicitly:

```csharp
// After creating AudioRecord
if (NoiseSuppressor.IsAvailable)
{
    var ns = NoiseSuppressor.Create(_audioRecord.AudioSessionId);
    ns?.SetEnabled(true);
}
```

---

## Permissions

### Android

```xml
<!-- AndroidManifest.xml — already present for mic, add BT permissions -->
<uses-permission android:name="android.permission.RECORD_AUDIO" />
<uses-permission android:name="android.permission.BLUETOOTH" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
<uses-permission android:name="android.permission.MODIFY_AUDIO_SETTINGS" />
```

Android 12+ requires `BLUETOOTH_CONNECT` runtime permission:

```csharp
if (OperatingSystem.IsAndroidVersionAtLeast(31))
{
    var status = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
    if (status != PermissionStatus.Granted)
        status = await Permissions.RequestAsync<Permissions.Bluetooth>();
}
```

### Windows

No special permissions needed. BT audio devices appear as standard audio endpoints
once paired in Windows Bluetooth settings.

---

## Testing

### Unit Tests

BT hardware isn't available in CI. Test the provider logic with fakes:

```csharp
public class BluetoothAudioProviderTests
{
    [Fact]
    public async Task AutoSwitch_WhenSavedBtDeviceConnects()
    {
        var platform = new FakeAudioInputProvider("platform");
        var manager = CreateManager(platform);
        await manager.InitializeAsync();
        manager.Active!.ProviderId.Should().Be("platform");

        // Simulate BT device connecting
        var bt = new FakeAudioInputProvider("bt:AA:BB:CC:DD:EE:FF");
        manager.Settings.ActiveAudioInputProvider = "bt:AA:BB:CC:DD:EE:FF";
        manager.RegisterProvider(bt);

        // Should auto-switch
        await Task.Delay(50);
        manager.Active!.ProviderId.Should().Be("bt:AA:BB:CC:DD:EE:FF");
    }

    [Fact]
    public async Task Fallback_WhenBtDisconnects()
    {
        var platform = new FakeAudioInputProvider("platform");
        var bt = new FakeAudioInputProvider("bt:AA:BB:CC:DD:EE:FF");
        var manager = CreateManager(platform, bt);

        await manager.SetActiveAsync("bt:AA:BB:CC:DD:EE:FF");
        bt.SimulateDisconnect();

        await Task.Delay(50);
        manager.Active!.ProviderId.Should().Be("platform");
    }
}
```

### Integration Tests (Manual)

1. Pair BT headset in OS settings
2. Start BodyCam
3. Verify BT device appears in audio input picker
4. Select BT device → audio should route through BT mic
5. Turn off BT headset → should fallback to platform mic
6. Turn on BT headset → should auto-reconnect if it was the saved preference

### Resampling Tests

```csharp
public class AudioResamplerTests
{
    [Fact]
    public void Resample_8kTo24k_TriplesSampleCount()
    {
        // 100ms of 8kHz PCM16 = 800 samples = 1600 bytes
        var input = new byte[1600];
        var output = AudioResampler.Resample(input, 8000, 24000);

        // 100ms of 24kHz = 2400 samples = 4800 bytes
        output.Length.Should().Be(4800);
    }

    [Fact]
    public void Resample_SameRate_ReturnsInput()
    {
        var input = new byte[] { 1, 2, 3, 4 };
        var output = AudioResampler.Resample(input, 24000, 24000);
        output.Should().BeSameAs(input);
    }
}
```
