# M17 Phase 1 — Hardware Investigation & BT Audio

Receive TKYUAN glasses, investigate all protocols, implement Bluetooth audio
providers that plug into the M12/M13 abstractions.

**Depends on:** M12 Phase 1 (audio input abstraction), M13 Phase 1 (audio output
abstraction). Both are implemented.

---

## Wave 1: Hardware Investigation

When glasses arrive, execute this checklist systematically and document findings.

### BT Service Discovery
```powershell
# Windows: use BT Explorer or nRF Connect (Android)
# List all GATT services and characteristics
```

| Check | Tool | What to Look For |
|-------|------|------------------|
| GATT services | nRF Connect | Service UUIDs — especially 0x180F (battery), 0x1812 (HID), custom services |
| Audio profiles | BT logs / Windows BT settings | A2DP, HFP, HSP — which are supported |
| Audio codec | BT snoop log | SBC, AAC, aptX, LDAC negotiation |
| WiFi-Direct | Android WiFi settings | Does glasses broadcast a hotspot/WiFi-Direct group? |
| Camera stream | Browser / VLC | Try http://<glasses-ip>/stream, rtsp://<glasses-ip>:554 |
| Companion app | APK decompile / Wireshark | What protocol does the manufacturer app use? |
| Button events | nRF Connect GATT notifications | Which characteristic fires on button press? |
| HID reports | Windows Device Manager | Does glasses appear as HID device? |

### Write Investigation Report

Create `m17-glasses/investigation-report.md` with findings for each item.
This determines the implementation path for Phase 2 (camera, buttons).

### Verify
- [ ] All GATT services documented
- [ ] Audio profiles identified (A2DP / HFP / HSP)
- [ ] Camera protocol determined (or confirmed unavailable)
- [ ] Button event mechanism identified
- [ ] Battery service availability confirmed
- [ ] Investigation report written

---

## Wave 2: BluetoothAudioInputProvider (Windows)

Implement a BT microphone provider that plugs into `AudioInputManager`.

### Implementation

```csharp
// Platforms/Windows/BluetoothAudioInputProvider.cs
public class BluetoothAudioInputProvider : IAudioInputProvider
{
    public string ProviderId => "bluetooth";
    public string DisplayName => "Bluetooth Microphone";
    public bool IsAvailable => _btDevice != null && _btDevice.IsConnected;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    private WasapiCapture? _capture;
    private MMDevice? _btDevice;

    public BluetoothAudioInputProvider()
    {
        // Find BT audio input device by name or device type
        // Using NAudio's MMDeviceEnumerator
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_btDevice == null)
            throw new InvalidOperationException("No BT microphone connected");

        _capture = new WasapiCapture(_btDevice)
        {
            WaveFormat = new WaveFormat(24000, 16, 1) // PCM16 24kHz mono
        };

        _capture.DataAvailable += (_, e) =>
        {
            var chunk = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
            AudioChunkAvailable?.Invoke(this, chunk);
        };

        _capture.StartRecording();
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
        IsCapturing = false;
        return Task.CompletedTask;
    }
}
```

### Device Enumeration

```csharp
// Find BT devices using NAudio's MMDeviceEnumerator
private static MMDevice? FindBluetoothMic()
{
    using var enumerator = new MMDeviceEnumerator();
    var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

    // BT devices typically have "Bluetooth" in the name or use BT transport
    return devices.FirstOrDefault(d =>
        d.FriendlyName.Contains("TKYUAN", StringComparison.OrdinalIgnoreCase) ||
        d.DeviceTopology?.GetConnector(0)?.GetConnectorIdConnectedTo()
            ?.Contains("BTHENUM") == true);
}
```

### DI Registration

```csharp
// MauiProgram.cs — register alongside existing platform provider
#if WINDOWS
builder.Services.AddSingleton<IAudioInputProvider, PlatformMicProvider>();
builder.Services.AddSingleton<IAudioInputProvider, BluetoothAudioInputProvider>();
#endif
```

`AudioInputManager` already supports multiple providers and picks the active one.

### Tests
```csharp
public class BluetoothAudioInputProviderTests
{
    [Fact]
    public void ProviderId_IsBluetooth()
    {
        var provider = new BluetoothAudioInputProvider();
        provider.ProviderId.Should().Be("bluetooth");
    }

    [Fact]
    public void IsAvailable_WhenNoDevice_IsFalse()
    {
        var provider = new BluetoothAudioInputProvider();
        // No BT device paired in test environment
        provider.IsAvailable.Should().BeFalse();
    }
}
```

### Verify
- [ ] Provider compiles
- [ ] Device enumeration finds BT mic (when paired)
- [ ] Audio capture produces PCM16 24kHz mono chunks
- [ ] `AudioChunkAvailable` fires with correct data
- [ ] `Disconnected` fires when BT drops
- [ ] Tests pass

