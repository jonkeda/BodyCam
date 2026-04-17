# M1 Audio Pipeline — Implementation Steps

**9 steps, executed in order. Each step builds, tests, and is independently verifiable.**

---

## Dependency Graph

```
Step 1 (API Key Service)
   │
   ▼
Step 2 (IRealtimeClient Interface + Models)
   │
   ├──────────────────┬──────────────────┐
   ▼                  ▼                  ▼
Step 3 (Win Input)  Step 4 (Win Output) Step 5 (RealtimeClient WS)
   │                  │                  │
   └──────────────────┴──────────────────┘
                      │
                      ▼
               Step 6 (Rewire Agents)
                      │
                      ▼
               Step 7 (Event-Driven Orchestrator)
                      │
               ┌──────┴──────┐
               ▼              ▼
Step 8 (Android Audio)  Step 9 (E2E Voice Loop)
```

Steps 3, 4, 5 can be done **in parallel** after Step 2.
Step 8 (Android) can be done **in parallel** with Steps 6-7.

---

## Step Summary

| Step | Name | Key Deliverable | New Files | Est. Complexity |
|------|------|----------------|-----------|-----------------|
| **1** | [API Key Service](STEP-01-api-key-service.md) | `IApiKeyService` + SecureStorage impl | 2 new, 2 modified | Low |
| **2** | [Realtime Interface](STEP-02-realtime-interface.md) | `IRealtimeClient`, models, delete old interface | 3 new, 2 deleted, 6+ modified | Medium |
| **3** | [Windows Audio Input](STEP-03-windows-audio-input.md) | `WindowsAudioInputService` (NAudio mic) | 1 new, 2 modified | Low |
| **4** | [Windows Audio Output](STEP-04-windows-audio-output.md) | `WindowsAudioOutputService` (NAudio speaker) | 1 new, 3 modified | Low |
| **5** | [Realtime Client Impl](STEP-05-realtime-client-impl.md) | WebSocket connection, JSON messages, event dispatch | 3 new, 1 replaced | **High** |
| **6** | [Rewire Agents](STEP-06-rewire-agents.md) | Event-driven `VoiceIn`, `VoiceOut`, simplified `Conversation` | 0 new, 6 rewritten | Medium |
| **7** | [Event-Driven Orchestrator](STEP-07-event-driven-orchestrator.md) | Orchestrator wires events → agents → UI | 0 new, 3 rewritten | Medium |
| **8** | [Android Audio](STEP-08-android-audio.md) | `AndroidAudioInputService`, `AndroidAudioOutputService` | 2 new, 2 modified | Medium |
| **9** | [E2E Voice Loop](STEP-09-end-to-end-voice-loop.md) | Working voice conversation, manual test pass | 3 modified | Low (integration) |

---

## Critical Path

**Steps 1 → 2 → 5 → 6 → 7 → 9** — this is the longest chain and determines M1 completion time.

Steps 3+4 (Windows audio) and Step 8 (Android audio) are off the critical path — they can be built in parallel.

---

## M1 Exit Criteria (from DESIGN.md)

- [ ] Speak into laptop mic → see transcript on screen
- [ ] Hear AI response through speakers
- [ ] Works on Windows (Android is secondary for M1)

---

## NuGet Packages Added

| Package | Version | When | Platform |
|---------|---------|------|----------|
| NAudio | 2.3.0 | Step 3 | Windows only (conditional) |

No other external packages needed. `System.Net.WebSockets.Client` and `System.Text.Json` are built-in.

---

## Key Decisions Embedded in Steps

1. **API key via SecureStorage** (Step 1) — not environment variable, not config file
2. **Event-based `IRealtimeClient`** (Step 2) — not `IAsyncEnumerable`, not callback
3. **NAudio WaveInEvent** for Windows mic (Step 3) — not WASAPI, not ASIO
4. **NAudio WaveOutEvent + BufferedWaveProvider** for Windows speaker (Step 4)
5. **`ClientWebSocket` with header auth** (Step 5) — key in header, not URL
6. **JSON source generation** for WebSocket messages (Step 5) — performance
7. **VoiceOutputAgent tracks playback bytes** for interruption (Step 6)
8. **Orchestrator subscribes to events** (Step 7) — not pipeline, not polling
9. **`AudioSource.VoiceCommunication`** on Android for AEC (Step 8)
