# M1 Implementation — Step 6: Rewire Agents for Realtime-First

**Depends on:** Step 2 (IRealtimeClient), Step 3+4 (audio services), Step 5 (RealtimeClient impl)
**Produces:** Updated `VoiceInputAgent`, `VoiceOutputAgent`, `ConversationAgent` for event-driven Realtime flow

---

## Why This Step?
The current agents use a pipeline model (VoiceIn → Conversation → VoiceOut) with `IAsyncEnumerable`. The Realtime API is event-driven — all three concerns (STT, reasoning, TTS) happen in the single WebSocket session. Agents need to react to `IRealtimeClient` events, not drive the flow.

---

## Tasks

### 6.1 — Rewrite `VoiceInputAgent`

**Role:** Pipes mic audio → `IRealtimeClient`. That's it. No transcript handling (events do that).

**File:** `src/BodyCam/Agents/VoiceInputAgent.cs`

```csharp
using BodyCam.Services;

namespace BodyCam.Agents;

public class VoiceInputAgent
{
    private readonly IAudioInputService _audioInput;
    private readonly IRealtimeClient _realtime;

    public VoiceInputAgent(IAudioInputService audioInput, IRealtimeClient realtime)
    {
        _audioInput = audioInput;
        _realtime = realtime;
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
                await _realtime.SendAudioChunkAsync(chunk);
        }
        catch (Exception)
        {
            // Swallow — don't crash the audio capture thread.
            // Errors will surface via IRealtimeClient.ErrorOccurred event.
        }
    }
}
```

### 6.2 — Rewrite `VoiceOutputAgent`

**Role:** Receives `AudioDelta` events → plays through speaker. Tracks playback position for interruption handling.

**File:** `src/BodyCam/Agents/VoiceOutputAgent.cs`

```csharp
using BodyCam.Models;
using BodyCam.Services;

namespace BodyCam.Agents;

public class VoiceOutputAgent
{
    private readonly IAudioOutputService _audioOutput;
    private readonly AudioPlaybackTracker _tracker = new();

    public AudioPlaybackTracker Tracker => _tracker;

    public VoiceOutputAgent(IAudioOutputService audioOutput)
    {
        _audioOutput = audioOutput;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _audioOutput.StartAsync(ct);
    }

    public async Task StopAsync()
    {
        _tracker.Reset();
        await _audioOutput.StopAsync();
    }

    /// <summary>Called by orchestrator when AudioDelta event fires.</summary>
    public async Task PlayAudioDeltaAsync(byte[] pcmData, CancellationToken ct = default)
    {
        await _audioOutput.PlayChunkAsync(pcmData, ct);
        _tracker.BytesPlayed += pcmData.Length;
    }

    /// <summary>Called when user interrupts (speech_started while playing).</summary>
    public void HandleInterruption()
    {
        _audioOutput.ClearBuffer();
        // Don't reset tracker — orchestrator reads PlayedMs for truncation
    }

    /// <summary>Called after truncation is sent. Reset for next response.</summary>
    public void ResetTracker()
    {
        _tracker.Reset();
    }

    public void SetCurrentItem(string itemId)
    {
        _tracker.CurrentItemId = itemId;
    }
}
```

**Note:** `VoiceOutputAgent` no longer depends on `IRealtimeClient` or `IOpenAiStreamingClient`. It's a pure audio player. The orchestrator wires events to it.

### 6.3 — Simplify `ConversationAgent`

**Role:** Local history tracker. No API calls — Realtime handles reasoning.

**File:** `src/BodyCam/Agents/ConversationAgent.cs`

```csharp
using BodyCam.Models;

namespace BodyCam.Agents;

public class ConversationAgent
{
    public void AddUserMessage(string transcript, SessionContext session)
    {
        session.Messages.Add(new ChatMessage { Role = "user", Content = transcript });
    }

    public void AddAssistantMessage(string transcript, SessionContext session)
    {
        session.Messages.Add(new ChatMessage { Role = "assistant", Content = transcript });
    }
}
```

No more `ProcessAsync` — the Realtime API does the reasoning.

### 6.4 — Update tests

- **VoiceInputAgent tests:** Verify `StartAsync` subscribes to `AudioChunkAvailable`, verify chunks forwarded to `IRealtimeClient.SendAudioChunkAsync`
- **VoiceOutputAgent tests:** Verify `PlayAudioDeltaAsync` calls `PlayChunkAsync` and increments `BytesPlayed`. Verify `HandleInterruption` calls `ClearBuffer`.
- **ConversationAgent tests:** Verify `AddUserMessage` / `AddAssistantMessage` add to session history.

---

## Verification

- [ ] Build succeeds
- [ ] All agent unit tests pass with new signatures
- [ ] `VoiceInputAgent` forwards audio chunks to `IRealtimeClient`
- [ ] `VoiceOutputAgent` plays audio and tracks bytes
- [ ] `ConversationAgent` tracks history without API calls
- [ ] No references to `IOpenAiStreamingClient` remain anywhere in codebase

---

## Files Changed

| File | Action |
|------|--------|
| `Agents/VoiceInputAgent.cs` | REWRITE |
| `Agents/VoiceOutputAgent.cs` | REWRITE |
| `Agents/ConversationAgent.cs` | SIMPLIFY |
| `Tests/Agents/VoiceInputAgentTests.cs` | REWRITE |
| `Tests/Agents/VoiceOutputAgentTests.cs` | REWRITE |
| `Tests/Agents/ConversationAgentTests.cs` | SIMPLIFY |
