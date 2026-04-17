# M12 — Audio Input Abstraction Layer

## IAudioInputProvider

The central interface all audio input sources implement. Streaming PCM audio
via events, lifecycle management, and device metadata.

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// An audio input source that produces PCM16 audio chunks.
/// Only one provider is active at a time, managed by AudioInputManager.
/// </summary>
public interface IAudioInputProvider : IAsyncDisposable
{
    /// <summary>
    /// Human-readable name for the audio source (e.g. "Phone Microphone", "BT Glasses").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Unique identifier for this provider type (e.g. "platform", "bt", "usb", "wifi").
    /// For device-specific providers, includes device ID: "bt:AA:BB:CC:DD:EE:FF".
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Whether the audio source hardware is currently connected and ready to capture.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Whether audio is currently being captured.
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Initialize and start capturing audio. Chunks arrive via AudioChunkAvailable.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stop capturing audio. Idempotent.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Fires when a new PCM16 audio chunk is available.
    /// Format: 16-bit signed PCM, mono, sample rate from AppSettings.
    /// </summary>
    event EventHandler<byte[]>? AudioChunkAvailable;

    /// <summary>
    /// Raised when the audio source disconnects unexpectedly
    /// (e.g. BT glasses turned off, USB unplugged).
    /// </summary>
    event EventHandler? Disconnected;
}
```

### Design Decisions

**PCM16 bytes as the interchange format.** All providers emit `byte[]` containing
16-bit signed PCM mono audio. This is what the Realtime API expects (`pcm16` format)
and what the existing `VoiceInputAgent` already sends. No transcoding needed in the
pipeline.

**Event-based streaming (not pull-based).** Audio is inherently push-based — the
microphone produces chunks at a fixed rate. The `AudioChunkAvailable` event matches
the existing `IAudioInputService` pattern and avoids the complexity of
`IAsyncEnumerable` for real-time audio where backpressure isn't meaningful.

**Start/Stop lifecycle.** Matches `IAudioInputService` exactly. Providers manage
their own hardware lifecycle. `StartAsync` is idempotent so callers don't need
to track state.

**One active at a time.** `AudioInputManager` enforces this. When switching providers,
it stops the current one before starting the new one. This avoids hardware conflicts
(e.g. two processes competing for the same BT SCO channel).

**ProviderId format.** Simple string like `"platform"` for the default mic, or
`"bt:AA:BB:CC:DD:EE:FF"` for a specific BT device. This allows the settings to
persist a specific device selection across sessions.

---

## AudioInputManager

Manages available audio input providers, the currently active one, and provides
backward-compatible `IAudioInputService` implementation.

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// Manages available audio input providers and the currently active one.
/// Implements IAudioInputService for backward compatibility with VoiceInputAgent
/// and MicrophoneCoordinator.
/// </summary>
public class AudioInputManager : IAudioInputService, IAsyncDisposable
{
    private readonly List<IAudioInputProvider> _providers = new();
    private readonly ISettingsService _settings;
    private readonly ILogger<AudioInputManager> _logger;
    private IAudioInputProvider? _active;

    public event EventHandler<byte[]>? AudioChunkAvailable;

    public AudioInputManager(
        IEnumerable<IAudioInputProvider> providers,
        ISettingsService settings,
        ILogger<AudioInputManager> logger)
    {
        _providers.AddRange(providers);
        _settings = settings;
        _logger = logger;
    }

    /// <summary>All registered audio input providers.</summary>
    public IReadOnlyList<IAudioInputProvider> Providers => _providers;

    /// <summary>The currently active audio input provider.</summary>
    public IAudioInputProvider? Active => _active;

    /// <summary>Whether the active provider is currently capturing.</summary>
    public bool IsCapturing => _active?.IsCapturing ?? false;

    /// <summary>
    /// Select and activate an audio input provider by its ProviderId.
    /// Stops the previous provider if one was active.
    /// </summary>
    public async Task SetActiveAsync(string providerId, CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId)
            ?? throw new ArgumentException($"Unknown audio input provider: {providerId}");

        if (!provider.IsAvailable)
            throw new InvalidOperationException(
                $"Audio input provider '{provider.DisplayName}' is not available.");

        if (_active is not null && _active != provider)
        {
            _active.AudioChunkAvailable -= OnProviderChunk;
            _active.Disconnected -= OnProviderDisconnected;
            await _active.StopAsync();
        }

        _active = provider;
        _settings.ActiveAudioInputProvider = providerId;

        provider.AudioChunkAvailable += OnProviderChunk;
        provider.Disconnected += OnProviderDisconnected;

        _logger.LogInformation("Audio input switched to {Provider}", provider.DisplayName);
    }

    /// <summary>
    /// IAudioInputService.StartAsync — starts the active provider.
    /// If no provider is active, falls back to platform mic.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_active is null)
            await FallbackToPlatformAsync(ct);

        if (_active is not null)
            await _active.StartAsync(ct);
    }

    /// <summary>
    /// IAudioInputService.StopAsync — stops the active provider.
    /// </summary>
    public async Task StopAsync()
    {
        if (_active is not null)
            await _active.StopAsync();
    }

    /// <summary>
    /// Initialize: restore last-used provider from settings, or default to platform mic.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var savedId = _settings.ActiveAudioInputProvider ?? "platform";
        var provider = _providers.FirstOrDefault(p => p.ProviderId == savedId && p.IsAvailable)
            ?? _providers.FirstOrDefault(p => p.ProviderId == "platform");

        if (provider is not null)
            await SetActiveAsync(provider.ProviderId, ct);
    }

    /// <summary>
    /// Register a provider dynamically (e.g. when a BT device is discovered).
    /// </summary>
    public void RegisterProvider(IAudioInputProvider provider)
    {
        if (_providers.Any(p => p.ProviderId == provider.ProviderId))
            return;

        _providers.Add(provider);
        _logger.LogInformation("Registered audio input provider: {Provider}", provider.DisplayName);
    }

    /// <summary>
    /// Remove a provider (e.g. when a BT device is unpaired).
    /// If this was the active provider, falls back to platform mic.
    /// </summary>
    public async Task UnregisterProviderAsync(string providerId)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider is null) return;

        if (_active == provider)
        {
            _active.AudioChunkAvailable -= OnProviderChunk;
            _active.Disconnected -= OnProviderDisconnected;
            await _active.StopAsync();
            _active = null;
            await FallbackToPlatformAsync();
        }

        _providers.Remove(provider);
    }

    private void OnProviderChunk(object? sender, byte[] chunk)
    {
        AudioChunkAvailable?.Invoke(this, chunk);
    }

    private async void OnProviderDisconnected(object? sender, EventArgs e)
    {
        if (sender is IAudioInputProvider p)
        {
            _logger.LogWarning("Audio input '{Provider}' disconnected, falling back",
                p.DisplayName);
            p.AudioChunkAvailable -= OnProviderChunk;
            p.Disconnected -= OnProviderDisconnected;
        }

        _active = null;
        await FallbackToPlatformAsync();
    }

    private async Task FallbackToPlatformAsync(CancellationToken ct = default)
    {
        var platform = _providers.FirstOrDefault(
            p => p.ProviderId == "platform" && p.IsAvailable);

        if (platform is not null && platform != _active)
        {
            await SetActiveAsync("platform", ct);
            _logger.LogInformation("Fell back to platform microphone");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_active is not null)
        {
            _active.AudioChunkAvailable -= OnProviderChunk;
            _active.Disconnected -= OnProviderDisconnected;
            await _active.StopAsync();
        }

        foreach (var provider in _providers)
            await provider.DisposeAsync();
    }
}
```

