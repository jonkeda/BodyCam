# M1 Implementation — Step 7: Event-Driven Orchestrator

**Depends on:** Step 5 (RealtimeClient), Step 6 (rewired agents)
**Produces:** `AgentOrchestrator` that wires `IRealtimeClient` events → agents → UI

---

## Why This Step?
This is the glue. The orchestrator subscribes to `IRealtimeClient` events and routes them to the right agent. It replaces the old pipeline (VoiceIn→Conversation→VoiceOut) with an event-driven model.

---

## Tasks

### 7.1 — Rewrite `AgentOrchestrator`

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs`

```csharp
using BodyCam.Agents;
using BodyCam.Models;
using BodyCam.Services;

namespace BodyCam.Orchestration;

public class AgentOrchestrator
{
    private readonly VoiceInputAgent _voiceIn;
    private readonly VoiceOutputAgent _voiceOut;
    private readonly ConversationAgent _conversation;
    private readonly VisionAgent _vision;
    private readonly IRealtimeClient _realtime;

    private CancellationTokenSource? _cts;
    public SessionContext Session { get; } = new();

    public event EventHandler<string>? TranscriptUpdated;
    public event EventHandler<string>? DebugLog;

    public bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;

    public AgentOrchestrator(
        VoiceInputAgent voiceIn,
        VoiceOutputAgent voiceOut,
        ConversationAgent conversation,
        VisionAgent vision,
        IRealtimeClient realtime)
    {
        _voiceIn = voiceIn;
        _voiceOut = voiceOut;
        _conversation = conversation;
        _vision = vision;
        _realtime = realtime;
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        Session.IsActive = true;

        // Subscribe to Realtime events
        _realtime.AudioDelta += OnAudioDelta;
        _realtime.OutputTranscriptDelta += OnOutputTranscriptDelta;
        _realtime.OutputTranscriptCompleted += OnOutputTranscriptCompleted;
        _realtime.InputTranscriptCompleted += OnInputTranscriptCompleted;
        _realtime.SpeechStarted += OnSpeechStarted;
        _realtime.ResponseDone += OnResponseDone;
        _realtime.ErrorOccurred += OnError;

        // Connect to OpenAI
        await _realtime.ConnectAsync(_cts.Token);
        DebugLog?.Invoke(this, "Realtime connected.");

        // Start audio pipeline
        await _voiceOut.StartAsync(_cts.Token);
        await _voiceIn.StartAsync(_cts.Token);
        DebugLog?.Invoke(this, "Audio pipeline started.");
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        // Unsubscribe events
        _realtime.AudioDelta -= OnAudioDelta;
        _realtime.OutputTranscriptDelta -= OnOutputTranscriptDelta;
        _realtime.OutputTranscriptCompleted -= OnOutputTranscriptCompleted;
        _realtime.InputTranscriptCompleted -= OnInputTranscriptCompleted;
        _realtime.SpeechStarted -= OnSpeechStarted;
        _realtime.ResponseDone -= OnResponseDone;
        _realtime.ErrorOccurred -= OnError;

        _cts?.Cancel();

        await _voiceIn.StopAsync();
        await _voiceOut.StopAsync();
        await _realtime.DisconnectAsync();

        Session.IsActive = false;
        _cts?.Dispose();
        _cts = null;
        DebugLog?.Invoke(this, "Orchestrator stopped.");
    }

    // --- Event handlers ---

    private async void OnAudioDelta(object? sender, byte[] pcmData)
    {
        try { await _voiceOut.PlayAudioDeltaAsync(pcmData); }
        catch (Exception ex) { DebugLog?.Invoke(this, $"Playback error: {ex.Message}"); }
    }

    private void OnOutputTranscriptDelta(object? sender, string delta)
    {
        // Partial transcript of AI speaking — update UI incrementally
        TranscriptUpdated?.Invoke(this, $"AI: {delta}");
    }

    private void OnOutputTranscriptCompleted(object? sender, string transcript)
    {
        _conversation.AddAssistantMessage(transcript, Session);
        DebugLog?.Invoke(this, $"AI said: {transcript}");
    }

    private void OnInputTranscriptCompleted(object? sender, string transcript)
    {
        _conversation.AddUserMessage(transcript, Session);
        TranscriptUpdated?.Invoke(this, $"You: {transcript}");
        DebugLog?.Invoke(this, $"User said: {transcript}");
    }

    private async void OnSpeechStarted(object? sender, EventArgs e)
    {
        // User started speaking while AI may be playing audio → interruption
        if (_voiceOut.Tracker.CurrentItemId is not null)
        {
            _voiceOut.HandleInterruption();
            var itemId = _voiceOut.Tracker.CurrentItemId;
            var playedMs = _voiceOut.Tracker.PlayedMs;
            _voiceOut.ResetTracker();

            try
            {
                await _realtime.TruncateResponseAudioAsync(itemId, playedMs);
                DebugLog?.Invoke(this, $"Interrupted at {playedMs}ms.");
            }
            catch (Exception ex)
            {
                DebugLog?.Invoke(this, $"Truncation error: {ex.Message}");
            }
        }
    }

    private void OnResponseDone(object? sender, RealtimeResponseInfo info)
    {
        _voiceOut.ResetTracker();
        DebugLog?.Invoke(this, $"Response complete: {info.ResponseId}");
    }

    private void OnError(object? sender, string error)
    {
        DebugLog?.Invoke(this, $"Realtime error: {error}");
    }
}
```

### 7.2 — Update `MainViewModel` (if needed)

The `MainViewModel` already subscribes to `TranscriptUpdated` and `DebugLog`. The event signatures haven't changed, so it should work as-is. Verify.

### 7.3 — Update orchestrator tests

Test the event-driven flow with mocks:
1. Mock `IRealtimeClient` — raise events, verify agents are called
2. Verify `AudioDelta` → `VoiceOutputAgent.PlayAudioDeltaAsync`
3. Verify `InputTranscriptCompleted` → `ConversationAgent.AddUserMessage` + `TranscriptUpdated`
4. Verify `SpeechStarted` when playing → calls `HandleInterruption` + `TruncateResponseAudioAsync`
5. Verify `StopAsync` unsubscribes all events

### 7.4 — Update integration tests

Full pipeline test with mock `IRealtimeClient`:
1. `StartAsync` → verify Realtime connected + audio started
2. Simulate audio delta events → verify playback
3. Simulate transcript events → verify session history updated
4. `StopAsync` → verify clean shutdown

---

## Verification

- [ ] Build succeeds
- [ ] `StartAsync` subscribes to all 7 Realtime events
- [ ] `StopAsync` unsubscribes all events and shuts down cleanly
- [ ] Audio deltas play through `VoiceOutputAgent`
- [ ] Transcripts route to UI and session history
- [ ] Interruption handling: speech_started → clear buffer → truncate
- [ ] No event handler leaks (unsubscribe matches subscribe)
- [ ] All tests pass

---

## Files Changed

| File | Action |
|------|--------|
| `Orchestration/AgentOrchestrator.cs` | REWRITE |
| `Tests/Orchestration/AgentOrchestratorTests.cs` | REWRITE |
| `IntegrationTests/Orchestration/FullPipelineTests.cs` | REWRITE |
