# M9 — Multi-Provider Architecture Design

## Overview

Extensible provider architecture using **MAF (Microsoft Agent Framework)** patterns to support multiple real-time voice LLM providers. The existing `IRealtimeClient` interface becomes the provider contract, and provider-specific implementations are registered via DI with a factory pattern for runtime switching.

---

## Current Architecture (Before M9)

```
AppSettings.Provider (enum: OpenAi | Azure)
        │
        ▼
┌──────────────────────────┐
│     RealtimeClient       │  ← Single class handles both OpenAI + Azure
│  (WebSocket, JSON parse, │     via conditional logic in ConnectAsync()
│   event dispatch)        │
└──────────────────────────┘
        │
        ▼
┌──────────────────────────┐
│   AgentOrchestrator      │  ← Directly depends on IRealtimeClient
│   (event routing,        │
│    tool dispatch)        │
└──────────────────────────┘
```

**Problems:**
1. `RealtimeClient` has provider-specific logic mixed in (auth headers, URL construction)
2. Adding a new provider (Gemini) would require modifying `RealtimeClient` — violates Open/Closed Principle
3. Gemini uses a fundamentally different JSON protocol — can't be handled with conditionals
4. No way to hot-swap providers at runtime

---

## Target Architecture (M9)

```
┌─────────────────────────────────────────────────────────┐
│                    IRealtimeClient                       │
│  (unchanged interface — the provider contract)          │
└────────┬────────────────┬───────────────────┬───────────┘
         │                │                   │
         ▼                ▼                   ▼
┌─────────────┐  ┌────────────────┐  ┌────────────────────┐
│ OpenAi      │  │ Gemini         │  │ Composite          │
│ Realtime    │  │ LiveRealtime   │  │ RealtimeClient     │
│ Client      │  │ Client         │  │ (STT→LLM→TTS)     │
│             │  │                │  │ [Future]           │
└─────────────┘  └────────────────┘  └────────────────────┘
         │                │                   │
         ▼                ▼                   ▼
┌─────────────────────────────────────────────────────────┐
│              RealtimeClientFactory                       │
│  Creates the right IRealtimeClient based on             │
│  AppSettings.Provider at runtime                        │
└─────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────┐
│              AgentOrchestrator                           │
│  (unchanged — works with any IRealtimeClient)           │
└─────────────────────────────────────────────────────────┘
```

---

## Design Decisions

### 1. Keep `IRealtimeClient` as the Provider Contract

The existing interface is already well-designed and provider-agnostic:

```csharp
public interface IRealtimeClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    bool IsConnected { get; }

    Task SendAudioChunkAsync(byte[] pcm16Data, CancellationToken ct = default);
    Task CommitAudioBufferAsync(CancellationToken ct = default);
    Task CreateResponseAsync(CancellationToken ct = default);
    Task CancelResponseAsync(CancellationToken ct = default);
    Task TruncateResponseAudioAsync(string itemId, int audioEndMs, CancellationToken ct = default);
    Task UpdateSessionAsync(CancellationToken ct = default);
    Task SendTextInputAsync(string text, CancellationToken ct = default);
    Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken ct = default);

    event EventHandler<byte[]>? AudioDelta;
    event EventHandler<string>? OutputTranscriptDelta;
    event EventHandler<string>? OutputTranscriptCompleted;
    event EventHandler<string>? InputTranscriptCompleted;
    event EventHandler? SpeechStarted;
    event EventHandler? SpeechStopped;
    event EventHandler<RealtimeResponseInfo>? ResponseDone;
    event EventHandler<string>? ErrorOccurred;
    event EventHandler<string>? OutputItemAdded;
    event EventHandler<FunctionCallInfo>? FunctionCallReceived;
}
```

Every method and event maps naturally to Gemini's capabilities. No interface changes needed.

### 2. Expand the Provider Enum

```csharp
public enum RealtimeProvider
{
    OpenAi,          // Direct OpenAI API
    AzureOpenAi,     // Azure-hosted OpenAI (same protocol, different auth)
    Gemini,          // Google Gemini Live API
    // Future:
    // Anthropic,    // When Claude voice API matures
    // Composite,    // STT→LLM→TTS pipeline for any text-only provider
}
```

