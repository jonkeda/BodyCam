# M1 Implementation — Step 2: Realtime Client Interface + Models

**Depends on:** Step 1 (IApiKeyService, AppSettings)
**Produces:** `IRealtimeClient` interface, event arg models, `RealtimeSessionConfig`

---

## Why This Step?
Define the contract that all subsequent components program against. No implementation yet — just the interface and supporting types. This enables parallel development: audio services can be built against the interface while the WebSocket implementation proceeds.

---

## Tasks

### 2.1 — Add Realtime event models

**File:** `src/BodyCam/Models/RealtimeModels.cs`

```csharp
namespace BodyCam.Models;

/// <summary>
/// Configuration for a Realtime API session.
/// </summary>
public class RealtimeSessionConfig
{
    public string Model { get; set; } = "gpt-5.4-realtime";
    public string Voice { get; set; } = "marin";
    public string TurnDetection { get; set; } = "semantic_vad";
    public string NoiseReduction { get; set; } = "near_field";
    public string Instructions { get; set; } = "You are a helpful assistant.";
    public int SampleRate { get; set; } = 24000;
}

/// <summary>
/// Info returned when a Realtime response completes.
/// </summary>
public class RealtimeResponseInfo
{
    public required string ResponseId { get; set; }
    public string? ItemId { get; set; }
    public string? OutputTranscript { get; set; }
    public string? InputTranscript { get; set; }
}

/// <summary>
/// Tracks an audio item for interruption handling.
/// </summary>
public class AudioPlaybackTracker
{
    public string? CurrentItemId { get; set; }
    public int BytesPlayed { get; set; }
    public int SampleRate { get; set; } = 24000;
    public int BitsPerSample { get; set; } = 16;
    public int Channels { get; set; } = 1;

    /// <summary>Milliseconds of audio actually played.</summary>
    public int PlayedMs => SampleRate == 0 ? 0
        : (int)(BytesPlayed * 1000L / (SampleRate * (BitsPerSample / 8) * Channels));

    public void Reset()
    {
        CurrentItemId = null;
        BytesPlayed = 0;
    }
}
```

### 2.2 — Add `IRealtimeClient` interface

**File:** `src/BodyCam/Services/IRealtimeClient.cs`

```csharp
namespace BodyCam.Services;

/// <summary>
/// WebSocket client for OpenAI Realtime API.
/// Handles STT + reasoning + TTS in a single session.
/// </summary>
public interface IRealtimeClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    bool IsConnected { get; }

    // Audio streaming (mic → OpenAI)
    Task SendAudioChunkAsync(byte[] pcm16Data, CancellationToken ct = default);

    // Manual controls (push-to-talk when VAD disabled)
    Task CommitAudioBufferAsync(CancellationToken ct = default);
    Task CreateResponseAsync(CancellationToken ct = default);
    Task CancelResponseAsync(CancellationToken ct = default);

    // Interruption handling
    Task TruncateResponseAudioAsync(string itemId, int audioEndMs, CancellationToken ct = default);

    // Session management
    Task UpdateSessionAsync(CancellationToken ct = default);

    // Events — multiple concurrent streams from single WebSocket
    event EventHandler<byte[]>? AudioDelta;
    event EventHandler<string>? OutputTranscriptDelta;
    event EventHandler<string>? OutputTranscriptCompleted;
    event EventHandler<string>? InputTranscriptCompleted;
    event EventHandler? SpeechStarted;
    event EventHandler? SpeechStopped;
    event EventHandler<RealtimeResponseInfo>? ResponseDone;
    event EventHandler<string>? ErrorOccurred;
}
```

Note: uses `RealtimeResponseInfo` from `Models/RealtimeModels.cs`, so add `using BodyCam.Models;`.

### 2.3 — Remove old `IOpenAiStreamingClient`

The old interface (`IAsyncEnumerable`-based) is incompatible with the Realtime-first architecture. Replace it:

