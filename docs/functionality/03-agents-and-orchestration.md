# 03 — Agents and Orchestration

## Agent Architecture

Four agents, each with a single responsibility. The `AgentOrchestrator` coordinates them.

```
                    AgentOrchestrator
                   /    |       |      \
          VoiceInput  VoiceOutput  Vision  Conversation
              ↓          ↑          ↑         ↑
         mic PCM    speaker PCM  camera   chat completions
              ↓          ↑          ↑         ↑
         ─────── Realtime WebSocket ──────────
                   (OpenAI / Azure)
```

## VoiceInputAgent

**File:** `Agents/VoiceInputAgent.cs`
**Role:** Pipes raw PCM audio from the microphone to the Realtime API.

**How it works:**
1. `StartAsync()` subscribes to `IAudioInputService.AudioChunkAvailable`
2. Each chunk goes through AEC processing (if enabled)
3. Forwarded to `_audioSink` callback (set by orchestrator)
4. The sink wraps the PCM in `InputAudioBufferAppendRealtimeClientMessage` and sends via `session.SendAsync()`

**Key methods:**
- `SetAudioSink(Func<byte[], CancellationToken, Task>?)` — injected by orchestrator after session creation
- `SetConnected(bool)` — gates whether chunks are forwarded (prevents sending before session is ready)
- `StartAsync() / StopAsync()` — subscribe/unsubscribe from audio events

**Diagnostics:** Logs connect/disconnect, start/stop, chunk count, and send failures.

## VoiceOutputAgent

**File:** `Agents/VoiceOutputAgent.cs`
**Role:** Plays Realtime API audio responses through speakers. Tracks playback position for interruption.

**How it works:**
1. Receives base64-decoded PCM from `RunMessageLoopAsync` (OutputAudioDelta messages)
2. Calls `IAudioOutputService.PlayChunkAsync()` to queue audio
3. Tracks bytes played via `AudioPlaybackTracker`

**Key methods:**
- `PlayAudioDeltaAsync(pcmData, ct)` — play a chunk, feed to AEC as render reference
- `HandleInterruption()` — user started speaking, clear output buffer
- `SetCurrentItem(itemId)` — track which response item is playing (needed for truncation)
- `ResetTracker()` — reset after response completes

**AudioPlaybackTracker:** Tracks `CurrentItemId`, `BytesPlayed`, and computes `PlayedMs` for truncation.

## VisionAgent

**File:** `Agents/VisionAgent.cs`
**Role:** Sends camera JPEG frames to GPT Vision and returns descriptions.

**How it works:**
1. Receives JPEG bytes + optional user prompt
2. Sends to Chat Completions (vision-capable model) as image content
3. Returns 1-3 sentence description

**Key method:**
- `DescribeFrameAsync(jpegFrame, userPrompt?, ct)` → string description

**Caching:**
- `LastDescription` — most recent result
- `LastCaptureTime` — enforces 5-second cooldown between calls
- Tools check cooldown before calling

## ConversationAgent

**File:** `Agents/ConversationAgent.cs`
**Role:** Deep reasoning via Chat Completions for queries needing multi-step analysis.

**Key method:**
- `AnalyzeAsync(query, context?, ct)` → string response

Used by `DeepAnalysisTool` when the Realtime API's built-in reasoning isn't sufficient.

---

## AgentOrchestrator

**File:** `Orchestration/AgentOrchestrator.cs`
**Role:** The brain. Manages the Realtime WebSocket session, coordinates all agents, handles wake words, reconnection, and tool dispatch.

### Lifecycle

```
StartAsync()
  1. Create CancellationTokenSource
  2. Refresh AppSettings from ISettingsService
  3. Build RealtimeSessionOptions (voice, audio format, VAD, transcription)
  4. Create session: _realtimeFactory.CreateSessionAsync(options)
  5. Launch RunMessageLoopAsync() on background thread
  6. Wire VoiceInputAgent audio sink → session.SendAsync()
  7. Initialize AEC
  8. Start VoiceOutputAgent
  9. Start VoiceInputAgent

StopAsync()
  1. Cancel CTS
  2. Await message loop completion
  3. Disconnect VoiceInputAgent (SetConnected(false), clear sink)
  4. Stop VoiceInputAgent + VoiceOutputAgent
  5. Dispose session
  6. Reset state
```

### Message Loop

