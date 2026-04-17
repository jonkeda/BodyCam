# M13 — Audio Output Abstraction Layer

## IAudioOutputProvider

The central interface all audio output destinations implement. Mirrors the structure
of `ICameraProvider` from M11 — lifecycle management, a unique provider ID, and
availability/disconnection handling.

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// An audio output destination that can play PCM audio chunks.
/// Only one provider is active at a time, managed by AudioOutputManager.
/// </summary>
public interface IAudioOutputProvider : IAsyncDisposable
{
    /// <summary>
    /// Human-readable name for the audio output (e.g. "Phone Speaker", "BT Glasses").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Unique identifier for this provider type (e.g. "phone-speaker", "bt-audio", "usb-audio").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Optional device identifier within this provider type.
    /// For BT: the MAC address. For USB: the device path. Null for single-device providers.
    /// </summary>
    string? DeviceId { get; }

    /// <summary>
    /// Whether the audio output hardware is connected and ready to play.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Whether audio is currently playing through this provider.
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Initialize the audio output hardware. Call before PlayChunkAsync.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    /// <param name="sampleRate">PCM sample rate (e.g. 24000).</param>
    /// <param name="ct">Cancellation token.</param>
    Task StartAsync(int sampleRate, CancellationToken ct = default);

    /// <summary>
    /// Release the audio output hardware. Idempotent.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Play a chunk of 16-bit PCM mono audio.
    /// May apply back-pressure if the buffer is full.
    /// </summary>
    Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default);

    /// <summary>
    /// Clear the playback buffer. Used for interruption handling —
    /// when the user speaks, stop playing immediately.
    /// </summary>
    void ClearBuffer();

    /// <summary>
    /// Raised when the audio device disconnects unexpectedly
    /// (e.g. BT glasses turned off, USB unplugged).
    /// </summary>
    event EventHandler? Disconnected;
}
```

### Design Decisions

**Sample rate passed to StartAsync.** Unlike `IAudioOutputService` which reads from
`AppSettings`, the provider receives the sample rate explicitly. This allows the
`AudioOutputManager` to set the correct rate when switching providers, and makes
providers testable without `AppSettings`.

**16-bit PCM mono as the interchange format.** All providers receive `byte[]` containing
16-bit PCM mono data. This is the format the Realtime API produces. Converting to
device-native formats (if needed) happens inside each provider.

**One active at a time.** The `AudioOutputManager` enforces this. When switching
providers, it stops the current one before starting the new one. This avoids
conflicting audio focus (especially on Android).

**DeviceId for multi-device providers.** BT and USB providers may enumerate multiple
devices. The `DeviceId` distinguishes instances. For single-device providers like
phone speaker, `DeviceId` is null.

---

## AudioOutputManager

Manages the active audio output provider and provides the playback interface to
`VoiceOutputAgent`.

```csharp
namespace BodyCam.Services.Audio;

/// <summary>
/// Manages available audio output providers and the currently active one.
/// Implements IAudioOutputService for backward compatibility during migration.
/// </summary>
public class AudioOutputManager : IAudioOutputService
{
    private readonly IReadOnlyList<IAudioOutputProvider> _providers;
    private readonly ISettingsService _settings;
    private readonly ILogger<AudioOutputManager> _logger;
    private IAudioOutputProvider? _active;
    private int _sampleRate = 24000;

    public AudioOutputManager(
        IEnumerable<IAudioOutputProvider> providers,
        ISettingsService settings,
        ILogger<AudioOutputManager> logger)
    {
        _providers = providers.ToList();
        _settings = settings;
        _logger = logger;
    }

    /// <summary>All registered audio output providers.</summary>
    public IReadOnlyList<IAudioOutputProvider> Providers => _providers;

    /// <summary>The currently active audio output provider.</summary>
    public IAudioOutputProvider? Active => _active;

    /// <summary>Whether the active provider is currently playing.</summary>
    public bool IsPlaying => _active?.IsPlaying ?? false;

    /// <summary>
    /// Select and activate an audio output provider by its ProviderId.
    /// Stops the previous provider if one was active.
    /// </summary>
    public async Task SetActiveAsync(string providerId, string? deviceId = null, CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p =>
            p.ProviderId == providerId &&
            (deviceId is null || p.DeviceId == deviceId))
            ?? throw new ArgumentException($"Unknown audio output provider: {providerId}/{deviceId}");

        if (_active is not null && _active != provider)
        {
            _active.Disconnected -= OnProviderDisconnected;
            await _active.StopAsync();
        }

        _active = provider;
        _settings.ActiveAudioOutputProvider = providerId;
        _settings.ActiveAudioOutputDeviceId = deviceId;

        provider.Disconnected += OnProviderDisconnected;

