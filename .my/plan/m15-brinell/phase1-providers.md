# M15 Phase 1 — Test Provider Implementations

**Goal:** Create four test provider classes and test asset files so BodyCam can
run with fake hardware in test mode.

**Depends on:** M11–M14 provider interfaces exist and compile.

---

## What We're Building

Four classes that implement BodyCam's provider interfaces with test-controllable
behavior. These replace real hardware (mic, speaker, camera, buttons) so the full
pipeline can run in CI without devices.

| Class | Implements | Behavior |
|-------|-----------|----------|
| `TestMicProvider` | `IAudioInputProvider` | Emits pre-recorded PCM chunks on a timer |
| `TestSpeakerProvider` | `IAudioOutputProvider` | Captures played audio for assertion |
| `TestCameraProvider` | `ICameraProvider` | Returns JPEG frames from disk |
| `TestButtonProvider` | `IButtonInputProvider` | Fires button/gesture events programmatically |

---

## Project Location

All test providers go in a shared infrastructure folder inside `BodyCam.Tests`:

```
src/BodyCam.Tests/
├── TestInfrastructure/
│   ├── Providers/
│   │   ├── TestMicProvider.cs
│   │   ├── TestSpeakerProvider.cs
│   │   ├── TestCameraProvider.cs
│   │   └── TestButtonProvider.cs
│   └── TestAssets/
│       ├── Audio/
│       │   └── silence-1s.pcm
│       └── Frames/
│           └── test-frame.jpg
```

Why `BodyCam.Tests` and not `BodyCam.UITests`: unit and integration tests need
these providers too. UI tests can reference them via project reference.

---

## Wave 1: TestMicProvider

```csharp
using BodyCam.Services.Audio;

namespace BodyCam.Tests.TestInfrastructure.Providers;

/// <summary>
/// Feeds pre-recorded PCM audio chunks into the pipeline on a timer.
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

    // --- Assertion helpers ---
    public int ChunksEmitted { get; private set; }
    public bool FinishedPlaying { get; private set; }

    /// <param name="pcmFilePath">Path to raw PCM file (16-bit, 16kHz, mono).</param>
    /// <param name="chunkSize">Bytes per chunk (default 3200 = 100ms at 16kHz).</param>
    /// <param name="chunkIntervalMs">Milliseconds between chunks.</param>
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
        ChunksEmitted = 0;
        FinishedPlaying = false;
        _ = EmitChunksAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        IsCapturing = false;
        return Task.CompletedTask;
    }

    /// <summary>Simulate a mic disconnect.</summary>
    public void SimulateDisconnect()
    {
        IsCapturing = false;
        _cts?.Cancel();
        Disconnected?.Invoke(this, EventArgs.Empty);
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
            ChunksEmitted++;

            offset += size;
            try { await Task.Delay(_chunkIntervalMs, ct).ConfigureAwait(false); }
            catch (TaskCanceledException) { break; }
        }

        FinishedPlaying = true;
        IsCapturing = false;
    }

    public void Dispose() => _cts?.Cancel();
}
```

### Unit Tests

```csharp
public class TestMicProviderTests
{
    [Fact]
    public async Task StartAsync_EmitsChunks()
    {
        var chunks = new List<byte[]>();
        var mic = new TestMicProvider(new byte[9600]); // 3 chunks at 3200
        mic.AudioChunkAvailable += (_, c) => chunks.Add(c);

        await mic.StartAsync();
        await Task.Delay(500); // let chunks flow

        chunks.Should().HaveCountGreaterOrEqualTo(3);
        mic.ChunksEmitted.Should().Be(chunks.Count);
    }

    [Fact]
    public async Task StopAsync_StopsEmitting()
    {
        var mic = new TestMicProvider(new byte[320000]); // many chunks
        await mic.StartAsync();
        await Task.Delay(50);
        await mic.StopAsync();

        mic.IsCapturing.Should().BeFalse();
    }

    [Fact]
    public void SimulateDisconnect_FiresEvent()
    {
        var disconnected = false;
        var mic = new TestMicProvider(new byte[3200]);
        mic.Disconnected += (_, _) => disconnected = true;

        mic.SimulateDisconnect();

        disconnected.Should().BeTrue();
    }
}
```

---

## Wave 2: TestSpeakerProvider

