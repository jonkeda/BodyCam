# 01 — BodyCam Overview

## What It Is

BodyCam is a .NET MAUI companion app for cheap Bluetooth camera glasses (~$30 Alibaba). The app runs on Android or Windows and acts as the brain — the glasses just provide mic, camera, and speakers over Bluetooth.

## Core Loop

```
Smart Glasses (mic + camera + speakers)
    ↓ Bluetooth
.NET MAUI App (Android / Windows)
    ├─ VoiceInputAgent  → captures mic PCM → sends to OpenAI Realtime API
    ├─ Realtime API     → transcribes speech, reasons, generates response
    ├─ VoiceOutputAgent → plays response audio through speakers
    └─ VisionAgent      → sends camera frames to GPT Vision for scene understanding
```

The user speaks → the AI hears, optionally sees (via camera), thinks, and responds with voice — all in real time over a single WebSocket.

## Two Modes

| Mode | How It Works | When Used |
|------|-------------|-----------|
| **Realtime** | Full voice loop over one WebSocket: speech-to-text → reasoning → TTS | Default when Active session starts |
| **Separated** | Realtime for transcription only; transcripts routed through ConversationAgent for custom prompts, tool use, vision injection | Deep analysis, tool-heavy queries |

## Listening Layers

The app has three states, controlled by the status bar buttons (😴 👂 💬):

| Layer | Enum | What Happens |
|-------|------|-------------|
| **Sleep** | `ListeningLayer.Sleep` | Nothing runs. Idle. |
| **WakeWord** | `ListeningLayer.WakeWord` | Porcupine listens for wake words ("Hey BodyCam"). No API connection. |
| **Active** | `ListeningLayer.ActiveSession` | Full Realtime WebSocket session. Mic → API → speakers. Camera available for vision tools. |

Transitions happen via `SetLayerAsync("Sleep" / "Listen" / "Active")` in `MainViewModel`. Escalating to Active creates the Realtime session; de-escalating tears it down.

## Providers

Supports two backends, switchable in Settings:

- **OpenAI** — Direct API (default)
- **Azure OpenAI** — Custom endpoint, deployment names, API version

The `IRealtimeClient` is built at DI time from `AppSettings.Provider`. MAF middleware adds function invocation and logging.

## Default Models

| Purpose | Model | Configurable |
|---------|-------|-------------|
| Realtime voice | `gpt-realtime-1.5` | Yes |
| Chat/reasoning | `gpt-5.4-mini` | Yes |
| Vision | `gpt-5.4` | Yes |
| Transcription | `gpt-4o-mini-transcribe` | Yes |

## Key Technologies

- **.NET 10 MAUI** — cross-platform UI (Android + Windows)
- **Microsoft.Extensions.AI (MAF)** — abstraction over OpenAI/Azure for chat, vision, realtime
- **OpenAI Realtime API** — WebSocket-based voice conversation
- **Porcupine** — on-device wake word detection
- **WebRTC APM** — echo cancellation between speaker output and mic input
- **CommunityToolkit.Maui CameraView** — camera preview and frame capture

## Project Layout

```
src/
  BodyCam/              ← Main MAUI app
    Agents/             ← VoiceInput, VoiceOutput, Conversation, Vision
    Converters/         ← XAML value converters
    Models/             ← TranscriptEntry, SessionContext, AppSettings, etc.
    Mvvm/               ← ViewModelBase, RelayCommand, AsyncRelayCommand
    Orchestration/      ← AgentOrchestrator (the brain)
    Pages/              ← MainPage, SetupPage, Settings sub-pages
    Platforms/           ← Windows + Android platform code
    Services/           ← Audio, Camera, API keys, Settings, Memory
    Tools/              ← 12+ AI-callable tools
    ViewModels/         ← MainViewModel, SetupViewModel, Settings VMs
  BodyCam.Tests/        ← Unit tests (xUnit + FluentAssertions)
  BodyCam.IntegrationTests/
  BodyCam.RealTests/    ← Tests requiring live API keys
  BodyCam.UITests/      ← Brinell/FlaUI UI tests
```
