# M5 — Wake Word Detection

**Status:** INFRASTRUCTURE COMPLETE, ENGINE NOT IMPLEMENTED  
**Goal:** Always-on, low-power wake word detection using Porcupine so BodyCam can
listen for voice commands without keeping a Realtime API connection open.

**Depends on:** M1 (audio pipeline), M2 (conversation), M3 (vision tools).

---

## Why This Matters

The Realtime API costs **$5.76/hour** of active audio input. An always-on WebSocket
drains ~300mW continuous — smart glasses batteries (300-500mAh) would die in 2-3 hours.

Porcupine runs on-device at **~10mW** with **<50ms latency**, no network, and handles
multiple keywords in a single neural network pass. The mic stays local until a wake
word triggers the expensive API connection.

---

## Three-Layer Listening Architecture

```
LAYER 1: SLEEP          Nothing running. Button press only.
        │ button
        ▼
LAYER 2: WAKE WORD      Porcupine runs locally. No network. ~10mW.
        │ wake word        Listens for "Hey BodyCam" + 7 tool keywords
        ▼
LAYER 3: ACTIVE SESSION  Realtime API WebSocket connected. Full AI. ~300mW+.
                           Returns to Layer 2 on "Go to sleep" / timeout
```

---

## What's Already Implemented

| Component | Status | Location |
|-----------|--------|----------|
| `IWakeWordService` interface | ✅ Done | `Services/IWakeWordService.cs` |
| `WakeWordAction` enum (StartSession, GoToSleep, InvokeTool) | ✅ Done | `Services/IWakeWordService.cs` |
| `WakeWordEntry` record | ✅ Done | `Services/IWakeWordService.cs` |
| `WakeWordDetectedEventArgs` | ✅ Done | `Services/IWakeWordService.cs` |
| `NullWakeWordService` (no-op stub) | ✅ Done | `Services/NullWakeWordService.cs` |
| `WakeWordBinding` (tool-level config) | ✅ Done | `Tools/WakeWordBinding.cs` |
| `WakeWordMode` enum (QuickAction, FullSession) | ✅ Done | `Tools/WakeWordBinding.cs` |
| `ITool.WakeWord` property | ✅ Done | 7 tools declare bindings |
| `ToolDispatcher.BuildWakeWordEntries()` | ✅ Done | Builds system + tool keywords |
| `AgentOrchestrator.OnWakeWordDetected()` | ✅ Done | 3-case handler |
| `IMicrophoneCoordinator` + implementation | ✅ Done | Sequential mic handoff |
| Unit tests (NullService, Bindings, Orchestrator) | ✅ Done | `BodyCam.Tests` |

## What's Missing

| Component | Status | What's Needed |
|-----------|--------|---------------|
| `PorcupineWakeWordService` | ❌ Missing | Real implementation of `IWakeWordService` |
| Porcupine NuGet package | ❌ Missing | `Pv.Porcupine` package reference |
| `.ppn` keyword model files | ❌ Missing | Generated from Picovoice Console |
| Picovoice AccessKey management | ❌ Missing | Settings UI + `IApiKeyService` |
| Audio frame adapter (PCM → Porcupine format) | ❌ Missing | 16kHz 16-bit mono, `FrameLength` samples |
| Quick action flow (connect → execute → disconnect) | ❌ Missing | In orchestrator, after tool result |
| Session timeout (auto-disconnect after silence) | ❌ Missing | Configurable 30s–2min |
| Layer transition UI feedback | ❌ Missing | Status bar / LED color changes |

---

## Wake Word Inventory

### System Keywords

| Keyword | `.ppn` File | Action |
|---------|-------------|--------|
| "Hey BodyCam" | `hey-bodycam_en_windows.ppn` | StartSession (Layer 2 → 3) |
| "Go to sleep" | `go-to-sleep_en_windows.ppn` | GoToSleep (Layer 2 → 1) |

### Tool Keywords (7 tools)

