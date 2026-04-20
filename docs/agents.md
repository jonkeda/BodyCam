# Agents

Four agents handle the real-time AI pipeline. They are plain C# classes (not MAF agents) registered as singletons via DI.

## VoiceInputAgent

**File:** `Agents/VoiceInputAgent.cs`
**Dependencies:** `IAudioInputService`, `IRealtimeClient`

Bridges the platform microphone to the Realtime API. Subscribes to `AudioChunkAvailable` events from the audio input service and forwards each PCM chunk to the session via the audio sink callback set by the orchestrator.

- `StartAsync(ct)` — subscribes to audio chunks
- `StopAsync()` — unsubscribes

Exceptions in the audio callback are caught and swallowed to avoid blocking the audio thread.

## VoiceOutputAgent

**File:** `Agents/VoiceOutputAgent.cs`
**Dependencies:** `IAudioOutputService`

Plays PCM audio deltas received from the Realtime API. Tracks playback position for interruption support.

- `StartAsync(ct)` — starts the audio output service
- `StopAsync()` — stops output, resets tracker
- `PlayAudioDeltaAsync(pcmData, ct)` — plays a chunk and updates the `AudioPlaybackTracker`
- `HandleInterruption()` — clears the output buffer when the user starts speaking
- `SetCurrentItem(itemId)` — tracks which response item is playing (for truncation)

The `AudioPlaybackTracker` computes `PlayedMs` from bytes played, sample rate, and bit depth — used to tell the API where to truncate on interruption.

## ConversationAgent

**File:** `Agents/ConversationAgent.cs`
**Dependencies:** `IChatClient`, `AppSettings`

Used for deep analysis tasks that need a reasoning model (Chat Completions) rather than the Realtime API's built-in model. Called by `DeepAnalysisTool`.

- `AnalyzeAsync(query, context?, ct)` — sends query + optional context to Chat Completions, returns the text response

## VisionAgent

**File:** `Agents/VisionAgent.cs`
**Dependencies:** `IChatClient`, `AppSettings`

Sends JPEG camera frames to GPT Vision (via Chat Completions with image content) and returns text descriptions. Used by vision tools (`describe_scene`, `find_object`, `read_text`).

- `DescribeFrameAsync(jpegFrame, userPrompt?, ct)` — sends frame as base64 image to the vision model
- `LastDescription` — caches the most recent description
- `LastCaptureTime` — enforces a 5-second cooldown between captures

## Data Flow

```
Microphone
    │ PCM chunks (24kHz, 16-bit)
    ▼
VoiceInputAgent ──► session.SendAsync() ──► OpenAI Realtime API
                                              │
                                    ┌─────────┼─────────┐
                                    ▼         ▼         ▼
                              audio delta  transcript  function_call
                                    │         │         │
                              VoiceOutput  MainVM    ToolDispatcher
                              Agent        (UI)      ──► VisionAgent
                                    │                 ──► ConversationAgent
                                    ▼
                                 Speakers
```