---

## DI Registration

```csharp
// In MauiProgram.cs

// Audio input providers
builder.Services.AddSingleton<IAudioInputProvider, PlatformMicProvider>();
#if WINDOWS
builder.Services.AddSingleton<IAudioInputProvider, WindowsUsbAudioProvider>();
#endif
// BT provider registered dynamically by BluetoothAudioProvider when devices are discovered

// AudioInputManager replaces direct IAudioInputService registration
builder.Services.AddSingleton<AudioInputManager>();
builder.Services.AddSingleton<IAudioInputService>(sp => sp.GetRequiredService<AudioInputManager>());
```

### Key DI Detail

`AudioInputManager` is registered both as itself (for settings UI, device selection)
and as `IAudioInputService` (for backward compatibility with `VoiceInputAgent` and
`MicrophoneCoordinator`). Both resolve to the same singleton instance.

---

## Integration with Existing Code

### VoiceInputAgent — Zero Changes Required

`VoiceInputAgent` depends on `IAudioInputService`. Since `AudioInputManager` implements
`IAudioInputService`, the agent works without modification:

```csharp
// VoiceInputAgent constructor — unchanged
public VoiceInputAgent(IAudioInputService audioInput, IRealtimeClient realtime)
{
    _audioInput = audioInput;  // Resolves to AudioInputManager
    _realtime = realtime;
}
```

