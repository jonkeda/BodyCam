# Remaining Roadmap

What's left to build. Completed milestones (M0–M3, M5 tools, M7, M8) are done and documented in `docs/`.

---

## M4 — Bluetooth Glasses Integration (Audio + Buttons only)

**Status:** Not started — waiting for hardware  
**Plan:** [m4-glasses.md](m4-glasses.md)  
**Note:** Camera portions moved to M11.

- BT audio profile connection (pair glasses, route mic/speaker)
- `IGlassesService` interface (audio + buttons)
- Connection management UI
- Button mapping (tap, double-tap, long-press)
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

## M11 — Camera Architecture

**Status:** Planning complete  
**Plan:** [m11-camera/](m11-camera/)

Unified camera abstraction (`ICameraProvider`) supporting multiple sources
with one active camera at a time. Absorbs M4's camera portions.

- **Phase 1:** Camera abstraction + phone camera provider (wrap existing CameraView)
- **Phase 2:** USB bodycam support (Windows MediaCapture, Android UVC)
- **Phase 3:** WiFi/IP cameras (RTSP, HTTP MJPEG)
- **Phase 4:** Chinese WiFi glasses (WiFi-Direct discovery, per-model profiles)
- **Phase 5:** Meta Ray-Ban glasses (Meta SDK — blocked on SDK access)

Camera sources:
| Source | Protocol | Status |
|--------|----------|--------|
| Phone camera | CameraView (existing) | Needs wrapping into provider |
| USB bodycam | UVC / MediaCapture | Not started |
| WiFi/IP camera | RTSP / MJPEG | Not started |

## M12 — Input Audio Architecture

**Status:** Planning complete  
**Plan:** [m12-input-audio/](m12-input-audio/)

Unified audio input abstraction (`IAudioInputProvider`) supporting multiple
mic sources with one active input at a time. AudioInputManager implements
IAudioInputService for backward compatibility.

- **Phase 1:** Abstraction + platform mic providers (wrap existing Windows/Android)
- **Phase 2:** BT audio input (HFP/SCO, device enumeration)
- **Phase 3:** USB audio devices
- **Phase 4:** WiFi glasses audio stream

## M13 — Output Audio Architecture

**Status:** Planning complete  
**Plan:** [m13-output-audio/](m13-output-audio/)

Unified audio output abstraction (`IAudioOutputProvider`) supporting multiple
speaker sources with one active output at a time. AudioOutputManager implements
IAudioOutputService for backward compatibility.

- **Phase 1:** Abstraction + platform speaker providers (wrap existing Windows/Android)
- **Phase 2:** BT audio output (A2DP, codec selection, latency)
- **Phase 3:** USB audio output
- **Phase 4:** Volume management + audio ducking

## M14 — Button & Gesture Input

**Status:** Planning complete  
**Plan:** [m14-buttons/](m14-buttons/)

Button input abstraction with gesture recognition (tap/double-tap/long-press)
and configurable action mapping. Multiple providers active simultaneously.

- **Phase 1:** Abstraction + gesture recognizer + action mapping
- **Phase 2:** BT glasses buttons (AVRCP/SMTC + custom GATT)
- **Phase 3:** Phone buttons (volume keys, shake gesture)
- **Phase 4:** Keyboard shortcuts (Windows dev)
| Chinese WiFi glasses | WiFi-Direct + RTSP | Not started, needs hardware |
| Meta Ray-Ban | Meta SDK | Blocked on SDK access |
