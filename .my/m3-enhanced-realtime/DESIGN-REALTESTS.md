# Design: RealTests — End-to-End Voice Pipeline Testing

## Problem

The BodyCam voice pipeline has issues that only manifest with a live Realtime API connection:

1. **Duplicated transcriptions** — `InputTranscriptCompleted` fires more than once for the same utterance
2. **AudioPlaybackTracker never gets an ItemId** — `VoiceOutputAgent.SetCurrentItem()` is never called, so `OnSpeechStarted` truncation logic is dead code
3. **Tool definition JsonDocument leak** — `GetToolDefinitions()` calls `JsonDocument.Parse()` on every `UpdateSessionAsync` without disposing
4. **No response.output_item.added handling** — The `DispatchMessage` switch doesn't handle `response.output_item.added` which provides the `item_id` needed for truncation tracking
5. **Unknown: function call round-trip** — Never been tested end-to-end
6. **Unknown: interruption behavior** — Truncation may not work because ItemId tracking is broken

These can only be caught by tests that connect to the real API and run actual voice conversations.

## Approach

Add real integration tests in `src/BodyCam.RealTests/` that:

1. Connect to the Realtime API over WebSocket
2. Send pre-recorded PCM audio (a known phrase)
3. Collect all server events
4. Assert on the event sequence, transcriptions, and audio output

### Test Audio Strategy

Use **TTS-generated PCM files** as test input. These are deterministic, known-length utterances that produce reliable transcriptions. Store them as embedded resources or generate them in a one-time test fixture.

Alternatively, use **text input** via `conversation.item.create` with `input_text` content — this bypasses audio entirely but tests the reasoning/response pipeline. Faster and more deterministic for non-audio tests.

### Provider Support

Tests should work with both OpenAI direct and Azure OpenAI. Read credentials from `.env` at the repo root (same pattern as existing `AzureConnectionTests`).

## Test Architecture

```
src/BodyCam.RealTests/
├── Fixtures/
│   └── RealtimeFixture.cs        ← Shared WebSocket connection + event collector
├── Pipeline/
│   ├── TextRoundTripTests.cs     ← Send text input, verify response events
│   ├── AudioRoundTripTests.cs    ← Send PCM audio, verify transcription + response
│   ├── FunctionCallTests.cs      ← Trigger tool calls, verify round-trip
│   └── InterruptionTests.cs      ← Send overlapping audio, verify truncation
├── EventTracking/
│   └── EventSequenceTests.cs     ← Verify event ordering and no duplicates
├── Resources/
│   └── hello-bodycam.pcm         ← Pre-recorded test audio (24kHz 16-bit mono)
└── Helpers/
    └── PcmGenerator.cs           ← Generate silence/tone for test audio
```

## RealtimeFixture Design

A shared test fixture that manages a live WebSocket connection and collects all events:

```csharp
public class RealtimeFixture : IAsyncLifetime
{
    private readonly RealtimeClient _client;
    private readonly List<(string Type, string Json)> _events = [];
    private readonly List<string> _inputTranscripts = [];
    private readonly List<string> _outputTranscripts = [];
    private readonly List<byte[]> _audioChunks = [];
    private readonly List<FunctionCallInfo> _functionCalls = [];
    private readonly List<string> _errors = [];
    private readonly TaskCompletionSource _responseComplete = new();

    public IReadOnlyList<(string Type, string Json)> Events => _events;
    public IReadOnlyList<string> InputTranscripts => _inputTranscripts;
    public IReadOnlyList<string> OutputTranscripts => _outputTranscripts;
    public IReadOnlyList<FunctionCallInfo> FunctionCalls => _functionCalls;
    public IReadOnlyList<string> Errors => _errors;
    
    // Wait for a response to complete (response.done event)
    public Task WaitForResponseAsync(TimeSpan? timeout = null);
    
    // Wait for input transcription
    public Task WaitForInputTranscriptAsync(TimeSpan? timeout = null);
    
    // Send text as user input (bypasses audio, fast and deterministic)
    public Task SendTextInputAsync(string text);
    
    // Send PCM audio chunks
    public Task SendAudioAsync(byte[] pcm16Data, int chunkSize = 4800);
    
    // Reset collected events between tests
    public void Reset();
}
```

