# BodyCam

An open-source smart glasses platform — a DIY alternative to other AI Glasses.

Pair cheaper Bluetooth camera glasses with a .NET MAUI companion app that acts as the brain. The app captures audio and video from the glasses, routes it through OpenAI's APIs for speech recognition, reasoning, and vision understanding, then speaks responses back through the glasses' speakers.

## How It Works

```
Smart Glasses (mic + camera + speakers)
        │ Bluetooth
        ▼
  .NET MAUI App (Android / Windows)
        │
        ├─ VoiceInputAgent ── mic audio ──► OpenAI Realtime API ──► transcript
        │
        ├─ ConversationAgent ── transcript ──► GPT Chat Completions ──► reply
        │
        ├─ VisionAgent ── camera frames ──► GPT Vision ──► scene description
        │
        └─ VoiceOutputAgent ── reply text ──► TTS ──► speakers
```

The app supports two conversation modes:

- **Realtime mode** — The OpenAI Realtime API handles the full loop (audio in → reasoning → audio out) in a single WebSocket connection. Simple and low-latency.
- **Separated mode** — The Realtime API only does speech-to-text. Transcripts are routed through a `ConversationAgent` that calls GPT Chat Completions for reasoning, then sends the reply back for TTS. This gives you control over the model, system prompt, conversation history, vision context injection, and tool use.

## Tech Stack

| Layer    | Technology                                            |
| -------- | ----------------------------------------------------- |
| App      | .NET MAUI (Android + Windows)                         |
| AI       | OpenAI Realtime API, GPT Chat Completions, GPT Vision |
| Audio    | NAudio (Windows), AudioRecord/AudioTrack (Android)    |
| Language | C# / .NET 10                                          |

## Hardware

**Target:** Any Bluetooth camera glasses (tested with TKYUAN BT5.3 from Alibaba — ~$30).

**For development:** Works with just a laptop mic/webcam or phone. No glasses required.

## Project Structure

```
src/
  BodyCam/              # MAUI app
    Agents/             # VoiceInput, VoiceOutput, Conversation, Vision agents
    Models/             # SessionContext, TranscriptEntry, RealtimeModels
    Orchestration/      # AgentOrchestrator (dual-mode pipeline)
    Services/           # Audio I/O, Realtime WebSocket client, Camera, Settings
    ViewModels/         # MainViewModel, SettingsViewModel
    Platforms/          # Windows and Android platform implementations
  BodyCam.Tests/        # Unit tests
  BodyCam.RealTests/    # Integration tests against live Azure OpenAI
```

## Getting Started

1. Clone the repo
2. Copy `.env.example` to `.env` and add your OpenAI or Azure OpenAI API key
3. Open `BodyCam.sln` in Visual Studio or Rider
4. Build and run on Windows (`net10.0-windows10.0.19041.0`) or Android

## Configuration

API keys and settings are configured via `.env` file (dev) or the in-app Settings page:

```env
OPENAI_PROVIDER=openai
OPENAI_API_KEY=sk-proj-your-key-here
```

For Azure OpenAI:

```env
OPENAI_PROVIDER=azure
AZURE_OPENAI_API_KEY=your-key
AZURE_OPENAI_ENDPOINT=https://your-resource.cognitiveservices.azure.com
AZURE_OPENAI_DEPLOYMENT=my-realtime-deployment
AZURE_OPENAI_CHAT_DEPLOYMENT=my-chat-deployment
```

## License

Open source. See LICENSE file for details.
