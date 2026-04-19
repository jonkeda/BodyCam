# Remaining Roadmap

What's left to build. Completed milestones (M0–M3, M5 tools, M7, M8) are done and documented in `docs/`.

---

## M4 — Bluetooth Glasses Integration

**Status:** Not started — waiting for hardware
**Plan:** [m4-glasses.md](m4-glasses.md)

- BT audio profile connection (pair glasses, route mic/speaker)
- Camera protocol investigation (BT, WiFi-Direct, or proprietary?)
- Camera bridge service
- `IGlassesService` interface
- Connection management UI
- Button mapping
- Fallback routing when disconnected

## M5 — Wake Word Detection (remaining)

**Status:** Interface exists (`IWakeWordService`), stub in place (`NullWakeWordService`). Tools and UI are done.
**Plan:** [m5-smart-features/](m5-smart-features/)

- Porcupine wake word engine integration
- Custom `.ppn` keyword files for each tool binding
- Three-layer listening architecture (Sleep → WakeWord → ActiveSession)
- Mic coordinator handoff between Porcupine and Realtime API

## M6 — Polish & Optimization

**Status:** Not started
**Plan:** [m6-polish.md](m6-polish.md)

- Latency optimization (target <500ms round-trip)
- Battery optimization for glasses
- Offline fallback (basic commands without internet)
- Error handling & resilience (reconnection, graceful degradation)
- Privacy indicators (visual/audio cues when recording)
- Cost tracking / token usage monitoring

## M9 — Multi-Provider Architecture

**Status:** Design complete, not implemented
**Plan:** [m9-providers/](m9-providers/)

- Extract `RealtimeClient` into `OpenAiRealtimeClient`
- Add `GeminiLiveRealtimeClient` (Google Gemini Live API)
- `RealtimeClientFactory` for runtime provider switching
- Provider-specific settings UI
