# Adding Audio & Camera Test Support to Brinell / BodyCam

This document explains how to create test-injectable audio, camera, and button
providers so Brinell-based UI tests can exercise BodyCam's full AI pipeline
without real hardware or API calls.

---

## 1. BodyCam's Provider Abstractions (What We're Mocking)

BodyCam uses a provider-manager pattern for all hardware I/O. Each service type has:
- **Interface** (`IXxxProvider`) — contract for one source/destination
- **Manager** (`XxxManager`) — coordinates providers, selects active one
- **DI registration** — `AddSingleton<IXxxProvider, ConcreteProvider>()`

### Audio Input — `IAudioInputProvider`

```csharp
// src/BodyCam/Services/Audio/IAudioInputProvider.cs
public interface IAudioInputProvider : IDisposable
{
    string DisplayName { get; }
    string ProviderId { get; }
    bool IsAvailable { get; }
    bool IsCapturing { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    event EventHandler<byte[]>? AudioChunkAvailable;
    event EventHandler? Disconnected;
}
```

**What it does:** Emits PCM audio chunks from a microphone source. The
`AudioInputManager` picks one active provider, subscribes to `AudioChunkAvailable`,
and forwards chunks to the Realtime API or wake word engine.

### Audio Output — `IAudioOutputProvider`

```csharp
// src/BodyCam/Services/Audio/IAudioOutputProvider.cs
public interface IAudioOutputProvider : IDisposable
{
    string DisplayName { get; }
    string ProviderId { get; }
    bool IsAvailable { get; }
    bool IsPlaying { get; }
    Task StartAsync(int sampleRate, CancellationToken ct = default);
    Task StopAsync();
    Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default);
    void ClearBuffer();
    event EventHandler? Disconnected;
}
```

**What it does:** Receives PCM audio chunks from the AI response and plays them
through a speaker. `AudioOutputManager` picks one active provider.

### Camera — `ICameraProvider`

```csharp
// src/BodyCam/Services/Camera/ICameraProvider.cs
public interface ICameraProvider : IDisposable
{
    string DisplayName { get; }
    string ProviderId { get; }
    bool IsAvailable { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default);
    IAsyncEnumerable<byte[]> StreamFramesAsync(CancellationToken ct = default);
    event EventHandler? Disconnected;
}
```

**What it does:** Captures JPEG frames from a camera source. `CameraManager`
picks one active provider and feeds frames to the vision pipeline via
`FrameCaptureFunc`.

### Button Input — `IButtonInputProvider`

```csharp
// src/BodyCam/Services/Input/IButtonInputProvider.cs
public interface IButtonInputProvider : IDisposable
{
    string DisplayName { get; }
    string ProviderId { get; }
    bool IsAvailable { get; }
    bool IsActive { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    event EventHandler<RawButtonEvent>? RawButtonEvent;
    event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;
    event EventHandler? Disconnected;
}
```

**What it does:** Emits raw button events (down/up/click) and optionally
pre-recognized gestures. `ButtonInputManager` aggregates all providers,
runs gesture recognition, maps to `ButtonAction`, dispatches to
`MainViewModel.DispatchActionAsync()`.

---

## 2. Test Provider Implementations

These classes go in the test project (`BodyCam.UITests` or a shared
`BodyCam.TestInfrastructure` project). They implement the same interfaces
as production providers.

### TestMicProvider — Inject Pre-Recorded Audio

```csharp
using BodyCam.Services.Audio;

namespace BodyCam.TestInfrastructure;

/// <summary>
/// Feeds pre-recorded PCM audio chunks into the pipeline.
/// Usage: Load a .pcm file, start, and it emits chunks on a timer.
/// </summary>
public class TestMicProvider : IAudioInputProvider
{
    private readonly byte[] _pcmData;
    private readonly int _chunkSize;
    private readonly int _chunkIntervalMs;
    private CancellationTokenSource? _cts;

    public string DisplayName => "Test Microphone";
    public string ProviderId => "test-mic";
    public bool IsAvailable => true;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    /// <param name="pcmFilePath">Path to raw PCM file (16-bit, 16kHz, mono).</param>
    /// <param name="chunkSize">Bytes per chunk (default 3200 = 100ms at 16kHz).</param>
    /// <param name="chunkIntervalMs">Interval between chunks in ms.</param>
    public TestMicProvider(string pcmFilePath, int chunkSize = 3200, int chunkIntervalMs = 100)
    {
        _pcmData = File.ReadAllBytes(pcmFilePath);
        _chunkSize = chunkSize;
        _chunkIntervalMs = chunkIntervalMs;
    }

    /// <summary>Create with raw byte array instead of file.</summary>
    public TestMicProvider(byte[] pcmData, int chunkSize = 3200, int chunkIntervalMs = 100)
    {
        _pcmData = pcmData;
        _chunkSize = chunkSize;
        _chunkIntervalMs = chunkIntervalMs;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsCapturing = true;
        _ = EmitChunksAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    private async Task EmitChunksAsync(CancellationToken ct)
    {
        var offset = 0;
        while (!ct.IsCancellationRequested && offset < _pcmData.Length)
        {
            var remaining = _pcmData.Length - offset;
            var size = Math.Min(_chunkSize, remaining);
            var chunk = new byte[size];
            Array.Copy(_pcmData, offset, chunk, 0, size);

            AudioChunkAvailable?.Invoke(this, chunk);

            offset += size;
            await Task.Delay(_chunkIntervalMs, ct).ConfigureAwait(false);
        }

        IsCapturing = false;
    }

    public void Dispose() => _cts?.Cancel();
}
```