        _logger.LogInformation("Audio output switched to {Provider} ({DeviceId})",
            provider.DisplayName, deviceId ?? "default");
    }

    /// <summary>
    /// Start playback on the active provider.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_active is null)
            await FallbackToDefaultAsync(ct);

        if (_active is not null)
            await _active.StartAsync(_sampleRate, ct);
    }

    /// <summary>
    /// Stop playback on the active provider.
    /// </summary>
    public async Task StopAsync()
    {
        if (_active is not null)
            await _active.StopAsync();
    }

    /// <summary>
    /// Play a PCM chunk through the active provider.
    /// </summary>
    public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (_active is null)
            await FallbackToDefaultAsync(ct);

        if (_active is not null)
            await _active.PlayChunkAsync(pcmData, ct);
    }

    /// <summary>
    /// Clear the playback buffer on the active provider.
    /// Used for interruption handling.
    /// </summary>
    public void ClearBuffer()
    {
        _active?.ClearBuffer();
    }

    /// <summary>
    /// Set the sample rate for audio playback.
    /// Must be called before StartAsync.
    /// </summary>
    public void SetSampleRate(int sampleRate)
    {
        _sampleRate = sampleRate;
    }

    /// <summary>
    /// Initialize: restore last-used provider from settings, or default to phone/laptop speaker.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var savedProviderId = _settings.ActiveAudioOutputProvider;
        var savedDeviceId = _settings.ActiveAudioOutputDeviceId;

        var provider = savedProviderId is not null
            ? _providers.FirstOrDefault(p =>
                p.ProviderId == savedProviderId &&
                (savedDeviceId is null || p.DeviceId == savedDeviceId) &&
                p.IsAvailable)
            : null;

        // Fall back to platform default
        provider ??= _providers.FirstOrDefault(p =>
            p.ProviderId is "phone-speaker" or "windows-speaker" &&
            p.IsAvailable);

        if (provider is not null)
            await SetActiveAsync(provider.ProviderId, provider.DeviceId, ct);
    }

    private async void OnProviderDisconnected(object? sender, EventArgs e)
    {
        if (sender is IAudioOutputProvider p)
        {
            p.Disconnected -= OnProviderDisconnected;
            _logger.LogWarning("Audio output {Provider} disconnected, falling back",
                p.DisplayName);
        }

        await FallbackToDefaultAsync();
    }

    private async Task FallbackToDefaultAsync(CancellationToken ct = default)
    {
        // Try phone speaker first, then Windows speaker
        var fallback = _providers.FirstOrDefault(p =>
            p.ProviderId == "phone-speaker" && p.IsAvailable)
            ?? _providers.FirstOrDefault(p =>
                p.ProviderId == "windows-speaker" && p.IsAvailable);

        if (fallback is not null && fallback != _active)
        {
            _logger.LogInformation("Falling back to {Provider}", fallback.DisplayName);
            await SetActiveAsync(fallback.ProviderId, fallback.DeviceId, ct);
        }
    }
}
```

### IAudioOutputService Compatibility

`AudioOutputManager` implements `IAudioOutputService` to enable incremental migration.
During Phase 1, all existing code that depends on `IAudioOutputService` continues
to work — the manager routes through the active provider.

```csharp
// AudioOutputManager already implements all IAudioOutputService members:
// - StartAsync(ct) → calls _active.StartAsync(_sampleRate, ct)
// - StopAsync()    → calls _active.StopAsync()
// - PlayChunkAsync(pcmData, ct) → calls _active.PlayChunkAsync(pcmData, ct)
// - ClearBuffer()  → calls _active.ClearBuffer()
// - IsPlaying      → returns _active?.IsPlaying ?? false
```

Once migration is complete, `VoiceOutputAgent` can depend on `AudioOutputManager`
directly (for device selection, provider enumeration, etc.) and `IAudioOutputService`
can be removed.

---

## DI Registration

```csharp
// In MauiProgram.cs

// Audio output providers (registered as individual singletons)
#if WINDOWS
builder.Services.AddSingleton<IAudioOutputProvider, WindowsSpeakerProvider>();
#elif ANDROID
builder.Services.AddSingleton<IAudioOutputProvider, PhoneSpeakerProvider>();
#else
builder.Services.AddSingleton<IAudioOutputProvider, StubAudioOutputProvider>();
#endif

// BT audio output provider (all platforms with BT)
builder.Services.AddSingleton<IAudioOutputProvider, BluetoothAudioOutputProvider>();

// Audio output manager (replaces direct IAudioOutputService registration)
builder.Services.AddSingleton<AudioOutputManager>();
builder.Services.AddSingleton<IAudioOutputService>(sp => sp.GetRequiredService<AudioOutputManager>());
```

The key insight: `AudioOutputManager` is registered both as itself (for direct access
to provider enumeration and device selection) and as `IAudioOutputService` (for backward
compatibility with `VoiceOutputAgent` and any other consumer).

---

## Integration with VoiceOutputAgent

### Phase 1: No changes to VoiceOutputAgent

`VoiceOutputAgent` already depends on `IAudioOutputService`. Since `AudioOutputManager`
implements that interface, `VoiceOutputAgent` works without changes:

```csharp
// VoiceOutputAgent constructor — no change needed
public VoiceOutputAgent(IAudioOutputService audioOutput)
{
    _audioOutput = audioOutput;
    // audioOutput is actually AudioOutputManager at runtime
}
```

### Phase 2: Enhanced VoiceOutputAgent (optional)

Once `AudioOutputManager` is proven stable, `VoiceOutputAgent` can take a direct
dependency for richer functionality:

```csharp
public class VoiceOutputAgent
{
    private readonly AudioOutputManager _outputManager;
    private readonly AudioPlaybackTracker _tracker = new();

