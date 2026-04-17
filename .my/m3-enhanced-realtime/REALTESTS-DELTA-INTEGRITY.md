# RealTests Proposal: Garbled Streaming Text (RCA-004)

These tests verify that transcript delta data is correct at the API level, confirming the garbling is a rendering issue, not a data issue.

## Test File

`src/BodyCam.RealTests/EventTracking/DeltaIntegrityTests.cs`

## Test Cases

### 1. Deltas_ContainNoControlCharacters

**Purpose:** Verify that transcript deltas never contain control characters, null bytes, or other non-printable characters that could cause garbled display.

```
Steps:
  1. Send text: "Give me a recipe for chocolate chip cookies with all ingredients and steps."
  2. Wait for response.done
  3. For each collected delta string:
     - Assert: no char < 0x20 (except \n, \r, \t)
     - Assert: no null chars
  4. Assert: all deltas are non-empty
```

### 2. Deltas_ConcatenateToCompletedTranscript_LongResponse

**Purpose:** Verify data integrity specifically for longer responses (where the garbling was observed). The existing test uses short responses — this forces a longer one.

```
Steps:
  1. Send text: "Give me a detailed recipe for chocolate chip cookies including all 
     ingredients, measurements, and step-by-step baking instructions."
  2. Wait for response.done
  3. Assert: delta count > 10 (forces a multi-delta response)
  4. Concatenate all deltas
  5. Assert: concatenation exactly equals the completed transcript
```

### 3. Deltas_AreUtf8Clean

**Purpose:** Verify that no delta contains a split UTF-8 sequence or invalid Unicode.

```
Steps:
  1. Send text: "Tell me about crème brûlée, café, naïve, and résumé — use the accented characters."
  2. Wait for response.done
  3. For each delta:
     - Assert: delta is valid UTF-16 (no unpaired surrogates)
     - Assert: delta does not start or end with a combining character
  4. Assert: concatenated deltas match completed transcript
```

### 4. Deltas_ArriveInStrictSequentialOrder

**Purpose:** Verify that deltas arrive in the same order as the final text — no reordering.

```
Steps:
  1. Send text: "Count from 1 to 10, one number per line."
  2. Wait for response.done
  3. Concatenate deltas
  4. Assert: concatenation matches completed transcript
  5. Assert: each delta appears in the completed transcript at the expected offset
     (i.e., delta[0] starts at offset 0, delta[1] starts at offset len(delta[0]), etc.)
```

### 5. RapidDeltas_AllCaptured

**Purpose:** Verify that no deltas are lost when they arrive in rapid succession.

```
Steps:
  1. Send text: "List 20 different types of cookies, one per line."
  2. Wait for response.done
  3. Count total deltas
  4. Assert: count > 15 (forces many deltas)
  5. Concatenate all deltas
  6. Assert: matches completed transcript exactly (no lost data)
```

### 6. DeltaTimings_MeasureBurstRate

**Purpose:** Measure the actual arrival rate of deltas to quantify the PropertyChanged pressure. This is a diagnostic test — it always passes but logs timing data.

```
Steps:
  1. Send text: "Tell me about the history of bread making in detail."
  2. Wait for response.done
  3. Using timestamped events, calculate:
     - Total delta count
     - Time between first and last delta
     - Minimum inter-delta interval
     - Average inter-delta interval
  4. Log all timing data
  5. Assert: true (diagnostic only — always passes)
```

This test reveals how fast deltas arrive, which informs whether throttling is needed.

## Priority

Tests 2, 4, 5 are the most important — they prove the data is clean and the garbling must be a rendering issue. Test 6 is diagnostic and helps calibrate the throttle interval for the fix.