The fixture hooks into `DispatchMessage` or subscribes to all `IRealtimeClient` events to capture the full event log. This allows tests to inspect the raw event sequence for ordering issues, duplicates, etc.

### Key Design Decision: Hook DispatchMessage Directly

Rather than only subscribing to typed events, the fixture should also capture the **raw JSON** of every server message. This enables:
- Detecting events we don't handle (e.g., `response.output_item.added`)
- Verifying event ordering
- Diagnosing issues the current code silently drops

To do this, add an `internal` event to `RealtimeClient`:

```csharp
// In RealtimeClient.cs:
internal event EventHandler<string>? RawMessageReceived;

// In DispatchMessage, at the top:
RawMessageReceived?.Invoke(this, json);
```

Tests in `BodyCam.RealTests` can access this via `[InternalsVisibleTo]`.

## Test Cases

### 1. TextRoundTripTests

Tests that send text input (no audio) and verify the response pipeline.

```
Test: SendTextInput_ReceivesAudioResponse
  1. Connect to Realtime API
  2. Send conversation.item.create with input_text "What is 2 plus 2?"
  3. Send response.create
  4. Wait for response.done
  5. Assert: OutputTranscriptCompleted fired exactly once
  6. Assert: AudioDelta fired at least once (got audio back)
  7. Assert: response.done status == "completed"
  
Test: SendTextInput_ReceivesTextTranscript
  1. Send text input "Say the word hello"
  2. Wait for response.done
  3. Assert: OutputTranscriptCompleted contains "hello" (case-insensitive)

Test: SendTextInput_NoInputTranscript
  1. Send text input "Hi"
  2. Wait for response.done
  3. Assert: InputTranscriptCompleted was NOT fired (text input, not audio)
```

### 2. AudioRoundTripTests

Tests that send PCM audio and verify both transcription and response.

```
Test: SendAudio_ProducesInputTranscript
  1. Send PCM audio of "Hello BodyCam" (pre-recorded or silence + speech)
  2. Wait for InputTranscriptCompleted
  3. Assert: transcript contains "hello" (case-insensitive)
  4. Assert: InputTranscriptCompleted fired EXACTLY ONCE (catches duplicate bug)

Test: SendAudio_ProducesResponseAudio
  1. Send PCM audio of a question
  2. Wait for response.done
  3. Assert: AudioDelta chunks received
  4. Assert: OutputTranscriptCompleted fired exactly once

Test: SendAudio_EventOrdering
  1. Send PCM audio
  2. Collect all raw events
  3. Assert event order:
     - speech_started before speech_stopped
     - speech_stopped before input_audio_transcription.completed
     - response.created before response.audio.delta
     - response.audio_transcript.done before response.done
```

### 3. EventSequenceTests — Duplicate Detection

These tests specifically target the duplicate transcription bug.

```
Test: InputTranscription_NoDuplicates
  1. Send audio of "What time is it?"
  2. Wait for response.done (full cycle complete)
  3. Count InputTranscriptCompleted fires
  4. Assert: count == 1

Test: OutputTranscription_NoDuplicates
  1. Send text input "Tell me a joke"
  2. Wait for response.done
  3. Count OutputTranscriptCompleted fires
  4. Assert: count == 1

Test: ResponseDone_FiresOnce
  1. Send text input
  2. Wait 5 seconds after first response.done
  3. Assert: response.done fired exactly once

Test: RawEvents_NoUnexpectedDuplicates
  1. Send text input
  2. Wait for response.done
  3. Group raw events by type
  4. Assert: conversation.item.input_audio_transcription.completed appears at most once
  5. Assert: response.audio_transcript.done appears at most once per response
```