**Breaking change:** Rename `OpenAiProvider` → `RealtimeProvider` and split `Azure` into explicit `AzureOpenAi`. This is cleaner and allows each provider to have distinct settings.

### 3. Provider-Specific Settings

```csharp
public class AppSettings
{
    // Provider selection
    public RealtimeProvider Provider { get; set; } = RealtimeProvider.OpenAi;

    // --- Shared settings (all providers) ---
    public string Voice { get; set; } = "marin";
    public string SystemInstructions { get; set; } = "...";
    public int SampleRate { get; set; } = 24000;
    public int ChunkDurationMs { get; set; } = 50;

    // --- OpenAI settings ---
    public string RealtimeModel { get; set; } = "gpt-realtime-1.5";
    public string RealtimeApiEndpoint { get; set; } = "wss://api.openai.com/v1/realtime";

    // --- Azure OpenAI settings ---
    public string? AzureEndpoint { get; set; }
    public string? AzureRealtimeDeploymentName { get; set; }
    public string AzureApiVersion { get; set; } = "2025-04-01-preview";

    // --- Gemini settings ---
    public string GeminiModel { get; set; } = "gemini-3.1-flash-live-preview";
    public string GeminiApiEndpoint { get; set; } = "wss://generativelanguage.googleapis.com/ws";
    public string GeminiVoice { get; set; } = "Kore";

    // --- Chat/Vision (non-realtime, shared) ---
    public string ChatModel { get; set; } = "gpt-5.4-mini";
    public string VisionModel { get; set; } = "gpt-5.4";
}
```

### 4. Factory Pattern for Provider Creation

```csharp
public interface IRealtimeClientFactory
{
    IRealtimeClient Create(RealtimeProvider provider);
}

public class RealtimeClientFactory : IRealtimeClientFactory
{
    private readonly IServiceProvider _sp;

    public RealtimeClientFactory(IServiceProvider sp) => _sp = sp;

    public IRealtimeClient Create(RealtimeProvider provider) => provider switch
    {
        RealtimeProvider.OpenAi => ActivatorUtilities.CreateInstance<OpenAiRealtimeClient>(_sp),
        RealtimeProvider.AzureOpenAi => ActivatorUtilities.CreateInstance<OpenAiRealtimeClient>(_sp),
        RealtimeProvider.Gemini => ActivatorUtilities.CreateInstance<GeminiRealtimeClient>(_sp),
        _ => throw new NotSupportedException($"Provider {provider} not supported")
    };
}
```

Note: `OpenAiRealtimeClient` handles both `OpenAi` and `AzureOpenAi` — the only difference is auth headers and URL construction, which can stay as internal conditionals since they share the same WebSocket protocol.

### 5. DI Registration

```csharp
// In MauiProgram.cs
builder.Services.AddSingleton<IRealtimeClientFactory, RealtimeClientFactory>();

// Register as transient via factory (orchestrator gets a new client per session)
builder.Services.AddTransient<IRealtimeClient>(sp =>
{
    var factory = sp.GetRequiredService<IRealtimeClientFactory>();
    var settings = sp.GetRequiredService<AppSettings>();
    return factory.Create(settings.Provider);
});
```

---

## Provider Implementations

### OpenAiRealtimeClient (Refactored from existing `RealtimeClient`)

Minimal changes from current implementation:
- Rename class: `RealtimeClient` → `OpenAiRealtimeClient`
- Move to `Services/Realtime/Providers/` folder
- Extract shared WebSocket infrastructure to `RealtimeClientBase` if useful

```
Services/
  Realtime/
    Providers/
      OpenAiRealtimeClient.cs       ← renamed from RealtimeClient.cs
      GeminiRealtimeClient.cs       ← new
    RealtimeMessages.cs             ← OpenAI-specific messages (rename for clarity)
    GeminiMessages.cs               ← new: Gemini-specific messages
    RealtimeJsonContext.cs          ← OpenAI JSON source gen
    GeminiJsonContext.cs            ← new: Gemini JSON source gen
    ServerEventParser.cs            ← OpenAI-specific (rename for clarity)
    GeminiEventParser.cs            ← new
  IRealtimeClient.cs                ← unchanged
  RealtimeClientFactory.cs          ← new
```

