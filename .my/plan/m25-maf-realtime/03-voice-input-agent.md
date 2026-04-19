# Step 03 — Update VoiceInputAgent

Replace `IRealtimeClient` dependency with a delegate-based audio sink controlled by the orchestrator.

**Depends on:** Step 02  
**Touches:** `Agents/VoiceInputAgent.cs`  
**Tests affected:** `VoiceInputAgentTests.cs` (updated in step 06)

---

## What to Do

### 3.1 — Replace `IRealtimeClient` with audio sink delegate

```
src/BodyCam/Agents/VoiceInputAgent.cs
```

**Current file (entire):**
```csharp
using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;

namespace BodyCam.Agents;

public class VoiceInputAgent
{
    private readonly IAudioInputService _audioInput;
    private readonly IRealtimeClient _realtime;
    private readonly AecProcessor? _aec;

    public VoiceInputAgent(IAudioInputService audioInput, IRealtimeClient realtime, AecProcessor? aec = null)
    {
        _audioInput = audioInput;
        _realtime = realtime;
        _aec = aec;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _audioInput.AudioChunkAvailable += OnAudioChunk;
        await _audioInput.StartAsync(ct);
    }

    public async Task StopAsync()
    {
        _audioInput.AudioChunkAvailable -= OnAudioChunk;
        await _audioInput.StopAsync();
    }

    private async void OnAudioChunk(object? sender, byte[] chunk)
    {
        try
        {
            if (_realtime.IsConnected)
            {
                byte[] processed = _aec is not null ? _aec.ProcessCapture(chunk) : chunk;
                await _realtime.SendAudioChunkAsync(processed);
            }
        }
        catch (Exception)
        {
            // Swallow — don't crash the audio capture thread.
        }
    }
}
```

**Replace with:**
```csharp
using BodyCam.Services;
using BodyCam.Services.Audio.WebRtcApm;

namespace BodyCam.Agents;

public class VoiceInputAgent
{
    private readonly IAudioInputService _audioInput;
    private readonly AecProcessor? _aec;

    private Func<byte[], CancellationToken, Task>? _audioSink;
    private volatile bool _isConnected;

    public VoiceInputAgent(IAudioInputService audioInput, AecProcessor? aec = null)
    {
        _audioInput = audioInput;
        _aec = aec;
    }

    /// <summary>
    /// Sets the delegate that receives processed audio chunks.
    /// Called by AgentOrchestrator after session creation.
    /// </summary>
    public void SetAudioSink(Func<byte[], CancellationToken, Task>? sink) => _audioSink = sink;

    /// <summary>
    /// Controls whether audio chunks are forwarded to the sink.
    /// </summary>
    public void SetConnected(bool connected) => _isConnected = connected;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _audioInput.AudioChunkAvailable += OnAudioChunk;
        await _audioInput.StartAsync(ct);
    }

    public async Task StopAsync()
    {
        _audioInput.AudioChunkAvailable -= OnAudioChunk;
        await _audioInput.StopAsync();
    }

    private async void OnAudioChunk(object? sender, byte[] chunk)
    {
        try
        {
            if (_isConnected && _audioSink is not null)
            {
                byte[] processed = _aec is not null ? _aec.ProcessCapture(chunk) : chunk;
                await _audioSink(processed, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Swallow — don't crash the audio capture thread.
        }
    }
}
```

**Changes:**
- Constructor no longer takes `IRealtimeClient`
- `_realtime.IsConnected` → `_isConnected` (volatile bool, set by orchestrator)
- `_realtime.SendAudioChunkAsync(processed)` → `_audioSink(processed, CancellationToken.None)`
- New `SetAudioSink()` and `SetConnected()` methods

### 3.2 — Update DI registration

```
src/BodyCam/ServiceExtensions.cs — AddAgents()
```

No change needed — `VoiceInputAgent` is already registered as `services.AddSingleton<VoiceInputAgent>()`. The DI container will use the new constructor automatically since `IRealtimeClient` is no longer a parameter.

### 3.3 — Wire up in AgentOrchestrator.StartAsync()

In step 02's `StartAsync()` rewrite, after session creation, add:

```csharp
// Wire audio pipeline to MAF session
_voiceIn.SetAudioSink(async (pcm, ct) =>
{
    var audioMsg = new InputAudioBufferAppendRealtimeClientMessage(
        new DataContent(pcm, "audio/pcm"));
    await _session!.SendAsync(audioMsg, ct);
});
_voiceIn.SetConnected(true);
```

In `StopAsync()`, before stopping voice input:

```csharp
_voiceIn.SetConnected(false);
_voiceIn.SetAudioSink(null);
```

### 3.4 — Wire up in ReconnectAsync()

After successful reconnection, re-wire the sink:

```csharp
_voiceIn.SetAudioSink(async (pcm, ct) =>
{
    var audioMsg = new InputAudioBufferAppendRealtimeClientMessage(
        new DataContent(pcm, "audio/pcm"));
    await _session!.SendAsync(audioMsg, ct);
});
_voiceIn.SetConnected(true);
```

---

## Design Rationale

- **Why a delegate instead of `IRealtimeClientSession`?** The agent doesn't need the full session interface — it only sends audio. A delegate keeps the dependency minimal and testable. Tests pass a simple lambda instead of mocking an entire session.
- **Why `volatile bool`?** `_isConnected` is read from the audio capture thread (event handler) and written from the main thread (orchestrator). `volatile` ensures visibility without a lock.
- **Why `CancellationToken.None`?** The audio chunk handler is fire-and-forget from the audio capture thread. The orchestrator controls lifecycle via `SetConnected(false)`.

---

## Acceptance Criteria

- [ ] `VoiceInputAgent` constructor no longer takes `IRealtimeClient`
- [ ] Audio flows through delegate: mic → AEC → sink → session.SendAsync
- [ ] `SetConnected(false)` stops audio forwarding
- [ ] `SetAudioSink(null)` is safe (no NRE)
- [ ] Build compiles