---

## Wave 3: BluetoothAudioOutputProvider (Windows)

```csharp
// Platforms/Windows/BluetoothAudioOutputProvider.cs
public class BluetoothAudioOutputProvider : IAudioOutputProvider
{
    public string ProviderId => "bluetooth";
    public string DisplayName => "Bluetooth Speaker";
    public bool IsAvailable => _btDevice != null && _btDevice.IsConnected;
    public bool IsPlaying { get; private set; }

    private WasapiOut? _player;
    private BufferedWaveProvider? _buffer;
    private MMDevice? _btDevice;

    public Task StartAsync(CancellationToken ct = default)
    {
        _buffer = new BufferedWaveProvider(new WaveFormat(24000, 16, 1))
        {
            BufferDuration = TimeSpan.FromSeconds(5),
            DiscardOnBufferOverflow = true
        };

        _player = new WasapiOut(_btDevice, AudioClientShareMode.Shared, true, 50);
        _player.Init(_buffer);
        _player.Play();
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task PlayChunkAsync(byte[] pcm16Data, CancellationToken ct = default)
    {
        _buffer?.AddSamples(pcm16Data, 0, pcm16Data.Length);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        IsPlaying = false;
        return Task.CompletedTask;
    }
}
```

### Verify
- [ ] Provider compiles
- [ ] Audio plays through BT speaker
- [ ] Low latency (< 100ms buffer)
- [ ] `Disconnected` fires when BT drops
- [ ] Tests pass

---

## Wave 4: Basic GlassesDeviceManager

Minimal manager that coordinates BT providers and monitors connection state.

```csharp
// Services/Glasses/GlassesDeviceManager.cs
public class GlassesDeviceManager
{
    private readonly IEnumerable<IAudioInputProvider> _audioInputProviders;
    private readonly IEnumerable<IAudioOutputProvider> _audioOutputProviders;

    public GlassesConnectionState State { get; private set; }
    public GlassesBatteryInfo? Battery { get; private set; }

    public event EventHandler<GlassesConnectionState>? StateChanged;

    public async Task<IReadOnlyList<GlassesDeviceInfo>> ScanAsync(CancellationToken ct)
    {
        // Use Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState
        // or classic BT enumeration
        // Return discovered glasses-like devices
    }

    public async Task ConnectAsync(GlassesDeviceInfo device, CancellationToken ct)
    {
        State = GlassesConnectionState.Connecting;
        StateChanged?.Invoke(this, State);

        // Activate BT audio providers
        var btAudioIn = _audioInputProviders.FirstOrDefault(p => p.ProviderId == "bluetooth");
        var btAudioOut = _audioOutputProviders.FirstOrDefault(p => p.ProviderId == "bluetooth");

        // Providers become available when BT device connects
        // AudioInputManager/AudioOutputManager auto-switch to BT

        State = GlassesConnectionState.Connected;
        StateChanged?.Invoke(this, State);

        // Start battery monitoring (GATT 0x180F)
        _ = MonitorBatteryAsync(device, ct);
    }

    private async Task MonitorBatteryAsync(GlassesDeviceInfo device, CancellationToken ct)
    {
        // Read battery level from GATT Battery Service (UUID 0x180F)
        // Update Battery property periodically
    }
}
```

### Verify
- [ ] Scan discovers paired BT devices
- [ ] Connect activates BT providers
- [ ] State transitions fire events
- [ ] Battery monitoring works (if GATT 0x180F available)
- [ ] Tests pass

---

## Wave 5: Android BT Audio Providers

```csharp
// Platforms/Android/BluetoothAudioInputProvider.cs
// Uses Android AudioRecord with AudioSource.VoiceCommunication
// Routes via BluetoothHeadset profile (SCO)

public class BluetoothAudioInputProvider : IAudioInputProvider
{
    public string ProviderId => "bluetooth";

    public async Task StartAsync(CancellationToken ct = default)
    {
        var audioManager = (AudioManager)Platform.CurrentActivity!
            .GetSystemService(Context.AudioService)!;

        // Start BT SCO connection for mic access
        audioManager.StartBluetoothSco();
        audioManager.BluetoothScoOn = true;

        // Create AudioRecord with BT SCO source
        _recorder = new AudioRecord(
            AudioSource.VoiceCommunication,
            24000, // sample rate
            ChannelIn.Mono,
            Encoding.Pcm16bit,
            _bufferSize);

        _recorder.StartRecording();
        _ = CaptureLoopAsync(ct);
    }
}
```

### Verify
- [ ] Android BT audio input works
- [ ] Android BT audio output works
- [ ] SCO connection established
- [ ] Audio quality acceptable
- [ ] Build succeeds on both platforms
- [ ] All tests pass