    public AudioPlaybackTracker Tracker => _tracker;

    public VoiceOutputAgent(AudioOutputManager outputManager)
    {
        _outputManager = outputManager;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _outputManager.StartAsync(ct);
    }

    public async Task StopAsync()
    {
        _tracker.Reset();
        await _outputManager.StopAsync();
    }

    public async Task PlayAudioDeltaAsync(byte[] pcmData, CancellationToken ct = default)
    {
        await _outputManager.PlayChunkAsync(pcmData, ct);
        _tracker.BytesPlayed += pcmData.Length;
    }

    public void HandleInterruption()
    {
        _outputManager.ClearBuffer();
    }

    /// <summary>
    /// Get the active audio output device name for display.
    /// </summary>
    public string ActiveDeviceName =>
        _outputManager.Active?.DisplayName ?? "None";
}
```

---

## Integration with AgentOrchestrator

The orchestrator's audio pipeline does not change. It continues to subscribe to
`_realtime.AudioDelta` and call `_voiceOut.PlayAudioDeltaAsync(pcmData)`. The
audio routing is transparent — the VoiceOutputAgent doesn't know or care which
physical device the audio goes to.

```csharp
// AgentOrchestrator — no changes needed
private async void OnAudioDelta(object? sender, byte[] pcmData)
{
    try { await _voiceOut.PlayAudioDeltaAsync(pcmData); }
    catch (Exception ex) { _logger.LogError(ex, "Audio playback failed"); }
}
```

The interruption flow also stays the same:

```csharp
private void OnInputSpeechStarted(object? sender, EventArgs e)
{
    if (_voiceOut.Tracker.CurrentItemId is not null)
    {
        _voiceOut.HandleInterruption();
        // ... truncation logic ...
    }
}
```

---

## Settings

New settings for audio output device selection:

```csharp
// In ISettingsService / AppSettings
string? ActiveAudioOutputProvider { get; set; }   // e.g. "phone-speaker", "bt-audio"
string? ActiveAudioOutputDeviceId { get; set; }    // e.g. BT MAC address, null for defaults
```

### Settings UI

The settings page gets an audio output section similar to the camera source picker:

```xml
<!-- In SettingsPage.xaml -->
<VerticalStackLayout>
    <Label Text="Audio Output" Style="{StaticResource SectionHeader}" />
    <Picker Title="Output Device"
            ItemsSource="{Binding AvailableAudioOutputs}"
            SelectedItem="{Binding SelectedAudioOutput}"
            ItemDisplayBinding="{Binding DisplayName}" />
    <Label Text="{Binding AudioOutputStatus}"
           Style="{StaticResource StatusLabel}" />
</VerticalStackLayout>
```

```csharp
// In SettingsViewModel
public ObservableCollection<IAudioOutputProvider> AvailableAudioOutputs { get; } = new();

private IAudioOutputProvider? _selectedAudioOutput;
public IAudioOutputProvider? SelectedAudioOutput
{
    get => _selectedAudioOutput;
    set
    {
        if (SetProperty(ref _selectedAudioOutput, value) && value is not null)
            _ = _outputManager.SetActiveAsync(value.ProviderId, value.DeviceId);
    }
}

public string AudioOutputStatus =>
    _outputManager.Active is { } a
        ? $"{a.DisplayName} — {(a.IsPlaying ? "Playing" : "Ready")}"
        : "No audio output";
```

---

## Migration Plan

### Step 1: Create interfaces and manager (no behavior change)
1. Define `IAudioOutputProvider` in `Services/Audio/`
2. Create `AudioOutputManager` implementing `IAudioOutputService`
3. Create `WindowsSpeakerProvider` wrapping `WindowsAudioOutputService`
4. Create `PhoneSpeakerProvider` wrapping `AndroidAudioOutputService`
5. Create `StubAudioOutputProvider` wrapping existing `AudioOutputService`
6. Update DI registration in `MauiProgram.cs`
7. Verify: all existing tests pass, audio plays as before

### Step 2: Add settings and device selection
1. Add `ActiveAudioOutputProvider` / `ActiveAudioOutputDeviceId` to settings
2. Add audio output picker to settings page
3. Call `AudioOutputManager.InitializeAsync()` on app startup

### Step 3: Add BT audio provider (Phase 2)
1. Implement `BluetoothAudioOutputProvider`
2. Register in DI
3. BT devices appear in settings picker

### Step 4: Remove IAudioOutputService (after stabilization)
1. Update `VoiceOutputAgent` to depend on `AudioOutputManager` directly
2. Remove `IAudioOutputService` interface
3. Remove old platform service implementations
