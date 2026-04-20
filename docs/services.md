# Services

Platform services are abstracted behind interfaces for testability and cross-platform support. All are registered as singletons in `MauiProgram.cs`.

## RealtimeClient

**Interface:** `IRealtimeClient` (from `Microsoft.Extensions.AI`)
**Implementation:** `OpenAIRealtimeClient` (wraps OpenAI SDK's `RealtimeClient`)
**Registration:** Singleton in `ServiceExtensions.cs`

MAF abstraction over the OpenAI Realtime WebSocket API. Handles both OpenAI and Azure OpenAI endpoints.

**Session lifecycle:**
- `CreateSessionAsync(RealtimeSessionOptions, ct)` — opens WebSocket, returns `IRealtimeClientSession`
- Session options include model, voice, tools, transcription config, VAD settings
- Session is `IAsyncDisposable` — disposed when the orchestrator stops

**Audio (via session):**
- `session.SendAsync(InputAudioBufferAppendRealtimeClientMessage)` — sends PCM audio
- `session.SendAsync(CreateConversationItemRealtimeClientMessage)` — injects text input
- `session.SendAsync(CreateResponseRealtimeClientMessage)` — triggers a response

**Events (via session):**
- `session.GetStreamingResponseAsync(ct)` → `IAsyncEnumerable<RealtimeServerMessage>`
- Message types: `OutputAudioDelta`, `OutputAudioTranscriptionDelta`, `InputAudioTranscriptionCompleted`, `ResponseDone`, `Error`, etc.

**Tool dispatch:**
- MAF does not have `FunctionInvokingRealtimeClient` — tool dispatch is manual
- The orchestrator accesses `RawRepresentation` (SDK `RealtimeFunctionCallItem`) to extract function call details
- Results are sent back via SDK's `RealtimeItem.CreateFunctionCallOutputItem`

**Azure support:**
- `AzureRealtimeClient` subclasses `OpenAI.Realtime.RealtimeClient` to inject the `api-key` header
- GA endpoint: `wss://{resource}.cognitiveservices.azure.com/openai/v1/realtime`
- Wrapped in `OpenAIRealtimeClient` for MAF interface
- `InputTranscriptCompleted` — finalized user speech transcript
- `SpeechStarted` / `SpeechStopped` — VAD events
- `FunctionCallReceived` — tool invocation request
- `ResponseDone` — response complete
- `ErrorOccurred` — API error

**Serialization:** `Services/Realtime/` contains source-generated `JsonSerializerContext` and message types for the Realtime API wire protocol. `ServerEventParser` does lightweight string-based JSON extraction for server events.

## Audio Services

### IAudioInputService / AudioInputService

Captures PCM audio from the platform microphone (24kHz, 16-bit mono, 50ms chunks).

- `StartAsync(ct)` / `StopAsync()` — lifecycle
- `AudioChunkAvailable` event — fires with `byte[]` PCM data
- Platform-specific implementations in `Platforms/`

### IAudioOutputService / AudioOutputService

Plays PCM audio to the platform speaker.

- `StartAsync()` / `StopAsync()` — lifecycle
- `PlayChunkAsync(pcmData, ct)` — enqueues a PCM chunk
- `ClearBuffer()` — flushes on interruption

## Camera

### ICameraService / CameraService

Camera lifecycle management. The actual frame capture is handled by MAUI's `CameraView` control — `MainViewModel.CaptureFrameFromCameraViewAsync()` captures JPEG frames on demand.

## API Key Management

### IApiKeyService / ApiKeyService

Secure API key storage with a fallback chain:

1. MAUI `SecureStorage` (encrypted, persisted)
2. `.env` file (`AZURE_OPENAI_API_KEY` or `OPENAI_API_KEY`)
3. Environment variables

- `GetApiKeyAsync()` — resolves key from the chain, caches result
- `SetApiKeyAsync(key)` — stores in SecureStorage
- `ClearApiKeyAsync()` — removes from SecureStorage
- `HasKey` — quick check

## Settings

### ISettingsService / SettingsService

Persists user preferences via MAUI `Preferences` (platform key-value store).

**Model settings:** `RealtimeModel`, `ChatModel`, `VisionModel`, `TranscriptionModel`
**Voice settings:** `Voice`, `TurnDetection`, `NoiseReduction`
**Provider settings:** `Provider` (OpenAi/Azure), `AzureEndpoint`, deployment names, `AzureApiVersion`
**Debug settings:** `DebugMode`, `ShowTokenCounts`, `ShowCostEstimate`
**System:** `SystemInstructions`

## Microphone Coordinator

### IMicrophoneCoordinator / MicrophoneCoordinator

Manages mutual exclusion between wake word detection and active session mic capture. Only one listener can use the mic at a time.

- `TransitionToActiveSessionAsync()` — stops wake word, starts session mic
- `TransitionToWakeWordAsync()` — stops session mic, starts wake word (50ms release delay)

## Wake Word Detection

### IWakeWordService / NullWakeWordService

Interface for always-on keyword detection. Currently a no-op stub (`NullWakeWordService`). Designed for Porcupine integration.

- `StartAsync(ct)` / `StopAsync()` — lifecycle
- `RegisterKeywords(entries)` — configures keywords with actions
- `WakeWordDetected` event — fires with action type (`StartSession`, `GoToSleep`, `InvokeTool`)

## Memory Store

### MemoryStore

JSON file-based persistent memory for the `save_memory` / `recall_memory` tools.

- `SaveAsync(entry)` — appends a `MemoryEntry` (id, content, category, timestamp)
- `SearchAsync(query)` — case-insensitive keyword search
- `GetRecentAsync(count)` — latest N entries

## Utilities

- **DotEnvReader** — reads key-value pairs from `.env` file or falls back to environment variables
- **PlatformHelper** — returns platform suffix ("android", "windows") and maps wake word keyword paths
