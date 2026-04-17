# RealTests Proposal: Transcript Ordering (RCA-003)

These tests validate the fixes for the two transcript ordering bugs. They run against the live API and verify event ordering at the `RealtimeClient` level — the layer where the bugs originate.

## Test File

`src/BodyCam.RealTests/EventTracking/TranscriptOrderingTests.cs`

## Fixture Changes

Add to `RealtimeFixture`:
- **Timestamped event log** — record `(Type, Timestamp)` for every event so tests can assert relative timing
- **`WaitForResponseAndTranscriptAsync()`** — waits for both `response.done` AND `input_audio_transcription.completed` (with timeout), since the input transcript may arrive after response.done

No new fixture class needed — extend the existing one.

## Test Cases

### 1. InputTranscript_ArrivesAfterResponseStarts

**Purpose:** Prove that Issue 1 exists at the API level — the user's input transcript arrives *during or after* the AI response streaming.

```
Steps:
  1. Send audio of a short phrase (or use text + audio simulation)
  2. Wait for response.done
  3. Wait for input_audio_transcription.completed
  4. Compare timestamps:
     - First response.audio_transcript.delta timestamp
     - conversation.item.input_audio_transcription.completed timestamp
  5. Log both timestamps and the ordering
  6. Assert: input transcript arrived AFTER first audio_transcript.delta
     (This confirms the RCA — the API genuinely sends them out of order)
```

**Note:** This test documents the API behavior. It should pass regardless of our fix — we can't change when the API sends events.

### 2. MultiTurnConversation_EventSequenceIsConsistent

**Purpose:** Verify that across a multi-turn text conversation, the event sequence is consistent and no events are lost or duplicated.

```
Steps:
  1. Send text: "Say hello"
  2. Wait for response.done
  3. Record: event types + counts for turn 1
  4. Reset fixture
  5. Send text: "Now say goodbye"
  6. Wait for response.done
  7. Record: event types + counts for turn 2
  8. Assert: both turns have exactly 1 response.audio_transcript.done
  9. Assert: both turns have exactly 1 response.done
  10. Assert: neither turn has input_audio_transcription.completed (text input)
```

### 3. OutputTranscriptDeltas_ArriveBeforeCompleted

**Purpose:** Verify that all transcript deltas arrive before `response.audio_transcript.done` — ensures the streaming window is well-defined.

```
Steps:
  1. Send text: "Tell me a fun fact about penguins"
  2. Collect timestamped events
  3. Wait for response.done
  4. Assert: ALL response.audio_transcript.delta events have timestamps
           BEFORE response.audio_transcript.done
  5. Assert: response.audio_transcript.done timestamp
           BEFORE response.done timestamp
```

### 4. ResponseOutputItemAdded_ArrivesBeforeDeltas

**Purpose:** Verify that `response.output_item.added` (which provides the item ID for truncation) arrives before any audio/transcript deltas for that response.

```
Steps:
  1. Send text: "Say test"
  2. Wait for response.done
  3. Get index of response.output_item.added
  4. Get index of first response.audio_transcript.delta
  5. Get index of first response.audio.delta
  6. Assert: output_item.added comes before both
```

This validates that the truncation fix (subscribing to `OutputItemAdded`) will always set the item ID before audio starts playing.

### 5. ConcurrentInputAndOutput_EventsDoNotInterleaveCorruptly

**Purpose:** Simulate the scenario from the screenshot — send a second text input while the AI is still responding, verify events don't corrupt.

```
Steps:
  1. Send text: "Tell me a long story about a dragon"
  2. Wait for first audio delta (response is streaming)
  3. Immediately send second text: "Actually, just say ok"
  4. Wait for all responses to complete (may get 1 cancelled + 1 completed,
     or 2 completed)
  5. Assert: each response.done has a matching response.created
  6. Assert: no duplicate response.audio_transcript.done for the same response_id
  7. Log the full event sequence for analysis
```

### 6. InputTranscriptCompleted_NeverDuplicatedInTextMode

**Purpose:** Confirm that text-only input never generates `input_audio_transcription.completed` — this is a control test establishing that the duplication bug only applies to audio input.

```
Steps:
  1. Send text: "Hello"
  2. Wait for response.done
  3. Wait an extra 2 seconds
  4. Assert: zero input_audio_transcription.completed events
  5. Assert: exactly 1 response.audio_transcript.done
```

### 7. OutputTranscriptCompleted_MatchesConcatenatedDeltas

**Purpose:** Verify that the final completed transcript exactly matches the concatenation of all deltas — ensuring no data loss during streaming.

```
Steps:
  1. Send text: "What is the capital of Japan?"
  2. Collect all response.audio_transcript.delta values
  3. Wait for response.audio_transcript.done
  4. Concatenate all delta values
  5. Assert: concatenated deltas == completed transcript text
```

This already exists as `SendTextInput_TranscriptDeltasAggregateToCompleted` in `TextRoundTripTests` but is included here for the ordering-specific test suite.

## Fixture Enhancement

```csharp
// Add to RealtimeFixture:
private readonly List<(string Type, DateTimeOffset Timestamp)> _timestampedEvents = [];

public IReadOnlyList<(string Type, DateTimeOffset Timestamp)> TimestampedEvents => _timestampedEvents;

// In OnRawMessage:
_timestampedEvents.Add((type, DateTimeOffset.UtcNow));

// In Reset():
_timestampedEvents.Clear();

// New helper:
public Task WaitForInputTranscriptOrTimeoutAsync(TimeSpan timeout)
{
    // Returns without throwing if timeout expires (input transcript is optional)
    return Task.WhenAny(_inputTranscriptTcs.Task, Task.Delay(timeout));
}
```

## Priority

Tests 1, 3, 4, 5 are the most valuable — they directly validate the preconditions that cause the bugs and confirm the fixes work. Tests 2, 6, 7 are supplementary.