1. Delete `src/BodyCam/Services/IOpenAiStreamingClient.cs`
2. Delete `src/BodyCam/Services/OpenAiStreamingClient.cs`
3. Add a **stub** `RealtimeClient` that implements `IRealtimeClient` (no-ops, like the old stub)
4. Update `MauiProgram.cs`: replace `IOpenAiStreamingClient` → `IRealtimeClient` registration
5. Update all files that reference `IOpenAiStreamingClient` (agents, orchestrator)

**File:** `src/BodyCam/Services/RealtimeClient.cs` (stub)

```csharp
namespace BodyCam.Services;

/// <summary>
/// Stub Realtime client. WebSocket implementation in Step 5.
/// </summary>
public class RealtimeClient : IRealtimeClient
{
    private readonly IApiKeyService _apiKeyService;
    private readonly AppSettings _settings;

    public RealtimeClient(IApiKeyService apiKeyService, AppSettings settings)
    {
        _apiKeyService = apiKeyService;
        _settings = settings;
    }

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendAudioChunkAsync(byte[] pcm16Data, CancellationToken ct = default) => Task.CompletedTask;
    public Task CommitAudioBufferAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task CreateResponseAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task CancelResponseAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task TruncateResponseAudioAsync(string itemId, int audioEndMs, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateSessionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public event EventHandler<byte[]>? AudioDelta;
    public event EventHandler<string>? OutputTranscriptDelta;
    public event EventHandler<string>? OutputTranscriptCompleted;
    public event EventHandler<string>? InputTranscriptCompleted;
    public event EventHandler? SpeechStarted;
    public event EventHandler? SpeechStopped;
    public event EventHandler<RealtimeResponseInfo>? ResponseDone;
    public event EventHandler<string>? ErrorOccurred;
}
```

### 2.4 — Update agent signatures

Update the following to use `IRealtimeClient` instead of `IOpenAiStreamingClient`:
- `VoiceInputAgent` — constructor takes `IRealtimeClient`
- `VoiceOutputAgent` — constructor takes `IRealtimeClient`
- `AgentOrchestrator` — constructor takes `IRealtimeClient`

**Keep them as stubs** — actual logic comes in Steps 6-7. Just fix the dependency signatures now.

### 2.5 — Update DI registrations

**File:** `src/BodyCam/MauiProgram.cs`

```diff
- builder.Services.AddSingleton<IOpenAiStreamingClient, OpenAiStreamingClient>();
+ builder.Services.AddSingleton<IRealtimeClient, RealtimeClient>();
```

### 2.6 — Update tests

- Update all test files that mock `IOpenAiStreamingClient` → mock `IRealtimeClient`
- Add model tests for `RealtimeSessionConfig`, `AudioPlaybackTracker` (especially `PlayedMs` calculation)
- Update integration tests that reference `OpenAiStreamingClient`

---

## Verification

- [ ] Build succeeds
- [ ] All tests pass (updated for new interface)
- [ ] New model tests pass (`AudioPlaybackTracker.PlayedMs` calculation verified)
- [ ] DI resolves `IRealtimeClient`

---

## Files Changed

| File | Action |
|------|--------|
| `Models/RealtimeModels.cs` | NEW |
| `Services/IRealtimeClient.cs` | NEW |
| `Services/RealtimeClient.cs` | NEW (stub) |
| `Services/IOpenAiStreamingClient.cs` | DELETE |
| `Services/OpenAiStreamingClient.cs` | DELETE |
| `Agents/VoiceInputAgent.cs` | MODIFY — use `IRealtimeClient` |
| `Agents/VoiceOutputAgent.cs` | MODIFY — use `IRealtimeClient` |
| `Orchestration/AgentOrchestrator.cs` | MODIFY — use `IRealtimeClient` |
| `MauiProgram.cs` | MODIFY — register `IRealtimeClient` |
| `Tests/Agents/*.cs` | MODIFY — mock `IRealtimeClient` |
| `Tests/Models/RealtimeModelsTests.cs` | NEW |
| `IntegrationTests/Services/OpenAiStreamingClientTests.cs` | MODIFY or DELETE |
| `IntegrationTests/Orchestration/FullPipelineTests.cs` | MODIFY |
