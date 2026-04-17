# RCA: Mode B — Two Voices Speaking + Wrong Transcription

## Symptom

In Mode B (Separated), two things go wrong:

1. **Two voices talk back** — the user hears both the Realtime API's auto-response AND the ConversationAgent's TTS response
2. **Transcription is wrong** — the transcript displayed in the UI doesn't match what the AI actually said

## Root Cause

**The Realtime API generates an auto-response before our `CancelResponseAsync` arrives.**

### Timing race

When the user finishes speaking, the Realtime API fires events in this order:

```
1. input_audio_buffer.speech_stopped
2. conversation.item.input_audio_transcription.completed  ← we get the transcript here
3. response.created                                        ← Realtime ALREADY started responding
4. response.audio.delta                                    ← audio is flowing
5. response.audio_transcript.delta                         ← transcript of its own response
```

Our `CancelResponseAsync()` is called in `OnInputTranscriptCompleted` (step 2), but by then the Realtime API has already started generating its response (step 3). The cancel races against audio deltas that are already in-flight or buffered in the WebSocket.

### Two voices

The `_modeBProcessing` flag blocks `OnAudioDelta` from playing auto-response audio. But:

- Audio deltas from the auto-response can arrive **before** `_modeBProcessing = true` is set (the event handler is `async void`, so there's no ordering guarantee vs. the Realtime receive loop)
- After `_modeBProcessing = false` is set (when `SendTextForTtsAsync` is called), the auto-response may STILL be sending audio deltas if `CancelResponseAsync` didn't fully stop it
- `SendTextForTtsAsync` calls `CreateResponseAsync`, which triggers a NEW response — but the old auto-response may not be fully cancelled, so both overlap

### Wrong transcription

The Realtime API fires `response.audio_transcript.delta` and `response.audio_transcript.done` for its auto-response. Even though `OnOutputTranscriptDelta` and `OnOutputTranscriptCompleted` have Mode B guards that `return`, the **auto-response is also feeding text into the Realtime API's conversation context**. When `SendTextForTtsAsync` creates a new assistant item and triggers `CreateResponseAsync`, the Realtime API sees our injected text PLUS its own prior response in context, leading to confused or duplicated output.

Additionally, `SendTextForTtsAsync` calls `CreateResponseAsync` which tells the Realtime API to generate a *new* response (reasoning + audio), not just TTS the text we gave it. The API treats our `conversation.item.create` as context and generates its own reply on top.

## Impact

- User hears overlapping audio from two sources
- Transcript shows the Realtime API's auto-response text, not the ConversationAgent's reply
- Conversation context in the Realtime session becomes polluted with duplicate/conflicting assistant messages

## Fix Options

### Option A: Disable auto-response via session config (Recommended)

Set the Realtime session to **not auto-respond** in Mode B. The OpenAI Realtime API supports a response configuration that can disable auto-triggering:

```json
{
  "type": "session.update",
  "session": {
    "turn_detection": null
  }
}
```

With `turn_detection: null`, the Realtime API still does STT but does NOT auto-generate a response. We manually control when responses happen via `CreateResponseAsync`. This eliminates the race entirely.

**Trade-off:** We lose automatic voice activity detection. We'd need to either:
- Use client-side VAD to detect end-of-speech and call `CommitAudioBufferAsync` manually
- Or keep `turn_detection` but add a `response` config that suppresses auto-response

### Option B: Use REST TTS instead of Realtime for Mode B audio out

Instead of routing reply text back through the Realtime WebSocket (which conflates our TTS with the session's conversation context), use the separate OpenAI TTS REST API (`/v1/audio/speech`) to generate audio:

```
POST https://api.openai.com/v1/audio/speech
{ "model": "tts-1", "voice": "marin", "input": "reply text" }
```

This completely decouples the ConversationAgent's audio output from the Realtime session. The Realtime API only does STT, never generates audio.

**Trade-off:** Adds a separate HTTP call for TTS. Slightly higher latency. Different voice model than Realtime's built-in TTS.

### Option C: Fix the race with response.created detection

Subscribe to the `response.created` event. When it fires in Mode B and we didn't initiate it (track a `_ttsResponseId` from our `SendTextForTtsAsync`), immediately cancel it. This is brittle but doesn't require architectural changes.

## Recommended Fix

**Option A** is cleanest. In Mode B's `UpdateSessionAsync`:

```csharp
TurnDetection = _settings.Mode == ConversationMode.Separated
    ? null  // No auto-response; we control response timing
    : new TurnDetectionConfig { Type = _settings.TurnDetection }
```

Then in Mode B, after `InputTranscriptCompleted`, the Realtime API won't auto-respond. We call `ConversationAgent`, get the reply, and use `SendTextForTtsAsync` to generate audio — no race, no overlap.

The downside (losing server-side VAD) can be mitigated by committing the audio buffer manually when `input_audio_buffer.speech_stopped` fires (which still fires even without turn detection — it's a separate signal).

## Affected Files

| File | Issue |
|------|-------|
| `AgentOrchestrator.cs` | Race between `CancelResponseAsync` and auto-response audio deltas |
| `RealtimeClient.cs` | `UpdateSessionAsync` doesn't disable auto-response for Mode B |
| `RealtimeClient.cs` | `SendTextForTtsAsync` uses `CreateResponseAsync` which triggers reasoning, not just TTS |
