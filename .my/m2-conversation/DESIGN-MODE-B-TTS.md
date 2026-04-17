# Design: Mode B Voice Pipeline — Is This the Right Approach?

## The Problem

Mode B (Separated pipeline) has fundamental issues with voice output:
- The Realtime API generates an **auto-response** (both text and audio) before we can cancel it
- Our `SendTextForTtsAsync` uses `response.create` which tells the API to **reason and generate a new response**, not just TTS the text
- Race conditions between auto-response and our TTS response cause overlapping audio
- The conversation context inside the Realtime session gets polluted with duplicate assistant messages

## Honest Assessment: Is Mode B Right for Voice Conversations?

**No.** For natural, responsive voice conversations, Mode A (Realtime) is significantly better.

### Latency comparison

| Step | Mode A (Realtime) | Mode B (Separated) |
|------|-------------------|---------------------|
| Speech → transcript | ~200ms (internal) | ~1-2s (wait for `InputTranscriptCompleted`) |
| Reasoning | ~0ms (bundled) | ~1-3s (Chat Completions streaming start) |
| Text → audio | ~0ms (bundled) | ~1-2s (TTS generation) |
| **Total** | **<1 second** | **3-7 seconds** |

That latency gap is **brutal** for voice conversations. A 3-7 second pause after speaking makes the interaction feel broken.

### What Mode A already supports

The Realtime API supports **function calling** within its session. This means:
- Vision context injection → define a `get_vision_context` tool, return camera descriptions
- MCP bridging → shim MCP tools as Realtime API functions
- Custom behavior → system prompt + instructions in `session.update`
- Conversation memory → the Realtime API maintains its own context

### When Mode B actually makes sense

1. **Different model for reasoning** — you want gpt-5.4's intelligence but the realtime model's voice. Worth it for complex analysis, coding help, etc.
2. **Long-form responses** — the Realtime API has token limits; Chat Completions doesn't
3. **Batch/offline processing** — not a real-time conversation, just process audio
4. **Cost optimization** — realtime audio costs more per token; route simple queries through a cheaper text model

**Conclusion:** Mode B should be a **specialty mode**, not the primary conversation path. Mode A should be default and recommended. Mode B is for users who explicitly want a different reasoning model and accept the latency trade-off.

## Design Options for Fixing Mode B TTS

### Option 1: Per-response modality override (Current approach)

**How:** Session config uses `modalities: ["text"]` to suppress auto-response audio. `SendTextForTtsAsync` sends a `response.create` with `modalities: ["text","audio"]` override.

**Pros:** No new dependencies. Uses existing Realtime WebSocket.
**Cons:** The API still generates a text auto-response (just no audio). It still **reasons** about our injected text — the `response.create` tells it to generate a new response, not just vocalize existing text. The auto-response text pollutes the conversation context.

**Verdict:** Partially fixes the audio problem but doesn't fix the reasoning/context pollution issue.

### Option 2: REST TTS API (Separate endpoint)

**How:** Add an `ITtsService` that calls the OpenAI TTS REST endpoint (`POST /v1/audio/speech`) with the ConversationAgent's reply text. Completely bypass the Realtime API for Mode B audio output.

```
POST https://api.openai.com/v1/audio/speech
{ "model": "tts-1", "voice": "marin", "input": "The capital of France is Paris." }
→ Returns PCM/MP3 audio bytes
```

**Pros:**
- Clean separation — Realtime does STT only, REST does TTS only
- No conversation context pollution
- No race conditions
- Simple implementation

**Cons:**
- Adds a second HTTP connection
- ~1-2s additional latency for TTS generation
- Voice may sound slightly different than Realtime's built-in TTS
- Need to handle streaming (chunked transfer) for long responses

**Verdict:** Cleanest architecture. Recommended for Mode B.

### Option 3: Disable auto-response with turn_detection: null

**How:** Set `turn_detection: null` in Mode B. The API does STT but never auto-responds. We manually commit audio buffers and trigger transcription.

**Pros:** Eliminates auto-response entirely.
**Cons:** Loses server-side VAD — we'd need to detect end-of-speech ourselves and call `CommitAudioBufferAsync`. More complex, more latency.

**Verdict:** Overkill. Solving a symptom, not the root cause.

### Option 4: Track response ownership

**How:** Add a `ResponseCreated` event. Track which responses are "ours" (from `SendTextForTtsAsync`) vs auto-responses. Cancel auto-responses immediately, allow ours through.

**Pros:** Keeps Realtime API for both STT and TTS.
**Cons:** Complex race condition handling. Auto-response still adds items to conversation context before we can cancel.

**Verdict:** Fragile. Not recommended.

## Recommended Design

### Keep Mode A as primary. Fix Mode B with Option 2 (REST TTS).

```
Mode A (Realtime — default, recommended):
  Mic → Realtime API → [reasoning + TTS] → Speaker
  Latency: <1s. Best for conversation.

Mode B (Separated — specialty):
  Mic → Realtime API (STT only, modalities: ["text"]) → transcript
  Transcript → ConversationAgent → Chat Completions → reply text
  Reply text → REST TTS API → audio → Speaker
  Latency: 3-7s. Use when you need a specific reasoning model.
```

### Implementation plan

1. **`ITtsService` interface** — `Task<byte[]> SynthesizeAsync(string text, CancellationToken ct)`
2. **`OpenAiTtsService` implementation** — calls `/v1/audio/speech`, returns PCM bytes
3. **Update orchestrator** — in `ProcessModeBAsync`, instead of `SendTextForTtsAsync`, call `ITtsService` and pipe bytes to `VoiceOutputAgent`
4. **Remove `SendTextForTtsAsync`** from `IRealtimeClient` — no longer needed
5. **Session config** — Mode B uses `modalities: ["text"]`, auto-response text is silently ignored

### Realtime API session in Mode B

```json
{
  "type": "session.update",
  "session": {
    "modalities": ["text"],
    "input_audio_transcription": { "model": "gpt-4o-mini-transcribe" },
    "turn_detection": { "type": "semantic_vad" }
  }
}
```

Auto-response text events are ignored (existing guards in orchestrator). No audio events fire because modalities is text-only.

### Alternative: Streaming TTS

For long responses, the REST TTS API returns audio all at once. For streaming, OpenAI offers a streaming mode. We should use it to reduce perceived latency:

```csharp
var response = await http.PostAsync("/v1/audio/speech", content);
var stream = await response.Content.ReadAsStreamAsync();
// Read chunks and pipe to AudioOutputService as they arrive
```

This lets the user hear the start of the response while the rest is still generating.
