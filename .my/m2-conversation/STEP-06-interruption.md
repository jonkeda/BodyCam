# M2 Implementation — Step 6: Interruption Handling

**Depends on:** Step 4 (Mode B orchestrator flow working)
**Produces:** User can interrupt AI mid-response in Mode B — cancels Chat API call, stops TTS, processes new input

---

## Why This Step?

In Mode A, interruption is already handled: Realtime API fires `input_audio_buffer.speech_started`, the orchestrator truncates audio and resets the tracker. In Mode B, we have **two things to cancel**:

1. The in-flight Chat Completions streaming call (ConversationAgent)
2. The TTS audio playback (VoiceOutputAgent, same as Mode A)

---

## Tasks

### 6.1 — Per-turn CancellationTokenSource in orchestrator

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs` — MODIFY

This was sketched in Step 4 with `_turnCts`. Ensure:

```csharp
private CancellationTokenSource? _turnCts;
```

- `ProcessModeBAsync` creates a new `_turnCts` at the start of each turn
- Previous `_turnCts` is cancelled before creating a new one
- `OnSpeechStarted` cancels `_turnCts` in Mode B

### 6.2 — Wire interruption into `OnSpeechStarted` for Mode B

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs` — MODIFY

```csharp
private async void OnSpeechStarted(object? sender, EventArgs e)
{
    // Mode B: cancel in-flight Chat API call + stop TTS
    if (_settings.Mode == ConversationMode.Separated)
    {
        _turnCts?.Cancel();
        _voiceOut.HandleInterruption();
        _voiceOut.ResetTracker();
        DebugLog?.Invoke(this, "Mode B: Interrupted — cancelled Chat API + cleared audio.");
        return;
    }

    // Mode A: existing truncation logic (unchanged)
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
```

### 6.3 — Handle partial reply in SessionContext

When the Chat API call is cancelled mid-stream, the `ConversationAgent.ProcessTranscriptAsync` enumeration stops. The `replyBuilder` in `ProcessModeBAsync` contains a partial reply. We should **not** add a partial assistant message to the session — it would corrupt history.

This is already handled by the ConversationAgent's design: the `AddAssistantMessage` call happens **after** the `await foreach` loop completes. If cancelled, the loop throws `OperationCanceledException` and the message is never added.

However, the **user message was already added** at the start of `ProcessTranscriptAsync`. On interruption, we should remove it so the next turn re-processes cleanly:

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs` — MODIFY

In `ProcessModeBAsync`, catch cancellation and remove the dangling user message:

```csharp
catch (OperationCanceledException)
{
    // Remove the user message that was added at the start of the cancelled turn —
    // it will be superseded by the new input
    if (Session.Messages.Count > 0 && Session.Messages[^1].Role == "user")
        Session.Messages.RemoveAt(Session.Messages.Count - 1);

    DebugLog?.Invoke(this, "Mode B: Turn cancelled (interruption).");
}
```

**Wait — actually, we should keep the user message.** The user did say something. The interruption means they want to say something *else* on top of it. Removing the first utterance loses context. 

**Revised approach:** Keep the user message. The partial assistant reply is already not added (enumeration was cancelled). The next turn starts clean with the user's new input.

```csharp
catch (OperationCanceledException)
{
    DebugLog?.Invoke(this, "Mode B: Turn cancelled (interruption).");
    // User message is kept in history — user did say it.
    // Partial assistant reply was never added (streaming cancelled before completion).
}
```

### 6.4 — Cancel TTS-via-Realtime on interruption

When the user interrupts during TTS playback in Mode B, the Realtime API may still be generating audio from the `SendTextForTtsAsync` call. Cancel that:

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs` — MODIFY

In the Mode B branch of `OnSpeechStarted`:

```csharp
// Also cancel any in-flight Realtime TTS response
try { await _realtime.CancelResponseAsync(); }
catch (Exception ex) { DebugLog?.Invoke(this, $"TTS cancel error: {ex.Message}"); }
```

### 6.5 — Cleanup `_turnCts` on StopAsync

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs` — MODIFY

In `StopAsync`:

```csharp
_turnCts?.Cancel();
_turnCts?.Dispose();
_turnCts = null;
```

---

## Verification

- [ ] Mode A interruption works exactly as before (no regression)
- [ ] Mode B: user speaks while Chat API is streaming → streaming stops, new input processed
- [ ] Mode B: user speaks while TTS is playing → audio stops, new input processed
- [ ] Mode B: partial assistant reply is NOT added to SessionContext
- [ ] Mode B: user message from interrupted turn IS kept in history
- [ ] Mode B: rapid interruptions don't crash or deadlock
- [ ] StopAsync cleans up `_turnCts`

---

## Risks

| Risk | Mitigation |
|------|-----------|
| Race between `_turnCts.Cancel()` and new `ProcessModeBAsync` | Sequential: cancel old CTS first, then create new one |
| `CancelResponseAsync` fails if no active response | Catch and log — non-fatal |
| User speaks multiple times rapidly | Each invocation cancels the previous; only the last one completes |
