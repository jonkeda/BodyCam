# RCA-003: Transcript Ordering & Overlap Bugs

## Symptoms

From the UI screenshot:

```
AI: Hi there! How can I help you
You: Hello.
AI: Hi there! How can I help you today?
AI: Sure! You could get cookies from a local bakery,
You: Uhm, I want some cookies, where can I get them?
AI: Sure! You could get cookies from a local bakery, a grocery store, or even recommendations?
```

**Issue 1 — User line appears after AI greeting:**
The AI's first line appears, then "You: Hello." shows up underneath it, then the AI responds *again* with a slightly different greeting. The user's input transcript arrives *after* the AI has already started streaming its response.

**Issue 2 — AI lines overlap / duplicate:**
"AI: Sure! You could get cookies from a local bakery," appears as one entry, then the *full* completed sentence appears as a separate entry below it. Long AI responses show up twice — once as the streaming delta accumulation, then again as the completed transcript.

## Root Cause Analysis

### Issue 1: User transcript arrives late

**Event timing in the Realtime API:**

```
1. User speaks "Hello"
2. input_audio_buffer.speech_stopped           ← VAD detects end of speech
3. response.created                            ← model starts responding immediately
4. response.audio_transcript.delta "Hi "       ← AI response streams
5. response.audio_transcript.delta "there!..." ← more deltas
6. conversation.item.input_audio_transcription.completed "Hello."  ← user transcript arrives LATE
7. response.audio_transcript.done              ← AI response completes
8. response.done
```

The Realtime API transcribes user input **asynchronously**. The model starts responding as soon as VAD commits the audio buffer, but the STT transcription of the user's words arrives later — often *during* or *after* the AI response has already started streaming.

**In `AgentOrchestrator`:**
- `OnOutputTranscriptDelta` fires first → creates an AI `TranscriptEntry` and starts appending deltas
- `OnInputTranscriptCompleted` fires later → appends "You: Hello." *below* the already-started AI entry

**Result:** The user's line always appears after the AI has begun responding, because that's the order the events arrive.

### Issue 2: Streaming deltas create a partial entry, then completed creates another

**In `MainViewModel`:**

```csharp
// TranscriptDelta handler:
if (_currentAiEntry is null)
{
    _currentAiEntry = new TranscriptEntry { Role = "AI" };
    Entries.Add(_currentAiEntry);               // ← Adds entry #1
}
_currentAiEntry.Text += delta;                  // ← Appends deltas to entry #1

// TranscriptCompleted handler:
if (msg.StartsWith("AI:"))
{
    if (_currentAiEntry is not null)
        _currentAiEntry.Text = msg[3..].Trim(); // ← Overwrites entry #1 with full text
    _currentAiEntry = null;
}
```

This *should* work — the completed handler overwrites the delta entry with the final text and clears `_currentAiEntry`. **But the bug happens when a `You:` completed event arrives between the delta stream and the AI completed event:**

```
1. TranscriptDelta "Sure! You could get" → creates AI entry, appends
2. TranscriptDelta " cookies from..."    → appends to same entry
3. TranscriptCompleted "You: Uhm, I want..." → sets _currentAiEntry = null  ← BUG
4. TranscriptDelta " a grocery store..." → _currentAiEntry is null → creates NEW AI entry
5. TranscriptCompleted "AI: Sure! You could get cookies..." → overwrites entry from step 4 only
```

**The `You:` completed handler sets `_currentAiEntry = null` (line in `TranscriptCompleted`).**
When the next AI delta arrives, `_currentAiEntry` is null, so a *new* `TranscriptEntry` is created. Now there are two AI entries: the orphaned partial one from before the interruption, and the new one.

For the non-interruption case, there's a simpler variant of this bug: when `InputTranscriptCompleted` fires during AI streaming (which it always does per Issue 1), the `You:` completed handler sets `_currentAiEntry = null`, splitting the AI deltas across two entries.

## Proposed Fixes

### Fix 1: Buffer user transcripts and insert them in chronological order

Instead of appending the user transcript wherever the cursor currently is, insert it *before* the current AI entry:

```csharp
// In TranscriptCompleted handler for "You:" messages:
if (_currentAiEntry is not null)
{
    // Insert user message BEFORE the current AI streaming entry
    var aiIndex = Entries.IndexOf(_currentAiEntry);
    if (aiIndex >= 0)
    {
        Entries.Insert(aiIndex, new TranscriptEntry { Role = "You", Text = msg[4..].Trim() });
        // Do NOT null out _currentAiEntry — AI is still streaming
        return;
    }
}
// Fallback: no active AI entry, just append
Entries.Add(new TranscriptEntry { Role = "You", Text = msg[4..].Trim() });
```

### Fix 2: Don't null `_currentAiEntry` on user transcript

The `You:` completed handler must not clear `_currentAiEntry` — that's only for the AI completed handler:

```csharp
_orchestrator.TranscriptCompleted += (_, msg) =>
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        if (msg.StartsWith("You:"))
        {
            // Don't touch _currentAiEntry — AI may still be streaming
            // Insert before current AI entry if one exists
            ...
        }
        else if (msg.StartsWith("AI:"))
        {
            if (_currentAiEntry is not null)
                _currentAiEntry.Text = msg[3..].Trim();
            _currentAiEntry = null; // Only clear here
        }
    });
};
```

## Files Involved

| File | Role |
|------|------|
| `src/BodyCam/ViewModels/MainViewModel.cs` | TranscriptDelta + TranscriptCompleted handlers — where both bugs manifest |
| `src/BodyCam/Orchestration/AgentOrchestrator.cs` | Event routing — fires events in API arrival order |
| `src/BodyCam/Services/RealtimeClient.cs` | DispatchMessage — event arrival order is dictated by API |

## Severity

**Medium** — Transcript display is confusing but audio playback is correct. Users hear the right thing but see garbled text.