### GeminiRealtimeClient (New)

Maps Gemini Live API protocol to `IRealtimeClient` interface:

```csharp
public class GeminiRealtimeClient : IRealtimeClient
{
    private readonly IApiKeyService _apiKeyService;
    private readonly AppSettings _settings;
    private readonly ToolDispatcher _dispatcher;
    private ClientWebSocket? _ws;

    // --- IRealtimeClient implementation ---

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var apiKey = await _apiKeyService.GetApiKeyAsync();
        _ws = new ClientWebSocket();

        // Gemini WebSocket URL includes model and API key
        var uri = new Uri(
            $"{_settings.GeminiApiEndpoint}" +
            $"/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent" +
            $"?key={apiKey}");

        await _ws.ConnectAsync(uri, ct);

        // Send setup message (equivalent to session.update)
        await SendSetupMessageAsync(ct);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
        IsConnected = true;
    }

    public async Task SendAudioChunkAsync(byte[] pcm16Data, CancellationToken ct = default)
    {
        // Gemini expects: { "realtimeInput": { "mediaChunks": [{ "data": base64, "mimeType": "audio/pcm;rate=24000" }] } }
        var msg = new GeminiRealtimeInputMessage
        {
            RealtimeInput = new GeminiRealtimeInput
            {
                MediaChunks = [new GeminiMediaChunk
                {
                    Data = Convert.ToBase64String(pcm16Data),
                    MimeType = $"audio/pcm;rate={_settings.SampleRate}"
                }]
            }
        };
        await SendJsonAsync(msg, ct);
    }

    public Task UpdateSessionAsync(CancellationToken ct = default)
    {
        // Gemini doesn't support mid-session config updates the same way
        // Session config is sent once during setup
        return Task.CompletedTask;
    }

    // ... other IRealtimeClient methods mapped to Gemini protocol
}
```

**Key protocol differences handled internally:**

| Aspect | OpenAI | Gemini (handled in GeminiRealtimeClient) |
|---|---|---|
| Setup | `session.update` JSON | `BidiGenerateContentSetup` JSON |
| Send audio | `input_audio_buffer.append` + base64 | `realtimeInput.mediaChunks[]` + base64 |
| Receive audio | `response.audio.delta` → base64 decode | `serverContent.modelTurn.parts[].inlineData` → base64 decode |
| Transcript out | `response.audio_transcript.delta` | `serverContent.outputTranscription.text` |
| Transcript in | `conversation.item.input_audio_transcription.completed` | `serverContent.inputTranscription.text` |
| VAD speech start | `input_audio_buffer.speech_started` | `serverContent.interrupted` (interruption signal) |
| Function call | Parsed from `response.done` output items | `toolCall` in server content |
| Function result | `conversation.item.create` with function_call_output | `toolResponse` message |
| Commit audio | `input_audio_buffer.commit` | Not needed (auto-committed via VAD) |
| Truncate | `conversation.item.truncate` | Not directly supported (handle via interruption) |

---

## Gemini-Specific Advantages to Expose

### Vision in Realtime Session

Gemini Live API accepts JPEG frames directly in the voice session — no separate VisionAgent needed. This is a major advantage for BodyCam.

```csharp
// Add to IRealtimeClient (optional capability)
public interface IRealtimeClient : IAsyncDisposable
{
    // ... existing members ...

    /// <summary>
    /// Whether this provider supports sending image frames inline with audio.
    /// </summary>
    bool SupportsInlineVision { get; }

    /// <summary>
    /// Send an image frame to the realtime session (if supported).
    /// </summary>
    Task SendImageFrameAsync(byte[] jpegData, CancellationToken ct = default);
}
```

- `OpenAiRealtimeClient.SupportsInlineVision` → `false` (uses separate VisionAgent)
- `GeminiRealtimeClient.SupportsInlineVision` → `true` (sends frames in session)

The `AgentOrchestrator` can check this capability and route vision accordingly:

```csharp
// In AgentOrchestrator
if (_realtime.SupportsInlineVision && frame != null)
{
    await _realtime.SendImageFrameAsync(frame, ct);
}
else
{
    // Fallback: use separate VisionAgent + ChatCompletions
    var description = await _vision.DescribeAsync(frame, ct);
    session.AddVisionContext(description);
}
```