### 4. FunctionCallTests

```
Test: AskAboutSurroundings_TriggersFunctionCall
  1. Send text input "What do you see around me?"
  2. Wait for response.done
  3. Assert: FunctionCallReceived fired with name == "describe_scene"
  4. Send function_call_output with mock description
  5. Wait for second response.done
  6. Assert: OutputTranscriptCompleted contains reference to the description

Test: FunctionCallOutput_CompletesRoundTrip
  1. Send text "Analyze why the sky is blue in depth"
  2. Wait for function call
  3. Assert: name == "deep_analysis"
  4. Send function_call_output
  5. Assert: model speaks the analysis result
```

### 5. InterruptionTests

```
Test: SpeechStarted_DuringResponse_CancelsAudio
  1. Send text input that produces a long response
  2. After receiving some AudioDelta chunks, send new audio input
  3. Assert: response.done has status == "cancelled" or new response starts
  
Test: ResponseOutputItemAdded_ProvidesItemId
  1. Send text input
  2. Collect raw events
  3. Assert: response.output_item.added event exists with an item ID
  4. Assert: This is the same ID in response.done output[0].id
```

## Known Bugs to Diagnose

### Bug 1: Duplicate InputTranscriptCompleted

**Hypothesis:** The Realtime API fires `conversation.item.input_audio_transcription.completed` once per committed audio buffer. If VAD commits audio and we also get delta events, or if the response includes an input transcript in `response.done`, we might count it twice.

**Diagnostic test:** `InputTranscription_NoDuplicates` — send a single utterance, count events.

**Possible root cause:** The `response.done` event may also contain an input transcript (in `response.output[].content[].transcript`), and if `ParseResponseDone` extracts it AND we also get the standalone transcription event, we see duplicates.

### Bug 2: ItemId Never Set for Truncation

**Diagnosis:** The `VoiceOutputAgent.SetCurrentItem(itemId)` is never called. Looking at the code:
- `OnResponseDone` resets the tracker but doesn't set the item ID
- `OnAudioDelta` plays audio but doesn't set the item ID
- No handler for `response.output_item.added` which provides the item ID

**Fix needed:** Handle `response.output_item.added` or `response.created` to capture the item ID, then call `_voiceOut.SetCurrentItem(itemId)`.

### Bug 3: JsonDocument Leak in GetToolDefinitions

`JsonDocument.Parse()` returns an `IDisposable` that's never disposed. Called on every `UpdateSessionAsync`. Should cache as `static readonly`.

## Implementation Steps

### Step 1: Add InternalsVisibleTo for raw event access
Add to `src/BodyCam/BodyCam.csproj`:
```xml
<InternalsVisibleTo Include="BodyCam.RealTests" />
```

Add `RawMessageReceived` internal event to `RealtimeClient`.

### Step 2: Create RealtimeFixture
Shared fixture that connects, collects events, and provides helper methods.

### Step 3: Create TextRoundTripTests
Start with text-only tests — fastest, most deterministic.

### Step 4: Create EventSequenceTests
Focus on duplicate detection and event ordering.

### Step 5: Create AudioRoundTripTests
Generate or embed a short PCM file, send it, verify transcription.

### Step 6: Create FunctionCallTests
Trigger function calls via text prompts, verify the round-trip.

### Step 7: Fix discovered bugs
Based on test results, fix:
- Duplicate transcription handling
- ItemId tracking (add `response.output_item.added` handler)
- JsonDocument caching
- Any other issues found

## Test Execution

```powershell
# Run real tests (requires .env with credentials)
dotnet test src/BodyCam.RealTests/BodyCam.RealTests.csproj -v n

# Skip in CI — these hit live APIs
# Use [Trait("Category", "RealAPI")] and filter in CI
```

Tests should use generous timeouts (10–30 seconds) since the Realtime API has variable latency.