Audio flow:
1. `VoiceInputAgent.StartAsync()` calls `_audioInput.StartAsync()` → `AudioInputManager.StartAsync()`
2. `AudioInputManager` starts the active provider
3. Provider emits `AudioChunkAvailable` → `AudioInputManager` re-emits → `VoiceInputAgent.OnAudioChunk`
4. `VoiceInputAgent` sends chunk to `IRealtimeClient.SendAudioChunkAsync`

### MicrophoneCoordinator — Zero Changes Required

`MicrophoneCoordinator` coordinates mic ownership between wake word engine and Realtime
session. It doesn't directly use `IAudioInputService`, but the wake word engine
(`IWakeWordService`) does. The coordinator continues to work as-is because
`AudioInputManager` handles the underlying provider lifecycle transparently.

### SendVisionCommandAsync / AgentOrchestrator — No Changes

Audio input is independent of the orchestrator's frame capture. The orchestrator
doesn't touch audio — that's handled by `VoiceInputAgent`.

---

## Settings

New settings for audio input device selection:

```csharp
// ISettingsService additions
string? ActiveAudioInputProvider { get; set; }  // "platform", "bt:AA:BB:CC:DD:EE:FF", "usb:DeviceName", etc.
```

### AppSettings (existing, unchanged)

```csharp
// These continue to apply to all providers
public int SampleRate { get; set; } = 24000;
public int ChunkDurationMs { get; set; } = 100;
```

### Settings UI

Audio input device picker on the Settings page:

```xml
<!-- SettingsPage.xaml -->
<VerticalStackLayout>
    <Label Text="Audio Input" FontAttributes="Bold" />
    <Picker Title="Select Microphone"
            ItemsSource="{Binding AudioInputProviders}"
            ItemDisplayBinding="{Binding DisplayName}"
            SelectedItem="{Binding SelectedAudioInputProvider}" />
    <Label Text="{Binding SelectedAudioInputProvider.ProviderId, StringFormat='ID: {0}'}"
           FontSize="12" TextColor="Gray" />
</VerticalStackLayout>
```

### SettingsViewModel

```csharp
public partial class SettingsViewModel : ViewModelBase
{
    private readonly AudioInputManager _audioInputManager;

    public IReadOnlyList<IAudioInputProvider> AudioInputProviders
        => _audioInputManager.Providers;

    private IAudioInputProvider? _selectedAudioInputProvider;
    public IAudioInputProvider? SelectedAudioInputProvider
    {
        get => _selectedAudioInputProvider;
        set
        {
            if (SetProperty(ref _selectedAudioInputProvider, value) && value is not null)
                _ = _audioInputManager.SetActiveAsync(value.ProviderId);
        }
    }
}
```

---

## Audio Format Contract

All providers MUST emit audio in this format:

| Property | Value |
|----------|-------|
| Encoding | 16-bit signed PCM (little-endian) |
| Channels | 1 (mono) |
| Sample rate | From `AppSettings.SampleRate` (default 24000 Hz) |
| Chunk size | `SampleRate × 2 × ChunkDurationMs / 1000` bytes |

If a source produces audio in a different format (e.g. BT SCO at 8kHz), the
provider is responsible for resampling to the configured sample rate before
emitting chunks.