### TestSpeakerProvider — Capture Audio Output for Assertions

```csharp
using BodyCam.Services.Audio;

namespace BodyCam.TestInfrastructure;

/// <summary>
/// Captures all PCM chunks the app tried to play.
/// After the test, inspect CapturedChunks or CapturedBytes for assertions.
/// </summary>
public class TestSpeakerProvider : IAudioOutputProvider
{
    private readonly List<byte[]> _chunks = new();

    public string DisplayName => "Test Speaker";
    public string ProviderId => "test-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    /// <summary>All PCM chunks that were "played".</summary>
    public IReadOnlyList<byte[]> CapturedChunks => _chunks;

    /// <summary>Total bytes played.</summary>
    public int TotalBytesPlayed => _chunks.Sum(c => c.Length);

    /// <summary>Concatenated audio data for analysis.</summary>
    public byte[] CapturedBytes => _chunks.SelectMany(c => c).ToArray();

    /// <summary>Was any audio played at all?</summary>
    public bool WasAudioPlayed => _chunks.Count > 0;

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
    {
        _chunks.Add(pcmData.ToArray()); // defensive copy
        return Task.CompletedTask;
    }

    public void ClearBuffer() => _chunks.Clear();

    public void Reset()
    {
        _chunks.Clear();
        IsPlaying = false;
    }

    public void Dispose() { }
}
```

### TestCameraProvider — Supply Test Frames

```csharp
using BodyCam.Services.Camera;
using System.Runtime.CompilerServices;

namespace BodyCam.TestInfrastructure;

/// <summary>
/// Returns JPEG frames from a directory of test images.
/// Cycles through images in alphabetical order.
/// </summary>
public class TestCameraProvider : ICameraProvider
{
    private readonly byte[][] _frames;
    private int _frameIndex;

    public string DisplayName => "Test Camera";
    public string ProviderId => "test-camera";
    public bool IsAvailable => true;

    public event EventHandler? Disconnected;

    /// <param name="framesDirectory">Directory containing .jpg files.</param>
    public TestCameraProvider(string framesDirectory)
    {
        var files = Directory.GetFiles(framesDirectory, "*.jpg")
            .OrderBy(f => f)
            .ToArray();
        _frames = files.Select(File.ReadAllBytes).ToArray();
    }

    /// <summary>Create with a single test frame.</summary>
    public TestCameraProvider(byte[] singleFrame)
    {
        _frames = new[] { singleFrame };
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (_frames.Length == 0) return Task.FromResult<byte[]?>(null);
        var frame = _frames[_frameIndex % _frames.Length];
        _frameIndex++;
        return Task.FromResult<byte[]?>(frame);
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = _frames[_frameIndex % _frames.Length];
            _frameIndex++;
            yield return frame;
            await Task.Delay(100, ct).ConfigureAwait(false); // 10 FPS
        }
    }

    public void Dispose() { }
}
```

### TestButtonProvider — Programmatic Button Presses

```csharp
using BodyCam.Services.Input;

namespace BodyCam.TestInfrastructure;

/// <summary>
/// Fires button events programmatically from test code.
/// Call SimulateTap/SimulateDoubleTap/SimulateLongPress in your test.
/// </summary>
public class TestButtonProvider : IButtonInputProvider
{
    public string DisplayName => "Test Buttons";
    public string ProviderId => "test-buttons";
    public bool IsAvailable => true;
    public bool IsActive { get; private set; }

    public event EventHandler<RawButtonEvent>? RawButtonEvent;
    public event EventHandler<ButtonGestureEvent>? PreRecognizedGesture;
    public event EventHandler? Disconnected;

    public Task StartAsync(CancellationToken ct = default)
    {
        IsActive = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    /// <summary>Simulate a button click (fires ButtonDown then ButtonUp).</summary>
    public void SimulateClick(string buttonId = "main")
    {
        var ts = Environment.TickCount64;
        RawButtonEvent?.Invoke(this, new RawButtonEvent
        {
            ProviderId = ProviderId,
            ButtonId = buttonId,
            EventType = RawButtonEventType.ButtonDown,
            TimestampMs = ts,
        });
        RawButtonEvent?.Invoke(this, new RawButtonEvent
        {
            ProviderId = ProviderId,
            ButtonId = buttonId,
            EventType = RawButtonEventType.ButtonUp,
            TimestampMs = ts + 50,
        });
    }

    /// <summary>Simulate a pre-recognized gesture (bypasses GestureRecognizer).</summary>
    public void SimulateGesture(ButtonGesture gesture, string buttonId = "main")
    {
        PreRecognizedGesture?.Invoke(this, new ButtonGestureEvent
        {
            ProviderId = ProviderId,
            ButtonId = buttonId,
            Gesture = gesture,
            TimestampMs = Environment.TickCount64,
        });
    }

    public void Dispose() { }
}
```

