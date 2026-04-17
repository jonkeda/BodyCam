# M2 Conversation Agent — Implementation Steps

**7 steps, executed in order. Each step builds, tests, and is independently verifiable.**

---

## Dependency Graph

```
Step 1 (SessionContext + Mode Flag)
   │
   ▼
Step 2 (Chat Completions Client)
   │
   ▼
Step 3 (ConversationAgent with IChatClient)
   │
   ├──────────────────┐
   ▼                  ▼
Step 4 (Orchestrator  Step 5 (Transcript
 Mode B flow)          UI binding)
   │                  │
   └──────────────────┘
            │
            ▼
      Step 6 (Interruption handling)
            │
            ▼
      Step 7 (E2E conversation loop)
```

Steps 4 and 5 can be done **in parallel** after Step 3.

---

## Step Summary

| Step | Name | Key Deliverable | New Files | Modified Files | Est. Complexity |
|------|------|----------------|-----------|----------------|-----------------|
| **1** | [SessionContext + Mode Flag](STEP-01-session-context.md) | Enhanced `SessionContext`, `ConversationMode` enum, `AppSettings.Mode` | 0 new | 3 modified | Low |
| **2** | [Chat Completions Client](STEP-02-chat-client.md) | `IChatCompletionsClient`, HTTP impl, DI registration | 3 new, 2 modified | Medium |
| **3** | [ConversationAgent](STEP-03-conversation-agent.md) | Rewrite `ConversationAgent` to call Chat API, streaming reply | 0 new | 1 rewritten, 1 modified | Medium |
| **4** | [Orchestrator Mode B Flow](STEP-04-orchestrator-mode-b.md) | Dual-mode orchestrator: Mode A (passthrough) or Mode B (separated pipeline) | 0 new | 1 rewritten | **High** |
| **5** | [Transcript UI Binding](STEP-05-transcript-ui.md) | Differentiate user/AI entries, streaming deltas for Mode B | 0 new | 2 modified | Low |
| **6** | [Interruption Handling](STEP-06-interruption.md) | Cancel in-flight Chat API call, cancel TTS, process new input | 0 new | 3 modified | Medium |
| **7** | [E2E Conversation Loop](STEP-07-e2e-loop.md) | Working Mode B voice conversation, manual test pass | 0 new | 2 modified | Low (integration) |

---

## Critical Path

**Steps 1 → 2 → 3 → 4 → 6 → 7** — this is the longest chain and determines M2 completion time.

Step 5 (transcript UI) is off the critical path — it can be built in parallel with Step 4.

---

## NuGet Packages Added

| Package | Version | When | Purpose |
|---------|---------|------|---------|
| Microsoft.Extensions.AI | latest stable | Step 2 | `IChatClient` abstraction |
| Microsoft.Extensions.AI.OpenAI | latest stable | Step 2 | OpenAI `IChatClient` implementation |
| Azure.AI.OpenAI | latest stable | Step 2 | Azure OpenAI Chat support |

---

## Key Design Decisions

1. **Dual-mode via `ConversationMode` enum** (Step 1) — `Realtime` (Mode A, M1 behavior) vs `Separated` (Mode B, new)
2. **`IChatClient` from Microsoft.Extensions.AI** (Step 2) — not raw HttpClient, enables MAF interop
3. **Streaming Chat Completions** (Step 3) — `CompleteStreamingAsync` for progressive response, not blocking
4. **Realtime API for STT only in Mode B** (Step 4) — disable audio output modality, use transcript events
5. **Separate TTS path in Mode B** (Step 4) — send reply text back to Realtime API as `conversation.item.create` + `response.create`, or use REST TTS
6. **CancellationToken propagation** (Step 6) — per-turn CTS cancelled on interruption
7. **Mode A remains default** (Step 1) — M1 behavior unchanged, Mode B opt-in via settings

---

## M2 Exit Criteria (from DESIGN.md)

- [ ] Full voice conversation loop working in Mode B
- [ ] Ask a question → get a spoken answer with custom reasoning
- [ ] Conversation history maintained across turns (SessionContext)
- [ ] User can interrupt AI mid-response
- [ ] Mode A (M1 behavior) still works unchanged