---

## API Key Management

Each provider needs its own API key. Extend `IApiKeyService`:

```csharp
public interface IApiKeyService
{
    // Existing (used for current provider)
    Task<string?> GetApiKeyAsync();
    Task SetApiKeyAsync(string key);
    Task ClearApiKeyAsync();
    Task<bool> ValidateApiKeyAsync();

    // New: provider-specific
    Task<string?> GetApiKeyAsync(RealtimeProvider provider);
    Task SetApiKeyAsync(RealtimeProvider provider, string key);
}
```

Storage keys:
- `openai_api_key` (existing)
- `azure_api_key` (existing, shared with OpenAI Azure)
- `gemini_api_key` (new)

The parameterless `GetApiKeyAsync()` continues to return the key for the currently active provider.

---

## Settings UI Changes

The Settings page needs:
1. **Provider picker** — dropdown with OpenAI / Azure OpenAI / Gemini
2. **Provider-specific sections** — show/hide settings based on selected provider
3. **API key entry** — per-provider key fields
4. **Model selection** — provider-specific model lists
5. **Voice selection** — provider-specific voice lists

```csharp
// In ModelOptions.cs — extend for multi-provider
public static class ModelOptions
{
    public static string[] GetRealtimeModels(RealtimeProvider provider) => provider switch
    {
        RealtimeProvider.OpenAi or RealtimeProvider.AzureOpenAi =>
            ["gpt-realtime-1.5", "gpt-4o-realtime-preview"],
        RealtimeProvider.Gemini =>
            ["gemini-3.1-flash-live-preview", "gemini-2.5-flash-native-audio-preview"],
        _ => []
    };

    public static string[] GetVoices(RealtimeProvider provider) => provider switch
    {
        RealtimeProvider.OpenAi or RealtimeProvider.AzureOpenAi =>
            ["alloy", "ash", "ballad", "coral", "echo", "marin", "sage", "shimmer", "verse"],
        RealtimeProvider.Gemini =>
            ["Kore", "Charon", "Fenrir", "Aoede", "Puck", "Leda"],
        _ => []
    };
}
```

---

## File Structure (New/Modified)

```
Services/
  IRealtimeClient.cs                    ← ADD: SupportsInlineVision, SendImageFrameAsync
  IApiKeyService.cs                     ← ADD: provider-specific overloads
  RealtimeClientFactory.cs              ← NEW: factory for provider selection
  Realtime/
    Providers/
      OpenAiRealtimeClient.cs           ← RENAME from RealtimeClient.cs (minimal changes)
      GeminiRealtimeClient.cs           ← NEW: full Gemini Live implementation
    OpenAi/
      OpenAiMessages.cs                 ← RENAME from RealtimeMessages.cs
      OpenAiJsonContext.cs              ← RENAME from RealtimeJsonContext.cs
      OpenAiEventParser.cs              ← RENAME from ServerEventParser.cs
    Gemini/
      GeminiMessages.cs                 ← NEW: Gemini protocol messages
      GeminiJsonContext.cs              ← NEW: source-gen JSON
      GeminiEventParser.cs              ← NEW: Gemini server event parsing

Models/
  RealtimeModels.cs                     ← unchanged

AppSettings.cs                          ← ADD: Gemini settings, rename enum

Orchestration/
  AgentOrchestrator.cs                  ← MODIFY: vision routing based on SupportsInlineVision

MauiProgram.cs                          ← MODIFY: factory-based DI registration
```

---

## Migration Plan

### Phase 1: Refactor (Non-breaking)
1. Rename `OpenAiProvider` → `RealtimeProvider` (add `Gemini` value)
2. Rename `RealtimeClient` → `OpenAiRealtimeClient`
3. Move OpenAI-specific files to `Services/Realtime/OpenAi/` subfolder
4. Add `RealtimeClientFactory` with factory DI registration
5. Add `SupportsInlineVision` + `SendImageFrameAsync` to `IRealtimeClient`
6. **Verify:** All existing tests pass, OpenAI + Azure still work

