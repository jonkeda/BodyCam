# 06 — Settings, Configuration, and Services

## Configuration Layers

Settings flow through three layers:

```
User changes setting in UI
  → ViewModel writes to ISettingsService (persisted to MAUI Preferences)
  → On session start, AgentOrchestrator reads ISettingsService → AppSettings
  → AppSettings used by agents, Realtime session options, API clients
```

Changes to models, voice, etc. take effect on next session start (not mid-session).

## AppSettings (Runtime Config)

**File:** `AppSettings.cs`

In-memory settings object populated at startup from `ISettingsService` + `.env` overrides.

| Category | Properties | Defaults |
|----------|-----------|----------|
| **Provider** | `Provider` | `OpenAi` |
| **Models** | `RealtimeModel`, `ChatModel`, `VisionModel`, `TranscriptionModel` | `gpt-realtime-1.5`, `gpt-5.4-mini`, `gpt-5.4`, `gpt-4o-mini-transcribe` (Azure auto-falls back to `whisper-1`) |
| **Voice** | `Voice`, `TurnDetection`, `NoiseReduction` | `marin`, `semantic_vad`, `near_field` |
| **System** | `SystemInstructions` | BodyCam persona prompt |
| **Azure** | `AzureEndpoint`, `AzureRealtimeDeploymentName`, `AzureChatDeploymentName`, `AzureVisionDeploymentName`, `AzureApiVersion` | null, `2024-10-01` (GA) |
| **Audio** | `SampleRate`, `ChunkDurationMs`, `AecEnabled` | 24000, 50, true |
| **Mic** | `MicReleaseDelayMs` | 50 |
| **Endpoints** | `RealtimeApiEndpoint`, `ChatApiEndpoint`, `TranscriptionApiEndpoint` | OpenAI defaults |

**URI builders:** `GetRealtimeUri()`, `GetChatUri()`, `GetVisionUri()` — construct full URIs with model/deployment params.

## ISettingsService (Persisted Prefs)

**File:** `Services/SettingsService.cs`
**Storage:** MAUI `Preferences` (platform key-value store)

Persists everything the user configures:
- All model selections
- Voice preset, turn detection, noise reduction
- Provider selection and Azure deployment names
- Debug flags (DebugMode, ShowTokenCounts, ShowCostEstimate)
- System instructions
- Active device providers (camera, mic, speaker)
- Picovoice access key
- Telemetry settings
- `SetupCompleted` flag

## IApiKeyService (Secure Key Storage)

**File:** `Services/ApiKeyService.cs`

Cascade lookup for API key:
1. In-memory cache (`_cachedKey`)
2. MAUI `SecureStorage` (persisted, encrypted)
3. `.env` file (`AZURE_OPENAI_API_KEY` or `OPENAI_API_KEY`)
4. Environment variable

Methods: `GetApiKeyAsync()`, `SetApiKeyAsync(key)`, `ClearApiKeyAsync()`, `HasKey`

## DotEnvReader

**File:** `Services/DotEnvReader.cs`

Reads `.env` files for dev convenience:
1. Check app data directory for `.env`
2. Walk up directory tree from app location to find `.env`
3. Parse `KEY=VALUE` lines
4. Fall back to `Environment.GetEnvironmentVariable()`

## MemoryStore (Persistent Memory)

**File:** `Services/MemoryStore.cs`

JSON-based persistent storage for user memories (used by SaveMemory/RecallMemory tools).

- `SaveAsync(entry)` — append `MemoryEntry` to JSON file
- `SearchAsync(query)` — text search across stored entries
- `GetRecentAsync(count)` — last N entries
- Thread-safe via `SemaphoreSlim`

Each `MemoryEntry`: `{ Id (GUID), Content, Category?, Timestamp }`

## Model Options (UI Dropdowns)

**File:** `ModelOptions.cs`

Static lists for settings UI pickers:

| Category | Options |
|----------|---------|
| Realtime | gpt-realtime-1.5, gpt-realtime-mini |
| Chat | gpt-5.4, gpt-5.4-mini, gpt-5.4-nano |
| Vision | gpt-5.4, gpt-5.4-mini |
| Transcription | gpt-4o-mini-transcribe, gpt-4o-transcribe, whisper-1 |
| Voices | alloy, ash, ballad, coral, echo, fable, marin, sage, shimmer, verse |
| Turn Detection | semantic_vad, server_vad |
| Noise Reduction | near_field, far_field |

## Settings UI

### ConnectionSettingsPage (ConnectionViewModel)
- Provider toggle: OpenAI ↔ Azure
- Model pickers for Realtime, Chat, Vision, Transcription
- Azure-specific: endpoint, deployment names, API version
- API key: masked display, toggle visibility, change, clear, test connection

### VoiceSettingsPage (VoiceViewModel)
- Voice preset picker (10 voices)
- Turn detection mode (semantic_vad / server_vad)
- Noise reduction mode (near_field / far_field)
- System instructions editor (multiline text)

### DeviceSettingsPage (DeviceViewModel)
- Camera provider picker (phone camera, Bluetooth cameras)
- Audio input provider picker (platform mic, Bluetooth mics)
- Audio output provider picker (platform speaker, Bluetooth speakers)
- Listens to `ProvidersChanged` events to refresh when devices connect/disconnect

### AdvancedSettingsPage (AdvancedViewModel)
- Debug mode toggle
- Show token counts, cost estimates
- Telemetry: diagnostic data, crash reports (Sentry), usage data
- Azure Monitor connection string
- Tool settings sections (loaded from tools implementing `IToolSettings`)

## DI Registration Summary

**`ServiceExtensions.cs`** registers everything:

| Method | What It Registers |
|--------|------------------|
| `AddAudioServices()` | Platform audio providers, AudioInputManager, AudioOutputManager, AecProcessor |
| `AddCameraServices()` | Platform camera, PhoneCameraProvider, CameraManager |
| `AddAgents()` | VoiceInputAgent, ConversationAgent, VoiceOutputAgent, VisionAgent |
| `AddTools()` | All 12+ ITool implementations, ToolDispatcher |
| `AddOrchestration()` | ButtonInputManager, WakeWordService, MicrophoneCoordinator, ApiKeyService, IRealtimeClient (with MAF middleware), AgentOrchestrator |
| `AddViewModels()` | All ViewModels and Pages (transient) |

**IRealtimeClient** is built at DI time:
1. Resolve `AppSettings.Provider`
2. Create `OpenAIRealtimeClient` (direct OpenAI or Azure)
3. Wrap with MAF pipeline: `.UseFunctionInvocation()` + `.UseLogging()`
4. Return as `IRealtimeClient`

**IChatClient** is built similarly for Chat Completions (vision + conversation).

## Startup Sequence

```
MauiProgram.CreateMauiApp()
  1. Create SettingsService (first — needed by everything)
  2. Load .env overrides
  3. Create AppSettings from SettingsService + env
  4. Register all services (audio, camera, agents, tools, orchestration, VMs)
  5. Build IChatClient + IRealtimeClient
  6. Register MemoryStore, analytics, logging

App.xaml.cs → AppShell
  7. Check SetupCompleted → SetupPage or MainPage
  8. MainPage constructor:
     - Wire CameraView to PhoneCameraProvider + MainViewModel
     - Subscribe to transcript scroll, snapshot, property changes
  9. MainPage.Loaded:
     - Initialize AudioInputManager + AudioOutputManager
     - Start ButtonInputManager
     - Scan Bluetooth devices
```
