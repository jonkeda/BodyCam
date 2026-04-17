# M2 — Conversation Agent ✦ Core

**Status:** NOT STARTED
**Goal:** Add reasoning/conversation agent between voice-in and voice-out.

---

## Scope

| # | Task | Details |
|---|------|---------|
| 2.1 | `ConversationAgent` (MAF) | Receives transcript, calls gpt-5.4 for reasoning |
| 2.2 | `SessionContext` model | Conversation history, user prefs, context window |
| 2.3 | System prompt design | Define assistant personality & capabilities |
| 2.4 | Orchestrator flow | VoiceIn → Conversation → VoiceOut pipeline |
| 2.5 | Transcript UI binding | Show both user speech and AI responses |
| 2.6 | Interruption handling | User speaks while AI is responding |

## Exit Criteria

- [ ] Full voice conversation loop working
- [ ] Ask a question → get a spoken answer with reasoning
- [ ] Conversation history maintained across turns
- [ ] User can interrupt AI mid-response

---

## Technical Design

### MAF Integration

**Microsoft Agent Framework (MAF)** will be used to define agents as first-class entities.

```
NuGet: Microsoft.Extensions.AI
NuGet: Microsoft.Extensions.AI.OpenAI (or Azure.AI.OpenAI)
```

**ConversationAgent responsibilities:**
1. Receive user transcript text
2. Maintain sliding window of conversation history
3. Call gpt-5.4 / gpt-5.4-mini Chat Completions API
4. Return reply text to orchestrator

### Conversation Flow

```
VoiceInputAgent
  │ transcript (string)
  ▼
AgentOrchestrator
  │ passes transcript + SessionContext
  ▼
ConversationAgent
  │ builds messages array from SessionContext
  │ calls ChatCompletions API
  │ appends reply to SessionContext
  │ returns reply string
  ▼
AgentOrchestrator
  │ passes reply to VoiceOutputAgent
  ▼
VoiceOutputAgent → TTS → Speaker
```

### Two-Mode Architecture

The OpenAI Realtime API can handle the full loop (audio-in → reasoning → audio-out) in a single WebSocket. However, we separate concerns:

**Mode A — Realtime API handles everything (simpler, M1 default):**
- Audio in → Realtime API → transcript + TTS audio out
- ConversationAgent is bypassed; the Realtime session IS the conversation

**Mode B — Separated pipeline (more control, M2 target):**
- Audio in → Realtime API → transcript only (disable TTS)
- Transcript → ConversationAgent → GPT Chat API → reply text
- Reply text → Realtime API or separate TTS → audio out

**Decision:** Start with Mode A in M1 (simpler). M2 adds Mode B for cases where we need custom reasoning, tool use, or vision injection.

### SessionContext Design

```csharp
public class SessionContext
{
    public List<ChatMessage> Messages { get; }
    public int MaxHistoryTokens { get; set; } = 4000;

    // Sliding window: trim old messages when context exceeds limit
    public void TrimHistory() { ... }

    // Vision context injection point
    public string? LastVisionDescription { get; set; }
}
```

### System Prompt

```
You are BodyCam, an AI assistant integrated into smart glasses.
You can see what the user sees (when vision is active) and hear what they say.

Guidelines:
- Be concise — the user hears your response through small speakers
- Prefer short, direct answers (1-3 sentences)
- If vision context is available, reference what you see
- You can be asked to remember things for later
- Be conversational and natural
```

### Interruption Handling

When user speaks while AI is still responding:
1. Cancel current TTS playback (`VoiceOutputAgent.StopAsync()`)
2. Cancel any in-flight Chat API call
3. Process new transcript immediately
4. OpenAI Realtime API has built-in `input_audio_buffer.clear` for this

---

## Risks

| Risk | Mitigation |
|------|-----------|
| Mode A vs B confusion | Clear flag in AppSettings; default to Mode A |
| Context window overflow | Token counting + sliding window trim |
| Interruption race conditions | CancellationToken propagation, sequential processing |
| MAF version compatibility | Pin NuGet version, test early |