### Phase 2: Gemini Implementation
7. Add Gemini settings to `AppSettings` + `ISettingsService`
8. Create `GeminiMessages.cs`, `GeminiJsonContext.cs`, `GeminiEventParser.cs`
9. Implement `GeminiRealtimeClient`
10. Add Gemini API key support to `IApiKeyService` / `ApiKeyService`
11. **Verify:** Connect to Gemini Live, voice round-trip works

### Phase 3: Integration
12. Update `ModelOptions` for provider-specific models/voices
13. Update Settings UI with provider picker and conditional sections
14. Update `AgentOrchestrator` for inline vision routing
15. **Verify:** Full end-to-end with both providers, settings persist correctly

### Phase 4: Tests
16. Unit tests for `GeminiRealtimeClient` (mock WebSocket)
17. Unit tests for `RealtimeClientFactory`
18. Integration tests for Gemini connection (real API)
19. Update existing tests for renamed classes

---

## Gemini WebSocket Protocol Reference

### Connection

```
wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent?key={API_KEY}
```

### Setup Message (sent immediately after connect)

```json
{
  "setup": {
    "model": "models/gemini-3.1-flash-live-preview",
    "generationConfig": {
      "responseModalities": ["AUDIO"],
      "speechConfig": {
        "voiceConfig": {
          "prebuiltVoiceConfig": {
            "voiceName": "Kore"
          }
        }
      }
    },
    "systemInstruction": {
      "parts": [{ "text": "You are BodyCam..." }]
    },
    "tools": [
      {
        "functionDeclarations": [
          {
            "name": "describe_scene",
            "description": "...",
            "parameters": { "type": "OBJECT", "properties": { ... } }
          }
        ]
      }
    ],
    "realtimeInputConfig": {
      "automaticActivityDetection": {
        "disabled": false,
        "startOfSpeechSensitivity": "START_SENSITIVITY_MEDIUM",
        "endOfSpeechSensitivity": "END_SENSITIVITY_MEDIUM"
      }
    },
    "outputAudioTranscription": {},
    "inputAudioTranscription": {}
  }
}
```

### Send Audio

```json
{
  "realtimeInput": {
    "mediaChunks": [
      {
        "data": "<base64-pcm16>",
        "mimeType": "audio/pcm;rate=24000"
      }
    ]
  }
}
```

### Send Image Frame

```json
{
  "realtimeInput": {
    "mediaChunks": [
      {
        "data": "<base64-jpeg>",
        "mimeType": "image/jpeg"
      }
    ]
  }
}
```

### Receive Audio + Transcript (server → client)

```json
{
  "serverContent": {
    "modelTurn": {
      "parts": [
        {
          "inlineData": {
            "data": "<base64-pcm16-24khz>",
            "mimeType": "audio/pcm;rate=24000"
          }
        }
      ]
    }
  }
}
```

```json
{
  "serverContent": {
    "outputTranscription": {
      "text": "Hello, how can I help?"
    }
  }
}
```

### Tool Call (server → client)

```json
{
  "toolCall": {
    "functionCalls": [
      {
        "id": "call_123",
        "name": "describe_scene",
        "args": { "detail_level": "brief" }
      }
    ]
  }
}
```

### Tool Response (client → server)

```json
{
  "toolResponse": {
    "functionResponses": [
      {
        "id": "call_123",
        "name": "describe_scene",
        "response": { "result": "A park with trees and a bench." }
      }
    ]
  }
}
```

### Interruption (server → client)

```json
{
  "serverContent": {
    "interrupted": true
  }
}
```

---

## Risk Assessment

| Risk | Mitigation |
|---|---|
| Gemini Live API is in Preview — may change | Pin to specific model version, abstract behind our interface |
| No .NET SDK for Gemini Live | Raw WebSocket is fine — we already do this for OpenAI |
| Audio sample rate mismatch (16kHz vs 24kHz) | Gemini accepts any rate via mime type; send 24kHz |
| Gemini function calling schema differs from OpenAI | Map in GeminiMessages.cs — `ToolDefinition` → `FunctionDeclaration` |
| Session duration limits (15 min) | Implement auto-reconnect with session context replay |
| API key in WebSocket URL (not header) | Gemini's documented pattern; use ephemeral tokens for production |
