# Architecture

BodyCam is a .NET MAUI app (Windows + Android) that pairs with Bluetooth camera glasses to provide an AI-powered voice and vision assistant. The phone/laptop runs the intelligence; the glasses provide sensors (mic, camera) and output (speakers).

## High-Level Data Flow

```
Smart Glasses (mic + camera + speakers)
        │ Bluetooth
        ▼
  .NET MAUI App
        │
        ├─ VoiceInputAgent ── mic PCM ──► OpenAI Realtime API (WebSocket)
        │                                         │
        │                              ◄── audio delta ──┘
        │                                         │
        ├─ AgentOrchestrator ── routes events ─────┘
        │       │
        │       ├── function_call ──► ToolDispatcher ──► ITool impl
        │       ├── audio delta ──► VoiceOutputAgent ──► speakers
        │       └── transcript ──► MainViewModel ──► UI
        │
        ├─ VisionAgent ── camera JPEG ──► GPT Vision (Chat Completions)
        │
        └─ ConversationAgent ── text ──► GPT Chat Completions
```

## Layers

### Agents (`Agents/`)

Four agents handle distinct responsibilities:

| Agent | Role |
|-------|------|
| `VoiceInputAgent` | Captures mic audio chunks, forwards to Realtime API |
| `VoiceOutputAgent` | Plays PCM audio deltas from the API, handles interruption |
| `ConversationAgent` | Deep analysis via Chat Completions (reasoning model) |
| `VisionAgent` | Sends JPEG frames to GPT Vision, returns descriptions |

### Orchestration (`Orchestration/`)

`AgentOrchestrator` is the central coordinator. It:
- Manages the Realtime API WebSocket session lifecycle
- Subscribes to all Realtime API events and routes them to agents
- Dispatches function calls to `ToolDispatcher`
- Coordinates mic handoff between wake word detection and active sessions
- Owns the `SessionContext` (conversation history, system prompt, vision context)

### Services (`Services/`)

Platform services abstracted behind interfaces:

| Interface | Purpose |
|-----------|---------|
| `IRealtimeClient` | WebSocket connection to OpenAI Realtime API |
| `IAudioInputService` | Platform mic capture (PCM 24kHz) |
| `IAudioOutputService` | Platform speaker playback |
| `ICameraService` | Camera lifecycle (CameraView handles frames natively) |
| `IApiKeyService` | Secure API key storage (SecureStorage → .env → env vars) |
| `ISettingsService` | Model/voice/provider preferences (MAUI Preferences) |
| `IWakeWordService` | Always-on keyword detection (currently `NullWakeWordService`) |
| `IMicrophoneCoordinator` | Mutual exclusion between wake word and active session mic use |

### Tools (`Tools/`)

Extensible tool framework invoked by the Realtime API's function calling:

- `ITool` interface with name, description, JSON schema, and execute method
- `ToolBase<TArgs>` — generic base that deserializes arguments
- `ToolDispatcher` — registry + dispatch by name
- `SchemaGenerator` — reflection-based JSON Schema from C# arg classes
- 13 tool implementations (vision, memory, phone, navigation, translation, etc.)

### Models (`Models/`)

- `SessionContext` — conversation history with token budget trimming
- `TranscriptEntry` — observable chat bubble (role, text, optional image)
- `RealtimeModels` — session config, response info, playback tracker, function call info
- `NotificationInfo` — phone notification data

### ViewModels (`ViewModels/`)

- `MainViewModel` — session state machine (Sleep → WakeWord → ActiveSession), transcript, camera, debug log
- `SettingsViewModel` — provider/model/voice configuration, API key management, connection testing, per-tool settings

### MVVM (`Mvvm/`)

Custom lightweight MVVM (not CommunityToolkit): `ObservableObject`, `ViewModelBase`, `RelayCommand`, `AsyncRelayCommand`.

## DI Registration

All wiring happens in `MauiProgram.cs`. Services, agents, tools, and view models are registered as singletons. Platform-specific implementations are registered conditionally. The `IChatClient` is created based on the active provider (OpenAI or Azure).

## Session Lifecycle

```
Sleep ──[wake word / button]──► WakeWord ──[detected]──► ActiveSession
  ▲                                                           │
  └────────────────[stop / timeout]───────────────────────────┘
```

1. **Sleep** — App idle, no mic capture
2. **WakeWord** — `IWakeWordService` listening for keywords
3. **ActiveSession** — Realtime API connected, full voice + vision loop active