---

## 3. Swapping Providers in DI for Tests

BodyCam registers providers in `MauiProgram.cs` with `#if WINDOWS` / `#if ANDROID`
guards. In tests, you replace these registrations entirely.

### Option A: Environment Variable Switch (Simplest)

Add a check in `MauiProgram.cs`:

```csharp
// In MauiProgram.CreateMauiApp()
var useTestProviders = Environment.GetEnvironmentVariable("BODYCAM_TEST_MODE") == "1";

if (useTestProviders)
{
    var testAssetsPath = Environment.GetEnvironmentVariable("BODYCAM_TEST_ASSETS")
        ?? Path.Combine(AppContext.BaseDirectory, "TestAssets");

    // Audio input: silence (no mic data)
    builder.Services.AddSingleton<IAudioInputProvider>(
        new TestMicProvider(new byte[3200]));

    // Audio output: capture for assertions
    builder.Services.AddSingleton<IAudioOutputProvider, TestSpeakerProvider>();

    // Camera: test frames
    builder.Services.AddSingleton<ICameraProvider>(
        new TestCameraProvider(Path.Combine(testAssetsPath, "Frames")));

    // Buttons: test provider
    builder.Services.AddSingleton<IButtonInputProvider, TestButtonProvider>();
}
else
{
    // Normal platform provider registration
    #if WINDOWS
    builder.Services.AddSingleton<IAudioInputProvider, PlatformMicProvider>();
    // ... etc
    #endif
}
```

### Option B: Test-Specific MauiApp Builder (More Isolated)

Create a builder in the UI test project:

```csharp
// In BodyCam.UITests/TestAppBuilder.cs
public static class TestAppBuilder
{
    public static MauiApp Build(TestProviderOptions options)
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Register test providers instead of platform ones
        if (options.MicPcmPath is not null)
            builder.Services.AddSingleton<IAudioInputProvider>(
                new TestMicProvider(options.MicPcmPath));
        else
            builder.Services.AddSingleton<IAudioInputProvider>(
                new TestMicProvider(new byte[3200])); // silence

        builder.Services.AddSingleton<TestSpeakerProvider>();
        builder.Services.AddSingleton<IAudioOutputProvider>(sp =>
            sp.GetRequiredService<TestSpeakerProvider>());

        if (options.FramesDirectory is not null)
            builder.Services.AddSingleton<ICameraProvider>(
                new TestCameraProvider(options.FramesDirectory));

        builder.Services.AddSingleton<TestButtonProvider>();
        builder.Services.AddSingleton<IButtonInputProvider>(sp =>
            sp.GetRequiredService<TestButtonProvider>());

        // Managers work unchanged — they just get test providers
        builder.Services.AddSingleton<AudioInputManager>();
        builder.Services.AddSingleton<AudioOutputManager>();
        builder.Services.AddSingleton<CameraManager>();
        builder.Services.AddSingleton<ButtonInputManager>();

        // ... rest of normal registration (orchestrator, agents, etc.)

        return builder.Build();
    }
}

public class TestProviderOptions
{
    public string? MicPcmPath { get; set; }
    public string? FramesDirectory { get; set; }
}
```

---

## 4. Writing Tests with Test Providers

### Example: Look Command End-to-End

```csharp
[Collection("BodyCam")]
[Trait("Category", "UITest")]
public class LookCommandTests
{
    private readonly BodyCamFixture _fixture;

    public LookCommandTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToHome();
    }

    [Fact]
    public async Task LookButton_Click_CapturesFrameAndSendsToAI()
    {
        // Arrange — test camera provider has a frame loaded
        var page = _fixture.MainPage;

        // Act — click Look button
        page.LookButton.Click();

        // Assert — wait for status to change (AI responded)
        page.StatusLabel.WaitText("Describing...", timeoutMs: 5000);

        // Assert — the test speaker received audio output
        var speaker = _fixture.GetService<TestSpeakerProvider>();
        speaker.WasAudioPlayed.Should().BeTrue();
    }
}
```

