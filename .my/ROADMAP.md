# BodyCam — Open-Source RayBan Meta Alternative

## Vision

Build an affordable, open-source smart glasses platform using cheap Chinese BT glasses (TKYUAN BT5.3 w/ camera) paired with a .NET MAUI companion app powered by Microsoft Agent Framework (MAF) and OpenAI GPT models. The app runs on Android phone / Windows laptop and acts as the brain — the glasses are just sensors (camera, mic) and output (speakers).

---

## Hardware

### Primary: TKYUAN Smart Glasses (Alibaba)
- **Model:** TKYUAN Smart Glasses with Camera BT5.3
- **Source:** [Alibaba listing](https://www.alibaba.com/product-detail/TKYUAN-Smart-Glasses-with-Camera-BT5_1601558400215.html)
- **Key specs (typical for this class):**
  - Camera: 1080p HD (some variants 4K), front-facing
  - Audio: Open-ear speakers + dual mic
  - Connectivity: Bluetooth 5.3
  - Storage: On-board (micro-SD or internal, varies)
  - Battery: ~4-6 hrs mixed use
  - Charging: USB-C / magnetic
  - Form factor: Standard sunglasses frame, swappable lenses
- **Capabilities we use:**
  - BT audio (mic input + speaker output) via standard BT audio profile
  - Camera stream via BT or WiFi-Direct (model-dependent; may need custom bridge)
  - Physical button for push-to-talk / photo capture

### Fallback / Testing Hardware
| Device | Purpose |
|--------|---------|
| Windows laptop webcam | Vision development & testing |
| Windows laptop mic/speakers | Audio pipeline development |
| Android phone camera | Mobile vision testing |
| Android phone mic/speakers | Mobile audio testing |
| Any BT headset | Audio agent testing without glasses |

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| App Framework | .NET MAUI (Android + Windows) |
| Agent Framework | Microsoft Agent Framework (MAF) |
| AI Models | OpenAI gpt-5.4 / gpt-5.4-mini |
| Voice | OpenAI Realtime Streaming API |
| Vision | OpenAI gpt-5.4 Vision |
| Language | C# 12 / .NET 9+ |
| DI | Microsoft.Extensions.DependencyInjection |
| BT | Platform BT APIs (Android BT classic + BLE) |

---

## Architecture Overview

```
┌─────────────────────┐
│   Smart Glasses      │
│  (Camera + Mic +     │
│   Speakers)          │
└────────┬────────────┘
         │ Bluetooth Audio / Video
         ▼
┌─────────────────────────────────────────────┐
│           .NET MAUI Companion App            │
│                                              │
│  ┌──────────────┐  ┌──────────────────────┐ │
│  │ Audio Input   │  │ Camera/Vision Input  │ │
│  │ Service       │  │ Service              │ │
│  └──────┬───────┘  └──────────┬───────────┘ │
│         │                     │              │
│         ▼                     ▼              │
│  ┌─────────────────────────────────────────┐ │
│  │        Agent Orchestrator (MAF)         │ │
│  │                                         │ │
│  │  VoiceInputAgent ──► ConversationAgent  │ │
│  │       │                    │             │ │
│  │       │              VisionAgent         │ │
│  │       │                    │             │ │
│  │       └──── VoiceOutputAgent            │ │
│  └─────────────────────────────────────────┘ │
│         │                     │              │
│         ▼                     ▼              │
│  ┌──────────────┐  ┌──────────────────────┐ │
│  │ Audio Output  │  │ OpenAI Streaming     │ │
│  │ Service       │  │ Client               │ │
│  └──────────────┘  └──────────────────────┘ │
└─────────────────────────────────────────────┘
         │
         ▼ HTTPS / WebSocket
┌─────────────────────┐
│   OpenAI API         │
│  (gpt-5.4, Realtime,  │
│   Vision, TTS)       │
└─────────────────────┘
```

---

## Milestones

### Milestone 0 — Project Scaffold ✦ Foundation
**Goal:** Runnable MAUI app with DI, settings, and basic UI shell.

| # | Task | Details |
|---|------|---------|
| 0.1 | Create MAUI solution | Solution + project, folder structure from first.md |
| 0.2 | Settings & configuration | `AppSettings` class, secure key storage, `appsettings.json` |
| 0.3 | DI registration | Register all services and agents |
| 0.4 | Main page shell | Start/Stop button, transcript area, debug console |
| 0.5 | Build & run on Windows | Verify the skeleton runs clean |

**Exit criteria:** App launches on Windows, shows UI, DI resolves all services.

---

### Milestone 1 — Audio Pipeline (Laptop/Phone) ✦ Core
**Goal:** Capture mic audio, stream to OpenAI Realtime API, play back TTS — no glasses yet.

| # | Task | Details |
|---|------|---------|
| 1.1 | `IAudioInputService` + Windows impl | Capture PCM 16kHz/24kHz from default mic |
| 1.2 | `IAudioOutputService` + Windows impl | Play PCM frames with low latency |
| 1.3 | `OpenAiStreamingClient` | WebSocket connection to OpenAI Realtime API |
| 1.4 | `VoiceInputAgent` (MAF) | Mic → OpenAI, emit partial transcripts |
| 1.5 | `VoiceOutputAgent` (MAF) | OpenAI TTS → speaker playback |
| 1.6 | Wire into orchestrator | Continuous mic capture → transcription → display |
| 1.7 | Android audio impl | Platform-specific mic/speaker for Android |

**Exit criteria:** Speak into laptop mic → see transcript on screen → hear AI response through speakers.

---

### Milestone 2 — Conversation Agent ✦ Core
**Goal:** Add reasoning/conversation agent between voice-in and voice-out.

| # | Task | Details |
|---|------|---------|
| 2.1 | `ConversationAgent` (MAF) | Receives transcript, calls gpt-5.4 for reasoning |
| 2.2 | `SessionContext` model | Conversation history, user prefs, context window |
| 2.3 | System prompt design | Define assistant personality & capabilities |
| 2.4 | Orchestrator flow | VoiceIn → Conversation → VoiceOut pipeline |
| 2.5 | Transcript UI binding | Show both user speech and AI responses |
| 2.6 | Interruption handling | User speaks while AI is responding |

**Exit criteria:** Full voice conversation loop — ask a question, get a spoken answer with reasoning.

---

### Milestone 3 — Vision Pipeline (Laptop/Phone Camera) ✦ Core
**Goal:** Capture camera frames, send to gpt-5.4 Vision, integrate with conversation.

| # | Task | Details |
|---|------|---------|
| 3.1 | `ICameraService` + Windows impl | Webcam frame capture as byte[] / base64 |
| 3.2 | `VisionAgent` (MAF) | Send frames to gpt-5.4 Vision, get descriptions |
| 3.3 | Vision trigger modes | On-demand (button), periodic, voice-triggered ("what do you see?") |
| 3.4 | Context injection | Vision descriptions feed into ConversationAgent context |
| 3.5 | Android camera impl | CameraX / platform camera for Android |
| 3.6 | Camera preview UI | Small preview pane on MainPage |

**Exit criteria:** Point webcam at object → ask "what is this?" → get accurate spoken description.

---

### Milestone 4 — Bluetooth Glasses Integration ✦ Hardware
**Goal:** Connect TKYUAN glasses as audio + camera source, replacing laptop peripherals.

| # | Task | Details |
|---|------|---------|
| 4.1 | BT audio profile connection | Pair glasses, route mic/speaker through BT |
| 4.2 | BT camera investigation | Determine how glasses expose camera (BT, WiFi-Direct, or app-specific protocol) |
| 4.3 | Camera bridge service | Adapter to receive camera frames from glasses |
| 4.4 | `IGlassesService` interface | Unified abstraction over glasses capabilities |
| 4.5 | Connection management UI | Pair, connect, status indicator |
| 4.6 | Button mapping | Map glasses physical button to actions (push-to-talk, snap photo) |
| 4.7 | Fallback routing | Auto-switch to phone/laptop when glasses disconnected |

**Exit criteria:** Full conversation + vision loop running through the glasses hardware.

---

### Milestone 5 — Smart Features ✦ Experience
**Goal:** Build the "Meta-like" experience features.

| # | Task | Details |
|---|------|---------|
| 5.1 | "Hey BodyCam" wake word | Always-on low-power wake word detection |
| 5.2 | Look & Ask | Auto-capture frame when user asks a question |
| 5.3 | Live translation | Real-time speech translation mode |
| 5.4 | Object/text recognition | Continuous background scene understanding |
| 5.5 | Memory / recall | "Remember this" — save context for later retrieval |
| 5.6 | Notification readout | Read phone notifications through glasses speakers |
| 5.7 | Navigation cues | Simple audio navigation directions |

**Exit criteria:** Core smart assistant features working through glasses.

---

### Milestone 6 — Polish & Optimization ✦ Quality
**Goal:** Production-ready quality, performance, and UX.

| # | Task | Details |
|---|------|---------|
| 6.1 | Latency optimization | Target <500ms voice round-trip |
| 6.2 | Battery optimization | Minimize BT + network drain |
| 6.3 | Offline fallback | Basic commands when no internet |
| 6.4 | Error handling & resilience | Reconnection, graceful degradation |
| 6.5 | Settings page | Model selection, voice settings, privacy controls |
| 6.6 | Privacy indicators | Visual/audio cues when camera/mic active |
| 6.7 | Cost tracking | Token/API usage monitoring |

**Exit criteria:** Reliable daily-driver experience.

---

### Milestone 7 — Authentication & Key Management ✦ Cross-cutting
**Goal:** Secure API key storage, entry UX, and optional Azure OpenAI backend.

| # | Task | Details |
|---|------|---------|
| 7.1 | `IApiKeyService` interface | Get/set/clear/validate API key |
| 7.2 | SecureStorage implementation | MAUI SecureStorage for Android/iOS/Windows |
| 7.3 | Key entry UI | Settings page: masked field, validate button, status indicator |
| 7.4 | Key validation | Lightweight GET /v1/models call to confirm key works |
| 7.5 | WebSocket header auth | Pass key via Authorization header (not URL query string) |
| 7.6 | Azure OpenAI backend (optional) | MSAL Entra ID flow for enterprise users |
| 7.7 | Key masking in logs | Never log full key — show only last 4 chars |

**Exit criteria:** User can enter API key → stored encrypted → used for all OpenAI calls → key never in logs/plain text.

**Note:** Tasks 7.1–7.2, 7.5, 7.7 are needed for M1 (Audio). Tasks 7.3–7.4 can ship with M6 (Settings page). Task 7.6 is a future enhancement.

---

## Phase Execution Order

```
M0 (Scaffold) ──► M7 (Auth, key parts) ──► M1 (Audio) ──► M2 (Conversation) ──► M3 (Vision)
                                                                                      │
                                                                                      ▼
                                                                        M4 (Glasses Integration)
                                                                                      │
                                                                                      ▼
                                                                        M5 (Smart Features)
                                                                                      │
                                                                                      ▼
                                                                        M6 (Polish + Auth UI)
```

**M7 is split:** Core key storage (7.1, 7.2, 7.5, 7.7) ships before M1. Key entry UI (7.3, 7.4) ships with M6. Azure backend (7.6) is future.
**M0–M3 are pure software** — no glasses hardware needed. Develop and test entirely with laptop/phone.
**M4 onward** requires the TKYUAN glasses.

---

## Key Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Glasses camera not accessible via standard BT | Blocks M4 vision | Investigate glasses SDK/protocol early; worst case use phone camera strapped to glasses |
| OpenAI Realtime API latency | Poor UX | Buffer management, edge caching, consider local whisper for STT |
| TKYUAN build quality / compatibility | Hardware failure | Order 2+ units; keep phone fallback working |
| BT audio codec quality | Bad transcription | Test with high-quality BT codecs (aptX); add noise cancellation |
| API costs at scale | Expensive | Use gpt-5.4-mini for most tasks, gpt-5.4 only for vision |

---

## Cost Estimate (Hardware)

| Item | Approx. Cost |
|------|-------------|
| TKYUAN Smart Glasses (Alibaba) | $25–50 |
| Spare pair | $25–50 |
| Android test phone (if needed) | $100–200 |
| **Total hardware** | **~$75–300** |

vs. RayBan Meta: $299+ (and locked ecosystem)

---

## Getting Started

1. **Now:** Run Milestone 0 — scaffold the MAUI project
2. **Next:** Milestone 1 — get voice working with laptop mic
3. **When glasses arrive:** Jump to Milestone 4 investigation in parallel with M2/M3

Start with: `first.md` template generation → then iterate per milestone.
