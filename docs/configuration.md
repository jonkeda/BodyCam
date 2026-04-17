# Configuration

## Providers

BodyCam supports two OpenAI providers, selected via the Settings page or `.env` file:

| Provider | Value | Description |
|----------|-------|-------------|
| OpenAI | `openai` | Direct OpenAI API (default) |
| Azure OpenAI | `azure` | Azure-hosted models with deployment names |

## .env File (Development)

Create a `.env` file in the solution root. The app reads it at startup via `DotEnvReader`.

### OpenAI (default)

```env
OPENAI_PROVIDER=openai
OPENAI_API_KEY=sk-proj-your-key-here
```

### Azure OpenAI

```env
OPENAI_PROVIDER=azure
AZURE_OPENAI_API_KEY=your-azure-key-here
AZURE_OPENAI_RESOURCE=my-openai-eastus2
AZURE_OPENAI_DEPLOYMENT=bodycam-realtime
AZURE_OPENAI_CHAT_DEPLOYMENT=bodycam-chat
AZURE_OPENAI_VISION_DEPLOYMENT=bodycam-vision
AZURE_OPENAI_TRANSCRIPTION_DEPLOYMENT=bodycam-transcribe
AZURE_OPENAI_API_VERSION=2025-04-01-preview
```

## AppSettings

`AppSettings` is the runtime configuration object, registered as a singleton. It holds all model, voice, provider, and audio settings. Key methods:

- `GetRealtimeUri()` — WebSocket URI for the Realtime API (OpenAI or Azure)
- `GetChatUri()` — HTTP endpoint for Chat Completions
- `GetVisionUri()` — HTTP endpoint for Vision

## Settings Page

The in-app Settings page (`SettingsViewModel`) persists preferences via `ISettingsService` (backed by MAUI `Preferences`).

### Model Selection

| Role | Setting | Default |
|------|---------|---------|
| Voice (Realtime) | `RealtimeModel` | See `ModelOptions.DefaultRealtime` |
| Chat | `ChatModel` | See `ModelOptions.DefaultChat` |
| Vision | `VisionModel` | See `ModelOptions.DefaultVision` |
| Transcription | `TranscriptionModel` | See `ModelOptions.DefaultTranscription` |

Available models are defined in `ModelOptions.cs` as static arrays.

### Voice Settings

- **Voice** — TTS voice selection (alloy, echo, shimmer, etc.)
- **Turn Detection** — VAD mode (server_vad, etc.)
- **Noise Reduction** — Audio preprocessing

### Azure Settings

When provider is `azure`:
- **Endpoint** — Azure resource URL
- **API Version** — API version string
- **Deployment names** — per-role deployment names (realtime, chat, vision)

### Debug Settings

- `DebugMode` — show debug panel
- `ShowTokenCounts` — display token usage
- `ShowCostEstimate` — display cost tracking

### Tool Settings

Each tool that implements `IToolSettings` exposes configurable parameters (e.g., `FindObjectTool` scan interval and timeout). These appear as per-tool sections on the Settings page.

### API Key

- Stored in MAUI `SecureStorage` (encrypted, per-platform)
- Can be entered/changed/cleared from the Settings page
- **Test Connection** button validates the key against the API
- Fallback chain: SecureStorage → `.env` file → environment variables

## API Key Security

- Keys are never logged (only last 4 chars shown)
- Stored encrypted via platform secure storage
- Passed via `Authorization` header on WebSocket connections
- `.env` file should be in `.gitignore`

## Azure Budget Setup

For detailed Azure OpenAI setup with budget-friendly model picks, see [azure-budget-setup.md](azure-budget-setup.md).