`RunMessageLoopAsync()` iterates `session.GetStreamingResponseAsync(ct)` and dispatches each message type:

| Message Type | Action |
|-------------|--------|
| `OutputAudioDelta` | Decode base64 → `VoiceOutputAgent.PlayAudioDeltaAsync()` |
| `OutputTextDelta` | Fire `TranscriptDelta` event (AI streaming text) |
| `OutputTextDone` / `OutputAudioTranscriptionDone` | Fire `TranscriptCompleted` ("AI:{text}") |
| `InputAudioTranscriptionCompleted` | Fire `TranscriptCompleted` ("You:{text}") |
| `ResponseOutputItemAdded` | Track item ID for interruption |
| `ResponseDone` | Reset playback tracker |
| `Error` | Log error |
| `input_audio_buffer.speech_started` | Handle interruption (truncate AI response) |

### Speech Interruption

When the user starts speaking while the AI is responding:

1. `SpeechStarted` message arrives
2. `VoiceOutputAgent.HandleInterruption()` clears audio buffer
3. Send `conversation.item.truncate` to API with `item_id` and `audio_end_ms` (playback position)
4. API stops generating, user's new input takes over

### Reconnection

If the session drops unexpectedly (message loop exits without cancellation):

1. Log warning
2. Exponential backoff: 1s → 2s → 4s → 8s → 16s
3. Up to 5 retries
4. Each retry: dispose old session, create new one, re-wire audio sink
5. If all retries fail: call `StopAsync()`

### Wake Word Integration

- `StartListeningAsync()` — hooks `IWakeWordService.WakeWordDetected`
- Wake word actions:
  - `StartSession` → transition mic to active + `StartAsync()`
  - `GoToSleep` → `StopAsync()` + transition mic to wake word
  - `InvokeTool` → start session if needed, execute named tool via `ToolDispatcher`

### ToolContext

Created by `CreateToolContext()` for tool execution:

```csharp
ToolContext {
    CaptureFrame = FrameCaptureFunc ?? _cameraManager.CaptureFrameAsync,
    Session = Session,           // SessionContext with conversation history
    Log = msg => DebugLog event  // fires to MainViewModel debug overlay
}
```

`FrameCaptureFunc` is set by `MainViewModel` to `CaptureFrameFromCameraViewAsync` — captures JPEG from the MAUI CameraView control.

### SendTextInputAsync

Injects text into the active Realtime session (used by quick action buttons when session is running):

```csharp
var item = new RealtimeConversationItem([new TextContent(text)], null, ChatRole.User);
await _session.SendAsync(new CreateConversationItemRealtimeClientMessage(item), ct);
await _session.SendAsync(new CreateResponseRealtimeClientMessage(), ct);
```

This makes the AI "hear" the text as if the user spoke it, and triggers a spoken response.

### Function Invocation

The Realtime API sends function calls as part of response output items. Since MAF does not have a `FunctionInvokingRealtimeClient` for realtime, tool dispatch is manual:

1. `ResponseDone` message arrives in the message loop
2. Orchestrator accesses `msg.RawRepresentation` to get the SDK's `RealtimeServerUpdateResponseDone`
3. Iterates `response.OutputItems` looking for `RealtimeFunctionCallItem`
4. For each function call, resolves the tool from the registered `AIFunction` list and invokes it
5. Sends the result back via `RealtimeItem.CreateFunctionCallOutputItem` through the SDK session
6. Triggers a new response with `CreateResponseRealtimeClientMessage`

## SessionContext

**File:** `Models/SessionContext.cs`
**Role:** Shared conversation state across agents.

| Property | Purpose |
|----------|---------|
| `SessionId` | GUID for current session |
| `Messages` | Conversation history (role + content + timestamp) |
| `IsActive` | Session running flag |
| `MaxHistoryChars` | Token budget (~16K chars ≈ 4K tokens) |
| `LastVisionDescription` | Injected as system context for vision-aware responses |

`GetTrimmedHistory()` returns messages that fit within the token budget, always keeping the system prompt and most recent messages.

## Events Flow

```
Realtime API WebSocket
  → RunMessageLoopAsync dispatches
    → TranscriptDelta        → MainViewModel appends to _currentAiEntry.Text
    → TranscriptCompleted    → MainViewModel creates/finalizes transcript entries
    → DebugLog               → MainViewModel.DebugLog string
    → (audio)                → VoiceOutputAgent → speakers
```
