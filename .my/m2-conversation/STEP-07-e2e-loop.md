# M2 Implementation — Step 7: End-to-End Conversation Loop

**Depends on:** Steps 4, 5, 6 all complete
**Produces:** Working Mode B voice conversation verified end-to-end, manual test pass

---

## Why Last?

This is the integration step. All pieces are built — now we verify they work together as a complete loop. No new code; just fixes, cleanup, and manual testing.

---

## Tasks

### 7.1 — Verify full Mode B loop

Manual test on Windows:

1. Open app → go to Settings → set Conversation Mode to "Separated"
2. Return to main page → press Start
3. Verify debug log shows: `Model: ...`, `Realtime connected.`, `Audio pipeline started.`
4. Speak a question ("What is the capital of France?")
5. Verify:
   - "You: What is the capital of France?" appears in transcript
   - Status shows "Thinking..."
   - AI reply streams word-by-word in transcript ("The capital of France is Paris.")
   - Status shows "Speaking..."
   - TTS audio plays through speakers
   - Status returns to "Listening..."
6. Ask a follow-up ("And what's its population?")
7. Verify: AI references prior context (knows you were asking about Paris)
8. Interrupt: start speaking while AI is still talking
9. Verify: audio stops, new input is processed

### 7.2 — Verify Mode A regression

Switch back to Realtime mode in settings. Verify M1 behavior is completely unchanged:

1. Speak → see transcript → hear response
2. Interruption works
3. No debug errors

### 7.3 — Fix any integration issues

Common issues to watch for:

- **Double transcript entries** — Mode B might fire both ConversationAgent events AND Realtime transcript events. Verify the guards in Step 4 work.
- **TTS not playing** — Realtime API may reject `conversation.item.create` with certain formats. Test the exact JSON payload.
- **SessionContext growing unbounded** — Verify `GetTrimmedHistory` is called and trims correctly after many turns.
- **Status stuck on "Thinking..."** — If Chat API errors, status should reset. Add error handling in `ProcessModeBAsync`.

### 7.4 — Add error recovery for Mode B

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs` — MODIFY (if needed)

Ensure `ProcessModeBAsync` always resets status on error:

```csharp
catch (Exception ex)
{
    DebugLog?.Invoke(this, $"Mode B error: {ex.Message}");
    // Reset status so the user knows something went wrong
    // (ViewModel subscribes to DebugLog and can show errors)
}
```

### 7.5 — Verify settings persistence

1. Set Mode to Separated → close app → reopen → verify Mode is still Separated
2. Verify system prompt default has changed to the BodyCam personality
3. Verify chat model selection affects Mode B (changing from gpt-5.4-mini to gpt-5.4)

---

## Manual Test Checklist

| # | Test | Mode | Expected |
|---|------|------|----------|
| 1 | Basic question | B | Streaming text + TTS audio response |
| 2 | Follow-up question | B | AI references prior conversation |
| 3 | Interrupt mid-response | B | Audio stops, new input processed |
| 4 | 10+ turn conversation | B | No context overflow, history trims correctly |
| 5 | Switch to Mode A | A | M1 behavior unchanged |
| 6 | Switch back to Mode B | B | Works without restart |
| 7 | No API key | B | Prompt dialog appears, same as Mode A |
| 8 | Network error mid-stream | B | Error logged, status resets, app doesn't crash |

---

## Exit Criteria (M2 Complete)

- [x] Full voice conversation loop working in Mode B
- [x] Ask a question → get a spoken answer with custom reasoning
- [x] Conversation history maintained across turns (SessionContext)
- [x] User can interrupt AI mid-response
- [x] Mode A (M1 behavior) still works unchanged
