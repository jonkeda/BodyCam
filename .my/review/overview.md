# Architecture Review — Overview

## System Summary

BodyCam is a .NET MAUI app (Android + Windows) that acts as an AI-powered wearable assistant. It streams audio through the OpenAI Realtime API, captures camera frames for vision analysis, and supports wake-word activation via Picovoice Porcupine. The system uses a three-layer state machine (Sleep → WakeWord → ActiveSession) orchestrated through a central hub.

## Layer Map

```
┌────────────────────────────────────────────────────────────┐
│  UI Layer                                                  │
│  MainPage.xaml / SettingsPage.xaml                         │
│  MainViewModel / SettingsViewModel                         │
├────────────────────────────────────────────────────────────┤
│  Orchestration Layer                                       │
│  AgentOrchestrator (central hub)                           │
│  ├─ VoiceInputAgent (mic → Realtime API)                  │
│  ├─ VoiceOutputAgent (Realtime API → speaker)             │
│  ├─ ConversationAgent (deep analysis via Chat Completions)│
│  └─ VisionAgent (frame → description via Vision model)    │
├────────────────────────────────────────────────────────────┤
│  Tools Layer                                               │
│  ToolDispatcher → 12 ITool implementations                │
│  ToolBase<T> (JSON deserialization + execution)            │
├────────────────────────────────────────────────────────────┤
│  Services Layer                                            │
│  AudioInputManager / AudioOutputManager (provider pattern)│
│  CameraManager / ButtonInputManager                       │
│  RealtimeClient (WebSocket to OpenAI)                     │
│  PorcupineWakeWordService / MicrophoneCoordinator         │
│  MemoryStore / SettingsService / ApiKeyService             │
├────────────────────────────────────────────────────────────┤
│  Provider Layer (platform-specific)                        │
│  Windows: WasapiCapture, WasapiOut, NAudio, KeyboardInput │
│  Android: AudioRecord, AudioTrack, BT intents             │
│  Shared: PhoneCameraProvider (CommunityToolkit CameraView)│
├────────────────────────────────────────────────────────────┤
│  Infrastructure                                            │
│  ObservableObject, ViewModelBase, RelayCommand             │
│  AppSettings, ModelOptions, SessionContext                 │
└────────────────────────────────────────────────────────────┘
```

## Data Flow — Active Session

```
User speaks → Platform mic provider
  → AudioInputManager.AudioChunkAvailable
  → VoiceInputAgent.OnAudioChunk
  → RealtimeClient.SendAudioChunkAsync (WebSocket)
  → OpenAI Realtime API

API responds → RealtimeClient.ReceiveLoop
  → AudioDelta → VoiceOutputAgent → AudioOutputManager → Speaker
  → TranscriptDelta → AgentOrchestrator → MainViewModel → UI
  → FunctionCallReceived → ToolDispatcher → Tool.ExecuteAsync
    → SendFunctionCallOutputAsync → API continues
```

## Data Flow — Offline Vision

```
User taps "Look" → MainViewModel.DispatchActionAsync
  → SendVisionCommandAsync
  → CameraManager.CaptureFrameAsync → PhoneCameraProvider
  → VisionAgent.DescribeFrameAsync (Chat Completions API)
  → TranscriptEntry added to Entries collection
```

## Strengths

1. **Clean provider abstraction** — Platform-specific audio/camera behind interfaces with DI
2. **Hot-plug support** — Bluetooth devices register/unregister dynamically with automatic fallback
3. **Tool system** — Pluggable function calling with wake-word bindings and schema generation
4. **Session context windowing** — Token-budget-aware history trimming prevents context overflow
5. **Lightweight MVVM** — Custom ObservableObject/RelayCommand avoids CommunityToolkit dependency
6. **Dual-path vision** — Works both offline (Chat Completions) and online (through Realtime session)

## Key Risks

| Risk | Severity | Details |
|------|----------|---------|
| No thread safety in managers | High | See [thread-safety.md](thread-safety.md) |
| No WebSocket reconnection | High | See [resilience.md](resilience.md) |
| Silent error swallowing | Medium | See [error-handling.md](error-handling.md) |
| Mutable shared AppSettings | Medium | See [configuration.md](configuration.md) |
| MemoryStore scalability | Low | See [performance.md](performance.md) |

## Review Documents

| Document | Focus |
|----------|-------|
| [thread-safety.md](thread-safety.md) | Concurrency gaps in managers, MemoryStore, orchestrator |
| [resilience.md](resilience.md) | WebSocket reconnection, tool timeouts, resource leaks |
| [error-handling.md](error-handling.md) | Silent failures, async void, fire-and-forget patterns |
| [configuration.md](configuration.md) | Mutable settings, mid-session changes, bootstrap size |
| [performance.md](performance.md) | MemoryStore I/O, SessionContext history walking, startup cost |
| [proposals.md](proposals.md) | Concrete improvement proposals with priority and effort |