```csharp
using BodyCam.Services.Audio;

namespace BodyCam.Tests.TestInfrastructure.Providers;

/// <summary>
/// Captures all PCM chunks the app tried to play for test assertions.
/// </summary>
public class TestSpeakerProvider : IAudioOutputProvider
{
    private readonly List<byte[]> _chunks = new();
    private readonly object _lock = new();

    public string DisplayName => "Test Speaker";
    public string ProviderId => "test-speaker";
    public bool IsAvailable => true;
    public bool IsPlaying { get; private set; }
    public int SampleRate { get; private set; }

    public event EventHandler? Disconnected;

    // --- Assertion helpers ---
    public IReadOnlyList<byte[]> CapturedChunks { get { lock (_lock) return _chunks.ToList(); } }
    public int TotalBytesPlayed { get { lock (_lock) return _chunks.Sum(c => c.Length); } }
    public int ChunkCount { get { lock (_lock) return _chunks.Count; } }
    public bool WasAudioPlayed { get { lock (_lock) return _chunks.Count > 0; } }

    /// <summary>Concatenated captured audio.</summary>
    public byte[] GetCapturedBytes()
    {
        lock (_lock) return _chunks.SelectMany(c => c).ToArray();
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        SampleRate = sampleRate;
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
        lock (_lock) _chunks.Add(pcmData.ToArray());
        return Task.CompletedTask;
    }

    public void ClearBuffer()
    {
        lock (_lock) _chunks.Clear();
    }

    /// <summary>Simulate a speaker disconnect.</summary>
    public void SimulateDisconnect()
    {
        IsPlaying = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reset all state between tests.</summary>
    public void Reset()
    {
        lock (_lock) _chunks.Clear();
        IsPlaying = false;
        SampleRate = 0;
    }

    public void Dispose() { }
}
```

### Unit Tests

```csharp
public class TestSpeakerProviderTests
{
    [Fact]
    public async Task PlayChunkAsync_CapturesData()
    {
        var speaker = new TestSpeakerProvider();
        await speaker.StartAsync(24000);

        await speaker.PlayChunkAsync(new byte[] { 1, 2, 3 });
        await speaker.PlayChunkAsync(new byte[] { 4, 5 });

        speaker.ChunkCount.Should().Be(2);
        speaker.TotalBytesPlayed.Should().Be(5);
        speaker.WasAudioPlayed.Should().BeTrue();
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var speaker = new TestSpeakerProvider();
        speaker.PlayChunkAsync(new byte[] { 1 }).Wait();

        speaker.Reset();

        speaker.WasAudioPlayed.Should().BeFalse();
        speaker.ChunkCount.Should().Be(0);
    }
}
```

---

## Wave 3: TestCameraProvider

```csharp
using BodyCam.Services.Camera;
using System.Runtime.CompilerServices;

namespace BodyCam.Tests.TestInfrastructure.Providers;

/// <summary>
/// Returns JPEG frames from a list of byte arrays or a directory.
/// Cycles through frames in order. Tracks capture count for assertions.
/// </summary>
public class TestCameraProvider : ICameraProvider
{
    private readonly byte[][] _frames;
    private int _frameIndex;

    public string DisplayName => "Test Camera";
    public string ProviderId => "test-camera";
    public bool IsAvailable { get; set; } = true;

    public event EventHandler? Disconnected;

    // --- Assertion helpers ---
    public int FramesCaptured { get; private set; }

    /// <param name="framesDirectory">Directory containing .jpg files.</param>
    public TestCameraProvider(string framesDirectory)
    {
        var files = Directory.GetFiles(framesDirectory, "*.jpg")
            .OrderBy(f => f).ToArray();
        _frames = files.Select(File.ReadAllBytes).ToArray();
    }

    /// <summary>Create with a single test frame.</summary>
    public TestCameraProvider(byte[] singleFrame)
    {
        _frames = [singleFrame];
    }

    /// <summary>Create with multiple frames in order.</summary>
    public TestCameraProvider(params byte[][] frames)
    {
        _frames = frames;
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (_frames.Length == 0 || !IsAvailable)
            return Task.FromResult<byte[]?>(null);

        var frame = _frames[_frameIndex % _frames.Length];
        _frameIndex++;
        FramesCaptured++;
        return Task.FromResult<byte[]?>(frame);
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested && IsAvailable)
        {
            if (_frames.Length == 0) yield break;
            var frame = _frames[_frameIndex % _frames.Length];
            _frameIndex++;
            FramesCaptured++;
            yield return frame;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Simulate camera disconnect.</summary>
    public void SimulateDisconnect()
    {
        IsAvailable = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reset frame index and capture count.</summary>
    public void Reset()
    {
        _frameIndex = 0;
        FramesCaptured = 0;
        IsAvailable = true;
    }

    public void Dispose() { }
}
```

### Unit Tests