### Example: Button Provider Programmatic Trigger

```csharp
[Fact]
public async Task SimulatedTap_TriggersLookAction()
{
    // Arrange
    var buttonProvider = _fixture.GetService<TestButtonProvider>();

    // Act — simulate a single tap (gesture recognizer maps to Look)
    buttonProvider.SimulateGesture(ButtonGesture.SingleTap);

    // Assert — same result as clicking Look button
    var page = _fixture.MainPage;
    page.StatusLabel.WaitText("Describing...", timeoutMs: 5000);
}
```

### Example: Audio Capture Assertion

```csharp
[Fact]
public async Task WakeWord_InAudio_StartsSession()
{
    // Arrange — load PCM with wake word
    var mic = _fixture.GetService<TestMicProvider>();

    // Act — mic provider emits chunks automatically on start
    // (AudioInputManager → WakeWordService detects keyword)

    // Assert — app transitions to Active state
    var page = _fixture.MainPage;
    page.StatusLabel.WaitText("Active", timeoutMs: 10000);
}
```

---

## 5. Test Assets

Store test data files in the UI test project:

```
BodyCam.UITests/
├── TestAssets/
│   ├── Audio/
│   │   ├── silence-1s.pcm          # 1 second of silence (16kHz, 16-bit, mono)
│   │   ├── wake-word-hey-camera.pcm # Pre-recorded wake word utterance
│   │   └── question-whats-this.pcm  # "What's this?" speech sample
│   └── Frames/
│       ├── office-desk.jpg          # Test scene: office desk with objects
│       ├── text-sign.jpg            # Test scene: readable text
│       └── empty-room.jpg           # Test scene: empty room
```

Mark files as `Content` / `CopyToOutputDirectory` in `.csproj`:

```xml
<ItemGroup>
  <Content Include="TestAssets\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

### Generating Test PCM Files

```powershell
# Generate 1 second of silence (16kHz, 16-bit, mono = 32000 bytes)
$bytes = [byte[]]::new(32000)
[System.IO.File]::WriteAllBytes("TestAssets/Audio/silence-1s.pcm", $bytes)

# Record from mic using ffmpeg (if you need real audio)
ffmpeg -f dshow -i audio="Microphone" -ac 1 -ar 16000 -f s16le -t 2 output.pcm
```

---

## 6. Fixture Setup for Provider Access

Extend `BodyCamFixture` to expose test providers:

```csharp
public class BodyCamFixture : MauiTestFixtureBase
{
    private TestSpeakerProvider? _testSpeaker;
    private TestButtonProvider? _testButtons;

    // ... existing setup ...

    /// <summary>
    /// Get a registered test service for assertions.
    /// Only works when BODYCAM_TEST_MODE=1.
    /// </summary>
    public T GetService<T>() where T : class
    {
        // Access via the running app's service provider
        var sp = MauiApplication.Current?.Services
            ?? throw new InvalidOperationException("App not running");
        return sp.GetRequiredService<T>();
    }

    /// <summary>Reset all test providers between tests.</summary>
    public void ResetProviders()
    {
        GetService<TestSpeakerProvider>().Reset();
    }
}
```

---

## 7. Key Design Decisions

### Why test providers live in BodyCam.UITests, not Brinell

Brinell is a **generic UI testing framework** — it knows about elements, pages,
and controls. It doesn't know about audio, cameras, or BodyCam-specific interfaces.
Test providers implement **BodyCam's own interfaces** (`IAudioInputProvider`, etc.),
so they belong in the BodyCam test project.

If the pattern proves reusable (e.g., other MAUI apps need test audio), promote
generic building blocks to `Brinell.Mocking` in Phase 4.

### Why managers don't need changes

`AudioInputManager`, `AudioOutputManager`, `CameraManager`, and `ButtonInputManager`
all accept providers via DI constructor injection (`IEnumerable<IXxxProvider>`).
They don't care whether the provider is real hardware or a test stub — the
abstraction boundary is the provider interface.

### Multiple providers in tests

Because DI registers multiple `IXxxProvider` implementations via
`AddSingleton<IXxxProvider>`, the managers receive all of them. In tests,
you can register multiple `TestMicProvider` instances with different PCM files
and switch between them via `AudioInputManager.SetActiveAsync("test-mic")`.

---

## 8. Checklist for Adding a New Test Provider

1. **Identify the interface** — which `IXxxProvider` does it implement?
2. **Create the class** in `BodyCam.UITests/TestInfrastructure/`
3. **Implement all members** — return canned data, capture output, or no-op
4. **Add assertion properties** — `WasXCalled`, `CapturedData`, `CallCount`
5. **Register in DI** — either via `TestAppBuilder` or env var switch
6. **Add a `Reset()` method** — so state can be cleared between tests
7. **Write a test** — verify the provider works end-to-end with the manager