### Resampling Helper

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// Simple linear interpolation resampler for converting between sample rates.
/// Used by providers whose hardware operates at a different rate than AppSettings.SampleRate.
/// </summary>
internal static class AudioResampler
{
    /// <summary>
    /// Resample PCM16 mono audio from one sample rate to another.
    /// </summary>
    public static byte[] Resample(byte[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate) return input;

        int inputSamples = input.Length / 2;
        int outputSamples = (int)((long)inputSamples * toRate / fromRate);
        var output = new byte[outputSamples * 2];

        for (int i = 0; i < outputSamples; i++)
        {
            double srcIndex = (double)i * fromRate / toRate;
            int idx0 = (int)srcIndex;
            int idx1 = Math.Min(idx0 + 1, inputSamples - 1);
            double frac = srcIndex - idx0;

            short s0 = BitConverter.ToInt16(input, idx0 * 2);
            short s1 = BitConverter.ToInt16(input, idx1 * 2);
            short interpolated = (short)(s0 + (s1 - s0) * frac);

            BitConverter.TryWriteBytes(output.AsSpan(i * 2), interpolated);
        }

        return output;
    }
}
```

---

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Active provider disconnects | `Disconnected` event → `AudioInputManager` falls back to `"platform"` |
| Provider `StartAsync` fails | Exception propagates to caller; manager remains on previous provider |
| No providers available | `AudioInputManager.StartAsync` logs warning, `IsCapturing` stays false |
| Audio chunk delivery fails | Provider swallows, continues capturing (same as current behavior) |
| Permission denied (Android) | `PermissionException` thrown from `StartAsync` (existing behavior) |

---

## Testing Strategy

### Unit Tests

```csharp
public class AudioInputManagerTests
{
    [Fact]
    public async Task SetActiveAsync_StopsPreviousProvider()
    {
        var provider1 = new FakeAudioInputProvider("test1");
        var provider2 = new FakeAudioInputProvider("test2");
        var manager = CreateManager(provider1, provider2);

        await manager.SetActiveAsync("test1");
        await manager.SetActiveAsync("test2");

        provider1.StopCalled.Should().BeTrue();
        manager.Active.Should().Be(provider2);
    }

    [Fact]
    public async Task ChunksFromActiveProvider_AreForwarded()
    {
        var provider = new FakeAudioInputProvider("test");
        var manager = CreateManager(provider);
        await manager.SetActiveAsync("test");
        await manager.StartAsync();

        var received = new List<byte[]>();
        manager.AudioChunkAvailable += (_, chunk) => received.Add(chunk);

        provider.EmitChunk(new byte[] { 1, 2, 3, 4 });

        received.Should().HaveCount(1);
        received[0].Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public async Task Disconnect_FallsBackToPlatform()
    {
        var bt = new FakeAudioInputProvider("bt:device");
        var platform = new FakeAudioInputProvider("platform");
        var manager = CreateManager(bt, platform);

        await manager.SetActiveAsync("bt:device");
        bt.SimulateDisconnect();

        // Allow async fallback
        await Task.Delay(50);

        manager.Active?.ProviderId.Should().Be("platform");
    }
}
```

### FakeAudioInputProvider (Test Double)

```csharp
internal class FakeAudioInputProvider : IAudioInputProvider
{
    public string DisplayName { get; }
    public string ProviderId { get; }
    public bool IsAvailable { get; set; } = true;
    public bool IsCapturing { get; private set; }
    public bool StopCalled { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public FakeAudioInputProvider(string id, string? name = null)
    {
        ProviderId = id;
        DisplayName = name ?? id;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsCapturing = false;
        StopCalled = true;
        return Task.CompletedTask;
    }

    public void EmitChunk(byte[] chunk)
        => AudioChunkAvailable?.Invoke(this, chunk);

    public void SimulateDisconnect()
        => Disconnected?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

---

## Migration Checklist

### Phase 1 Steps

1. Create `Services/Audio/` directory
2. Add `IAudioInputProvider.cs`
3. Add `AudioInputManager.cs`
4. Add `PlatformMicProvider.cs` (wraps existing implementations — see [platform-providers.md](platform-providers.md))
5. Add `AudioResampler.cs`
6. Update DI registration in `MauiProgram.cs`:
   - Register `PlatformMicProvider` as `IAudioInputProvider`
   - Register `AudioInputManager` as both itself and `IAudioInputService`
   - Remove direct `IAudioInputService` registrations of platform implementations
7. Add `ActiveAudioInputProvider` to `ISettingsService`
8. Add audio input picker to Settings page
9. Add unit tests for `AudioInputManager`
10. Verify `VoiceInputAgent` and `MicrophoneCoordinator` still work (no code changes needed)