```csharp
public class TestCameraProviderTests
{
    [Fact]
    public async Task CaptureFrameAsync_CyclesThroughFrames()
    {
        var frame1 = new byte[] { 0xFF, 0xD8, 1 };
        var frame2 = new byte[] { 0xFF, 0xD8, 2 };
        var camera = new TestCameraProvider(frame1, frame2);

        var f1 = await camera.CaptureFrameAsync();
        var f2 = await camera.CaptureFrameAsync();
        var f3 = await camera.CaptureFrameAsync(); // wraps to frame1

        f1.Should().BeEquivalentTo(frame1);
        f2.Should().BeEquivalentTo(frame2);
        f3.Should().BeEquivalentTo(frame1);
        camera.FramesCaptured.Should().Be(3);
    }

    [Fact]
    public async Task CaptureFrameAsync_ReturnsNull_WhenUnavailable()
    {
        var camera = new TestCameraProvider(new byte[] { 1 });
        camera.IsAvailable = false;

        var result = await camera.CaptureFrameAsync();

        result.Should().BeNull();
    }

    [Fact]
    public void SimulateDisconnect_FiresEvent()
    {
        var disconnected = false;
        var camera = new TestCameraProvider(new byte[] { 1 });
        camera.Disconnected += (_, _) => disconnected = true;

        camera.SimulateDisconnect();

        disconnected.Should().BeTrue();
        camera.IsAvailable.Should().BeFalse();
    }
}
```

---

## Wave 4: TestButtonProvider

```csharp
using BodyCam.Services.Input;

namespace BodyCam.Tests.TestInfrastructure.Providers;

/// <summary>
/// Fires button events programmatically from test code.
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

    // --- Assertion helpers ---
    public int ClickCount { get; private set; }
    public int GestureCount { get; private set; }
    public ButtonGesture? LastGesture { get; private set; }

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

    /// <summary>Simulate a button click (ButtonDown + ButtonUp with 50ms gap).</summary>
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
        ClickCount++;
    }

    /// <summary>
    /// Simulate a pre-recognized gesture (bypasses GestureRecognizer).
    /// Use this when you want to test ActionMap → DispatchAction directly.
    /// </summary>
    public void SimulateGesture(ButtonGesture gesture, string buttonId = "main")
    {
        PreRecognizedGesture?.Invoke(this, new ButtonGestureEvent
        {
            ProviderId = ProviderId,
            ButtonId = buttonId,
            Gesture = gesture,
            TimestampMs = Environment.TickCount64,
        });
        GestureCount++;
        LastGesture = gesture;
    }

    /// <summary>Simulate device disconnect.</summary>
    public void SimulateDisconnect()
    {
        IsActive = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reset counters between tests.</summary>
    public void Reset()
    {
        ClickCount = 0;
        GestureCount = 0;
        LastGesture = null;
    }

    public void Dispose() { }
}
```

### Unit Tests

```csharp
public class TestButtonProviderTests
{
    [Fact]
    public void SimulateClick_FiresDownThenUp()
    {
        var events = new List<RawButtonEventType>();
        var btn = new TestButtonProvider();
        btn.RawButtonEvent += (_, e) => events.Add(e.EventType);

        btn.SimulateClick();

        events.Should().ContainInOrder(
            RawButtonEventType.ButtonDown,
            RawButtonEventType.ButtonUp);
        btn.ClickCount.Should().Be(1);
    }

    [Fact]
    public void SimulateGesture_FiresPreRecognized()
    {
        ButtonGestureEvent? received = null;
        var btn = new TestButtonProvider();
        btn.PreRecognizedGesture += (_, e) => received = e;

        btn.SimulateGesture(ButtonGesture.DoubleTap);

        received.Should().NotBeNull();
        received!.Gesture.Should().Be(ButtonGesture.DoubleTap);
        btn.LastGesture.Should().Be(ButtonGesture.DoubleTap);
    }
}
```

---

## Wave 5: Test Assets

### Generating Assets

```powershell
# Create test asset directories
$audioDir = "src/BodyCam.Tests/TestInfrastructure/TestAssets/Audio"
$framesDir = "src/BodyCam.Tests/TestInfrastructure/TestAssets/Frames"
New-Item -ItemType Directory -Path $audioDir, $framesDir -Force

# 1 second of silence (16kHz, 16-bit, mono = 32000 bytes)
[System.IO.File]::WriteAllBytes("$audioDir/silence-1s.pcm", [byte[]]::new(32000))

# Minimal valid JPEG (1x1 white pixel) for camera tests
$jpegBytes = [Convert]::FromBase64String(
    "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRof" +
    "Hh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwh" +
    "MjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAAR" +
    "CAABAAEDASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAA" +
    "AAD/xAAUAQEAAAAAAAAAAAAAAAAAAAAA/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMR" +
    "AD8AKwA//9k=")
[System.IO.File]::WriteAllBytes("$framesDir/test-frame.jpg", $jpegBytes)
```

### .csproj Changes

Add to `BodyCam.Tests.csproj`:

```xml
<ItemGroup>
  <Content Include="TestInfrastructure\TestAssets\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

---

## Verification

After all waves:

1. `dotnet build src/BodyCam.Tests/BodyCam.Tests.csproj` — compiles
2. New unit tests for each provider pass
3. Test assets copied to output directory
4. Providers can be constructed in tests without platform dependencies