| Tool | Keyword | Mode | Behavior |
|------|---------|------|----------|
| `describe_scene` | "bodycam-look" | QuickAction | Capture → describe → speak → Layer 2 |
| `read_text` | "bodycam-read" | QuickAction | Capture → OCR → speak → Layer 2 |
| `find_object` | "bodycam-find" | FullSession | Start session, LLM asks "what to find?" |
| `save_memory` | "bodycam-remember" | FullSession | Start session, LLM asks "what to save?" |
| `set_translation_mode` | "bodycam-translate" | FullSession | Start session, LLM asks language |
| `make_phone_call` | "bodycam-call" | FullSession | Start session, LLM asks who to call |
| `navigate_to` | "bodycam-navigate" | FullSession | Start session, LLM asks destination |

---

## Phases

### Phase 1: Porcupine Engine Integration
Add the Porcupine NuGet package, implement `PorcupineWakeWordService`, generate
`.ppn` keyword files, add AccessKey management. Windows-only first.

**Deliverables:** `PorcupineWakeWordService`, `.ppn` files, AccessKey in settings,
audio frame adapter, DI registration swap from Null to Porcupine.

### Phase 2: Quick Action & Session Flow
Implement the quick-action lifecycle (connect → execute tool → speak → disconnect)
and session timeout (auto-disconnect after silence). Wire layer transitions with
UI feedback.

**Deliverables:** Quick action orchestration, session timeout, layer status UI,
audio feedback tones, MicrophoneCoordinator integration tests.

### Phase 3: Android & Cross-Platform
Port wake word detection to Android using `Porcupine.Android` native package.
Generate Android-specific `.ppn` files. Handle Android audio permissions and
background service requirements.

**Deliverables:** Android `PorcupineWakeWordService`, Android `.ppn` files,
background service, platform-specific audio routing.

### Phase 4: iOS Platform Support
Port wake word detection to iOS using `Porcupine-iOS` binding. Configure
`AVAudioSession` for background audio processing (`UIBackgroundModes: audio`).
Generate iOS-specific `.ppn` keyword files. Handle iOS audio route change
notifications and audio session interruptions (phone calls, Siri).

**Deliverables:** iOS `PorcupineWakeWordService`, iOS `.ppn` files,
`AVAudioSession` background audio configuration, audio interruption handling,
`NSMicrophoneUsageDescription` permission.

---

## Exit Criteria

- [ ] "Hey BodyCam" activates full session from wake word layer
- [ ] "Go to sleep" puts device to sleep from wake word layer
- [ ] All 7 tool wake words trigger correct tool execution
- [ ] Quick actions (look, read) connect → execute → speak → disconnect automatically
- [ ] Session timeout returns to wake word layer after configurable silence
- [ ] Mic handoff works cleanly (Porcupine releases → Realtime API captures)
- [ ] AccessKey stored securely alongside API keys
- [ ] Battery draw in wake word layer ≤ 15mW

---

## Documents

| Document | Purpose |
|----------|---------|
| [overview.md](overview.md) | This file — scope, status, exit criteria |
| [5.1-WAKE-WORD-ANALYSIS.md](5.1-WAKE-WORD-ANALYSIS.md) | Cost/battery analysis, three-layer architecture design |
| [WAKEWORD-ARCHITECTURE.md](WAKEWORD-ARCHITECTURE.md) | Tool-based wake word binding, orchestrator integration, data flow |
| [wake-word-engines.md](wake-word-engines.md) | Engine comparison — Porcupine vs free alternatives |
| [phase1-porcupine.md](phase1-porcupine.md) | Phase 1 — Porcupine engine integration |
| [phase2-session-flow.md](phase2-session-flow.md) | Phase 2 — Quick actions, timeout, UI feedback |
| [phase3-android.md](phase3-android.md) | Phase 3 — Android & cross-platform |
| [phase4-ios.md](phase4-ios.md) | Phase 4 — iOS platform support |
