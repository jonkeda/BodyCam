# RealTests Proposal: Audio Integrity (RCA-005)

These tests measure audio delta characteristics at the API level to understand the buffering demands and validate that audio data is complete.

## Test File

`src/BodyCam.RealTests/Pipeline/AudioIntegrityTests.cs`

## Test Cases

### 1. AudioDeltas_TotalBytesMatchExpectedDuration

**Purpose:** Verify that the total audio bytes received match the expected duration based on the transcript length. Detects silent data loss.

```
Steps:
  1. Send text: "Count from one to ten slowly."
  2. Wait for response.done
  3. Sum all audio delta byte lengths
  4. Calculate duration: totalBytes / (24000 * 2 * 1) seconds
  5. Log: total bytes, calculated duration, delta count
  6. Assert: duration > 1 second (should be several seconds for counting)
  7. Assert: total bytes > 0
```

### 2. AudioDeltas_ChunkSizeDistribution

**Purpose:** Measure the size distribution of audio chunks to understand buffering requirements. Diagnostic test — always passes.

```
Steps:
  1. Send text: "Give me a detailed recipe for chocolate chip cookies."
  2. Wait for response.done
  3. For each audio chunk, record byte length
  4. Log:
     - Total chunks
     - Min / max / avg chunk size
     - Total bytes
     - Calculated audio duration
  5. Assert: true (diagnostic)
```

### 3. AudioDeltas_ArrivalBurstRate

**Purpose:** Measure how quickly audio deltas arrive relative to playback speed. This quantifies whether the buffer will overflow.

```
Steps:
  1. Send text: "Tell me a long story about a brave knight."
  2. Wait for response.done
  3. Using timestamped events, for all response.audio.delta events:
     - Calculate inter-arrival intervals
     - Calculate total data arrival rate (bytes/sec)
     - Compare to playback rate (24000 * 2 = 48000 bytes/sec)
  4. Log:
     - Arrival rate vs playback rate ratio
     - Peak burst rate (shortest 5 inter-arrival intervals)
     - Buffer duration needed to absorb the burst
  5. Assert: true (diagnostic — results inform buffer sizing)
```

### 4. AudioDeltas_NoDuplicateData

**Purpose:** Verify that no two consecutive audio deltas contain the same data (which would cause echo/stutter).

```
Steps:
  1. Send text: "Explain photosynthesis."
  2. Wait for response.done
  3. For each pair of consecutive audio chunks:
     - Assert: chunk[i] is not byte-equal to chunk[i-1]
```

### 5. AudioDeltas_AudioDoneMarksEnd

**Purpose:** Verify that `response.audio.done` arrives after all `response.audio.delta` events, and no more audio arrives after it.

```
Steps:
  1. Send text: "Say hello."
  2. Wait for response.done
  3. Find index of response.audio.done
  4. Assert: no response.audio.delta events exist after response.audio.done
  5. Assert: response.audio.done arrives before response.done
```

### 6. LongResponse_AudioBufferRequirement

**Purpose:** Calculate whether a 5-second buffer is sufficient for a long response. This directly tests the RCA hypothesis.

```
Steps:
  1. Send text: "Give me a very detailed explanation of how bread is made, 
     from growing wheat to the final loaf. Be thorough."
  2. Wait for response.done
  3. Using timestamped events, simulate buffer fill:
     - Walk through audio deltas in order
     - For each: add chunk size to simulated buffer level
     - Subtract playback drain (48000 bytes/sec × elapsed time since last delta)
     - Track peak buffer level
  4. Log:
     - Peak buffer level in bytes and seconds
     - Would a 5-second buffer overflow? (peak > 5 * 48000 = 240000 bytes)
     - Would a 10-second buffer overflow?
     - Would a 30-second buffer overflow?
  5. Assert: peak buffer level is logged (diagnostic — always passes)
```

This test directly proves or disproves the buffer overflow theory.

## Priority

Test 6 is the most important — it simulates the buffer behavior and proves whether a 5-second buffer is sufficient. Tests 3 and 5 validate arrival patterns. Tests 1, 2, 4 are supplementary integrity checks.
