# M15 Phase 4 — Brinell Core Extensions (Optional)

**Goal:** Promote reusable patterns from Phases 1–3 into Brinell packages so other
MAUI apps (not just BodyCam) can use test-injectable audio, camera, and sensor
providers in their UI tests.

**Depends on:** Phase 3 (E2E tests validated the patterns work).

**Status:** Optional. Only proceed if the patterns prove genuinely reusable across
multiple apps. BodyCam-specific implementations stay in `BodyCam.Tests`.

---

## When to Promote

Move code to Brinell when **two or more** of these are true:

- Another app (samples, Brinell.Samples.Maui.App) needs the same test provider pattern
- The test infrastructure is stable (no breaking changes in 2+ milestones)
- The abstractions are generic (not tied to BodyCam's specific interfaces)

If only BodyCam uses them, leave them in `BodyCam.Tests/TestInfrastructure/`.

---

## What Gets Promoted

### From BodyCam-Specific → Generic Brinell

| BodyCam Implementation | Brinell Abstraction | Package |
|----------------------|---------------------|---------|
| `TestMicProvider` (IAudioInputProvider) | `MockAudioSource` (generic PCM emitter) | `Brinell.Mocking` |
| `TestSpeakerProvider` (IAudioOutputProvider) | `MockAudioSink` (generic PCM capture) | `Brinell.Mocking` |
| `TestCameraProvider` (ICameraProvider) | `MockFrameSource` (generic image emitter) | `Brinell.Mocking` |
| `TestButtonProvider` (IButtonInputProvider) | `MockInputSource` (generic event emitter) | `Brinell.Mocking` |
| `TestServiceAccessor` | `IMauiSensorContext` (test sensor DI) | `Brinell.Maui` |

### Key Difference

BodyCam's test providers implement BodyCam interfaces (`IAudioInputProvider`, etc.).
Brinell's abstractions would be **generic** — they don't know about BodyCam:

```csharp
// Brinell.Mocking — generic, no BodyCam dependency
namespace Brinell.Mocking.Audio;

public interface IMockAudioSource : IDisposable
{
    bool IsEmitting { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    event EventHandler<byte[]>? ChunkEmitted;
    void SimulateDisconnect();
}

public class MockAudioSource : IMockAudioSource
{
    // Same logic as TestMicProvider, but without IAudioInputProvider
}
```

Apps would then create thin adapters:

```csharp
// In BodyCam.Tests — adapter from Brinell generic to BodyCam interface
public class TestMicProvider : IAudioInputProvider
{
    private readonly MockAudioSource _source;

    public TestMicProvider(MockAudioSource source) { _source = source; }

    public string DisplayName => "Test Microphone";
    public string ProviderId => "test-mic";
    public bool IsAvailable => true;
    public bool IsCapturing => _source.IsEmitting;

    public event EventHandler<byte[]>? AudioChunkAvailable
    {
        add => _source.ChunkEmitted += value;
        remove => _source.ChunkEmitted -= value;
    }
    // ... delegate everything to _source
}
```

---

## Wave 1: Brinell.Mocking — Audio Mocks

### MockAudioSource

Generic PCM chunk emitter. Configured with byte array or file path, emits chunks
on a timer. No app-specific interfaces.

```csharp
namespace Brinell.Mocking.Audio;

/// <summary>
/// Emits pre-loaded PCM audio chunks on a configurable timer.
/// Use as a building block for app-specific mic/audio-input test providers.
/// </summary>
public class MockAudioSource : IMockAudioSource
{
    private readonly byte[] _pcmData;
    private readonly int _chunkSize;
    private readonly int _chunkIntervalMs;
    private CancellationTokenSource? _cts;

    public bool IsEmitting { get; private set; }
    public int ChunksEmitted { get; private set; }
    public bool FinishedPlaying { get; private set; }

    public event EventHandler<byte[]>? ChunkEmitted;
    public event EventHandler? Disconnected;

    public MockAudioSource(byte[] pcmData, int chunkSize = 3200, int chunkIntervalMs = 100)
    {
        _pcmData = pcmData;
        _chunkSize = chunkSize;
        _chunkIntervalMs = chunkIntervalMs;
    }

    public static MockAudioSource FromFile(string path, int chunkSize = 3200, int intervalMs = 100)
        => new(File.ReadAllBytes(path), chunkSize, intervalMs);

    public static MockAudioSource Silence(int durationMs = 1000, int sampleRate = 16000)
    {
        var bytes = sampleRate * 2 * durationMs / 1000; // 16-bit = 2 bytes/sample
        return new MockAudioSource(new byte[bytes]);
    }

    public Task StartAsync(CancellationToken ct = default) { /* same as TestMicProvider */ }
    public Task StopAsync() { /* same */ }
    public void SimulateDisconnect() { /* same */ }
    public void Dispose() => _cts?.Cancel();
}
```

### MockAudioSink

Generic PCM chunk capture. Records everything "played" for assertions.

```csharp
namespace Brinell.Mocking.Audio;

/// <summary>
/// Captures PCM chunks for test assertions.
/// Use as a building block for app-specific speaker/audio-output test providers.
/// </summary>
public class MockAudioSink : IMockAudioSink
{
    private readonly List<byte[]> _chunks = new();
    private readonly object _lock = new();

    public bool IsActive { get; private set; }
    public int SampleRate { get; private set; }

    // Assertion helpers
    public bool HasData { get { lock (_lock) return _chunks.Count > 0; } }
    public int ChunkCount { get { lock (_lock) return _chunks.Count; } }
    public int TotalBytes { get { lock (_lock) return _chunks.Sum(c => c.Length); } }

    public event EventHandler? Disconnected;

    public void Start(int sampleRate) { SampleRate = sampleRate; IsActive = true; }
    public void Stop() { IsActive = false; }

    public void Feed(byte[] pcmData)
    {
        lock (_lock) _chunks.Add(pcmData.ToArray());
    }

    public byte[] GetAllData()
    {
        lock (_lock) return _chunks.SelectMany(c => c).ToArray();
    }

    public void Reset()
    {
        lock (_lock) _chunks.Clear();
        IsActive = false;
        SampleRate = 0;
    }

    public void SimulateDisconnect()
    {
        IsActive = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() { }
}
```

---

## Wave 2: Brinell.Mocking — Frame Mocks

### MockFrameSource

Generic image frame provider. Loads JPEGs from disk or byte arrays, cycles through
them, tracks capture count.

```csharp
namespace Brinell.Mocking.Vision;

/// <summary>
/// Provides test image frames from memory or disk.
/// Cycles through frames in order. Tracks capture count.
/// </summary>
public class MockFrameSource : IMockFrameSource
{
    private readonly byte[][] _frames;
    private int _frameIndex;

    public bool IsAvailable { get; set; } = true;
    public int FramesCaptured { get; private set; }

    public event EventHandler? Disconnected;

    public MockFrameSource(params byte[][] frames) { _frames = frames; }

    public static MockFrameSource FromDirectory(string dir)
    {
        var files = Directory.GetFiles(dir, "*.jpg").OrderBy(f => f).ToArray();
        return new MockFrameSource(files.Select(File.ReadAllBytes).ToArray());
    }

    public static MockFrameSource SinglePixel()
        => new(MinimalJpeg());

    public byte[]? CaptureFrame()
    {
        if (_frames.Length == 0 || !IsAvailable) return null;
        var frame = _frames[_frameIndex++ % _frames.Length];
        FramesCaptured++;
        return frame;
    }

    public void SimulateDisconnect()
    {
        IsAvailable = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Reset() { _frameIndex = 0; FramesCaptured = 0; IsAvailable = true; }
    public void Dispose() { }

    private static byte[] MinimalJpeg() => Convert.FromBase64String("...");
}
```

---

## Wave 3: Brinell.Mocking — Input Mocks

### MockInputSource

Generic button/input event emitter. Fires raw events and pre-recognized gestures
programmatically. Not tied to any specific button event type — uses generic
event args.

```csharp
namespace Brinell.Mocking.Input;

/// <summary>
/// Generic input event emitter for test automation.
/// Fires named events with string payloads.
/// </summary>
public class MockInputSource : IMockInputSource
{
    public bool IsActive { get; private set; }
    public int EventCount { get; private set; }
    public string? LastEventName { get; private set; }

    public event EventHandler<MockInputEvent>? InputReceived;
    public event EventHandler? Disconnected;

    public void Start() { IsActive = true; }
    public void Stop() { IsActive = false; }

    public void FireEvent(string name, string? payload = null)
    {
        InputReceived?.Invoke(this, new MockInputEvent(name, payload));
        EventCount++;
        LastEventName = name;
    }

    public void SimulateDisconnect()
    {
        IsActive = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Reset() { EventCount = 0; LastEventName = null; }
    public void Dispose() { }
}

public record MockInputEvent(string Name, string? Payload);
```

---

## Wave 4: Brinell.Maui — IMauiSensorContext

Add sensor/provider context to the MAUI test framework so tests can access
injected test services through a typed interface.

```csharp
namespace Brinell.Maui.Interfaces;

/// <summary>
/// Provides access to mock sensors and providers registered in the app under test.
/// Available when the app is launched in test mode.
/// </summary>
public interface IMauiSensorContext
{
    /// <summary>Get a registered mock service by type.</summary>
    T GetMock<T>() where T : class;

    /// <summary>Reset all mock providers to clean state.</summary>
    void ResetAll();

    /// <summary>Whether the app is running in test/mock mode.</summary>
    bool IsTestMode { get; }
}
```

Implementation wraps the app's `IServiceProvider`:

```csharp
namespace Brinell.Maui.Context;

public class MauiSensorContext : IMauiSensorContext
{
    private readonly IServiceProvider _services;
    private readonly List<Action> _resetActions = new();

    public bool IsTestMode { get; }

    public MauiSensorContext(IServiceProvider services, bool isTestMode)
    {
        _services = services;
        IsTestMode = isTestMode;
    }

    public T GetMock<T>() where T : class
    {
        if (!IsTestMode)
            throw new InvalidOperationException("Not in test mode");
        return _services.GetRequiredService<T>();
    }

    public void RegisterReset(Action reset) => _resetActions.Add(reset);

    public void ResetAll()
    {
        foreach (var reset in _resetActions) reset();
    }
}
```

---

## Wave 5: Documentation & Samples

### Brinell Docs Update

Add a new doc to `Brinell/docs/`:

```
docs/
├── 20-mock-providers.md    ← NEW
```

Contents:
- How to create mock audio/camera/input sources
- How to wire them into a MAUI app's DI for testing
- Example: testing a camera-based feature without a real camera
- Example: verifying audio output without speakers

### Sample Project

Add mock provider usage to `Brinell.Samples.Maui.App`:

```
samples/
├── Brinell.Samples.Maui.App/
│   ├── TestMode/
│   │   ├── MockCameraService.cs    ← Uses MockFrameSource
│   │   └── MockAudioService.cs     ← Uses MockAudioSource + MockAudioSink
```

---

## Package Dependencies

```
Brinell.Mocking (new types)
├── Brinell.Mocking.Audio.MockAudioSource
├── Brinell.Mocking.Audio.MockAudioSink
├── Brinell.Mocking.Vision.MockFrameSource
└── Brinell.Mocking.Input.MockInputSource

Brinell.Maui (extension)
└── Brinell.Maui.Interfaces.IMauiSensorContext
    Brinell.Maui.Context.MauiSensorContext

BodyCam.Tests (adapters — NOT promoted)
├── TestMicProvider : IAudioInputProvider      → wraps MockAudioSource
├── TestSpeakerProvider : IAudioOutputProvider  → wraps MockAudioSink
├── TestCameraProvider : ICameraProvider        → wraps MockFrameSource
└── TestButtonProvider : IButtonInputProvider   → wraps MockInputSource
```

---

## Verification

After all waves:

1. `Brinell.Mocking` compiles with new types, no BodyCam dependency
2. `Brinell.Maui` compiles with `IMauiSensorContext`
3. BodyCam adapters still pass all Phase 3 tests (wrapping Brinell generics)
4. Sample project demonstrates the pattern
5. Documentation merged to Brinell docs
