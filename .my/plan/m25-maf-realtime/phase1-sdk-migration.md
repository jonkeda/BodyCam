# M25 Phase 1 — Replace Hand-Rolled RealtimeClient with M.E.AI

Migrate from our custom `IRealtimeClient` / `RealtimeClient` (700 lines of WebSocket plumbing + manual DTOs) to `Microsoft.Extensions.AI.IRealtimeClient` / `IRealtimeClientSession`. Eliminates ~700 lines of code, fixes the event model, and prevents future schema breakage.

**Depends on:** None (can start immediately).  
**RCA:** [rca-sdk-migration.md](../../rca/rca-sdk-migration.md)

---

## Current State

| Component | File | Lines | Status |
|---|---|---|---|
| Custom interface | `Services/IRealtimeClient.cs` | 47 | 11 events + 8 methods — DELETE |
| WebSocket implementation | `Services/RealtimeClient.cs` | 370 | Manual WS, receive loop, JSON dispatch — DELETE |
| Message DTOs | `Services/Realtime/RealtimeMessages.cs` | 200 | 20+ records with `[JsonPropertyName]` — DELETE |
| Source-gen JSON | `Services/Realtime/RealtimeJsonContext.cs` | 15 | — DELETE |
| Orchestrator | `Orchestration/AgentOrchestrator.cs` | 390 | Subscribes 11 events, 4 `async void` handlers — REWRITE |
| Voice input | `Agents/VoiceInputAgent.cs` | 50 | Sends audio via `IRealtimeClient` — MODIFY |
| Tool context | `Tools/ToolContext.cs` | 12 | Carries `IRealtimeClient` ref (unused by tools) — SIMPLIFY |
| DI registration | `ServiceExtensions.cs` | 130 | `IRealtimeClient, RealtimeClient` — REPLACE |
| Test global using | `BodyCam.Tests/GlobalUsings.cs` | 2 | Alias to resolve collision — DELETE |
| Models | `Models/RealtimeModels.cs` | 45 | `FunctionCallInfo`, `RealtimeResponseInfo` — KEEP |
| Test mocks (×18) | various | — | `Substitute.For<IRealtimeClient>()` — UPDATE |

**Key finding:** No tools actually call `context.RealtimeClient` — they all return values through `ToolResult`. The property can be removed from `ToolContext`.

---

## Wave 1: DI Registration + MAF Client Factory

### 1.1 — Register `Microsoft.Extensions.AI.IRealtimeClient` in DI

Replace the hand-rolled registration with the MAF client.

```
ServiceExtensions.cs
```

Remove:
```csharp
services.AddSingleton<IRealtimeClient, RealtimeClient>();
```

Add:
```csharp
services.AddSingleton<Microsoft.Extensions.AI.IRealtimeClient>(sp =>
{
    var settings = sp.GetRequiredService<AppSettings>();
    var apiKeyService = sp.GetRequiredService<IApiKeyService>();

    // The MAF OpenAI adapter handles both OpenAI and Azure
    if (settings.Provider == OpenAiProvider.Azure)
    {
        var credential = new System.ClientModel.ApiKeyCredential(
            apiKeyService.GetApiKeyAsync().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("API key not configured."));
        var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(settings.AzureEndpoint!), credential);
        return azureClient.AsRealtimeClient(settings.AzureRealtimeDeploymentName!);
    }
    else
    {
        var credential = new System.ClientModel.ApiKeyCredential(
            apiKeyService.GetApiKeyAsync().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("API key not configured."));
        var openaiClient = new OpenAI.OpenAIClient(credential);
        return openaiClient.AsRealtimeClient(settings.RealtimeModel);
    }
});
```

Note: Verify `AsRealtimeClient()` extension method exists in `Microsoft.Extensions.AI.OpenAI`. If not available, construct `OpenAIRealtimeClient` directly.

### 1.2 — Verify Build

Build Windows target to confirm the MAF client resolves. No behavioral change yet — nothing consumes it.

---

## Wave 2: Rewrite AgentOrchestrator to Use Session + Dispatch Loop

This is the core of the migration. Replace 11 event subscriptions + 11 unsubscriptions + 4 `async void` handlers with one `IAsyncEnumerable` dispatch loop.

### 2.1 — Change Constructor Dependencies

```
Orchestration/AgentOrchestrator.cs
```

Replace:
```csharp
private readonly IRealtimeClient _realtime;
// constructor param: IRealtimeClient realtime
```

With:
```csharp
private readonly Microsoft.Extensions.AI.IRealtimeClient _realtimeFactory;
private Microsoft.Extensions.AI.IRealtimeClientSession? _session;
private Task? _messageLoop;
// constructor param: Microsoft.Extensions.AI.IRealtimeClient realtimeFactory
```

### 2.2 — Rewrite `StartAsync()`

Replace the event subscription block with session creation + dispatch loop.

**Current** (remove):
```csharp
_realtime.AudioDelta += OnAudioDelta;
_realtime.OutputTranscriptDelta += OnOutputTranscriptDelta;
// ... 9 more subscriptions
await _realtime.ConnectAsync(_cts.Token);
```

**New**:
```csharp
var options = BuildSessionOptions();
_session = await _realtimeFactory.CreateSessionAsync(options, _cts.Token);
_messageLoop = Task.Run(() => RunMessageLoopAsync(_session, _cts.Token));
```

### 2.3 — Add `BuildSessionOptions()`

Map `AppSettings` → `RealtimeSessionOptions`:

```csharp
private RealtimeSessionOptions BuildSessionOptions()
{
    var tools = _dispatcher.GetToolDefinitions()
        .Select(dto => AIFunctionFactory.Create(/* ... */))
        .ToList();

    return new RealtimeSessionOptions
    {
        Model = _settings.RealtimeModel,
        Instructions = _settings.SystemInstructions,
        Voice = _settings.Voice,
        InputAudioFormat = new RealtimeAudioFormat { MediaType = "audio/pcm", SampleRate = 24000 },
        OutputAudioFormat = new RealtimeAudioFormat { MediaType = "audio/pcm", SampleRate = 24000 },
        OutputModalities = new[] { "text", "audio" },
        Tools = tools,
        // Map turn detection, noise reduction via RawRepresentationFactory if needed
    };
}
```

**Open question:** M.E.AI may not have first-class properties for `noise_reduction` or `turn_detection`. If not, use `RawRepresentationFactory` to inject provider-specific options:
```csharp
RawRepresentationFactory = (options) =>
{
    var raw = (OpenAI.Realtime.RealtimeConversationSessionOptions)options;
    // Set provider-specific fields here
    return raw;
}
```

### 2.4 — Add `RunMessageLoopAsync()` Dispatch Loop

This replaces ALL 11 event handlers:

```csharp
private async Task RunMessageLoopAsync(
    Microsoft.Extensions.AI.IRealtimeClientSession session,
    CancellationToken ct)
{
    try
    {
        await foreach (var message in session.GetStreamingResponseAsync(ct))
        {
            switch (message.Type)
            {
                case var t when t == RealtimeServerMessageType.OutputAudioDelta:
                    await HandleAudioDelta(message);
                    break;

                case var t when t == RealtimeServerMessageType.OutputTextDelta:
                    HandleOutputTranscriptDelta(message);
                    break;

                case var t when t == RealtimeServerMessageType.OutputTextDone:
                    HandleOutputTranscriptCompleted(message);
                    break;

                case var t when t == RealtimeServerMessageType.InputAudioTranscriptionCompleted:
                    HandleInputTranscriptCompleted(message);
                    break;

                case var t when t == RealtimeServerMessageType.ResponseOutputItemAdded:
                    HandleOutputItemAdded(message);
                    break;

                case var t when t == RealtimeServerMessageType.ResponseOutputItemDone:
                    HandleResponseOutputItemDone(message, session, ct);
                    break;

                case var t when t == RealtimeServerMessageType.ResponseDone:
                    HandleResponseDone(message);
                    break;

                case var t when t == RealtimeServerMessageType.Error:
                    HandleError(message);
                    break;

                // Speech started/stopped — check if M.E.AI exposes these
                // May need custom message types or RawRepresentation access
            }
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Clean shutdown — expected
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Message loop error");
    }
    finally
    {
        // Reconnection logic (replaces OnConnectionLost event)
        if (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Session ended unexpectedly — reconnecting");
            await ReconnectAsync();
        }
    }
}
```

### 2.5 — Extract Handler Methods

Each is a simple synchronous method (no more `async void`):

```csharp
private async Task HandleAudioDelta(RealtimeServerMessage message)
{
    // Extract audio bytes from message
    // M.E.AI should provide audio data via message properties or RawRepresentation
    var audioBytes = /* message.GetAudioBytes() or extract from RawRepresentation */;
    await _voiceOut.PlayAudioDeltaAsync(audioBytes);
}

private void HandleOutputTranscriptDelta(RealtimeServerMessage message)
{
    var delta = /* extract text from message */;
    TranscriptUpdated?.Invoke(this, $"AI: {delta}");
    TranscriptDelta?.Invoke(this, delta);
}

private void HandleOutputTranscriptCompleted(RealtimeServerMessage message)
{
    var transcript = /* extract text */;
    TranscriptCompleted?.Invoke(this, $"AI:{transcript}");
    _logger.LogDebug("AI transcript: {Length} chars", transcript.Length);
}

private void HandleInputTranscriptCompleted(RealtimeServerMessage message)
{
    var transcript = /* extract text */;
    TranscriptUpdated?.Invoke(this, $"You: {transcript}");
    TranscriptCompleted?.Invoke(this, $"You:{transcript}");
    _logger.LogDebug("User transcript received");
}

private void HandleOutputItemAdded(RealtimeServerMessage message)
{
    var itemId = /* extract from message */;
    _voiceOut.SetCurrentItem(itemId);
}

private void HandleResponseDone(RealtimeServerMessage message)
{
    _voiceOut.ResetTracker();
    _logger.LogDebug("Response complete");
}

private void HandleError(RealtimeServerMessage message)
{
    var error = /* extract error text */;
    _logger.LogError("Realtime error: {Error}", error);
}

private async Task HandleResponseOutputItemDone(
    RealtimeServerMessage message,
    Microsoft.Extensions.AI.IRealtimeClientSession session,
    CancellationToken ct)
{
    // Check if this is a function call
    // Extract call_id, name, arguments from message
    // If function call:
    var context = CreateToolContext();
    var result = await _dispatcher.ExecuteAsync(name, arguments, context, ct);
    // Send function output back
    var outputMsg = /* create RealtimeClientMessage for function call output */;
    await session.SendAsync(outputMsg, ct);
}
```

### 2.6 — Rewrite `StopAsync()`

Replace 11 unsubscriptions with clean shutdown:

**Current** (remove):
```csharp
_realtime.AudioDelta -= OnAudioDelta;
// ... 10 more unsubscriptions
await _realtime.DisconnectAsync();
```

**New**:
```csharp
_cts?.Cancel();

if (_messageLoop is not null)
{
    try { await _messageLoop; } catch (OperationCanceledException) { }
}

if (_session is not null)
{
    await _session.DisposeAsync();
    _session = null;
}
```

### 2.7 — SpeechStarted / Interruption Handling

**Open question:** M.E.AI's `RealtimeServerMessageType` may not include speech-started/stopped events. These are OpenAI-specific VAD signals.

Options:
1. Check if M.E.AI has these types (check the `RealtimeServerMessageType` well-known values)
2. Access `message.RawRepresentation` to get the underlying OpenAI SDK type
3. Use a custom `RealtimeServerMessageType` instance: `new RealtimeServerMessageType("input_audio_buffer.speech_started")`

If using `RawRepresentation`:
```csharp
case var t when t.Value == "input_audio_buffer.speech_started":
    await HandleSpeechStarted(session, ct);
    break;
```

```csharp
private async Task HandleSpeechStarted(
    Microsoft.Extensions.AI.IRealtimeClientSession session,
    CancellationToken ct)
{
    if (_voiceOut.Tracker.CurrentItemId is not null)
    {
        _voiceOut.HandleInterruption();
        var itemId = _voiceOut.Tracker.CurrentItemId;
        var playedMs = _voiceOut.Tracker.PlayedMs;
        _voiceOut.ResetTracker();

        // Send truncation via session
        var truncateMsg = /* create RealtimeClientMessage for truncation */;
        await session.SendAsync(truncateMsg, ct);
        _logger.LogDebug("Interrupted at {PlayedMs}ms", playedMs);
    }
}
```

### 2.8 — Update `ReconnectAsync()`

Replace `_realtime.ConnectAsync()` with new session creation:

```csharp
private async Task ReconnectAsync()
{
    var delay = TimeSpan.FromSeconds(1);
    const int maxRetries = 5;

    for (int i = 0; i < maxRetries; i++)
    {
        _logger.LogInformation("Reconnecting ({Attempt}/{MaxRetries})", i + 1, maxRetries);
        try
        {
            var options = BuildSessionOptions();
            _session = await _realtimeFactory.CreateSessionAsync(options, _cts?.Token ?? CancellationToken.None);
            _messageLoop = Task.Run(() => RunMessageLoopAsync(_session, _cts?.Token ?? CancellationToken.None));
            _logger.LogInformation("Reconnected");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reconnect failed");
            await Task.Delay(delay);
            delay *= 2;
        }
    }

    _logger.LogError("Reconnection failed after {MaxRetries} attempts", maxRetries);
    await StopAsync();
}
```

### 2.9 — Update `SendTextInputAsync()`

```csharp
public async Task SendTextInputAsync(string text, CancellationToken ct = default)
{
    if (_session is null)
        throw new InvalidOperationException("Session is not active.");

    // Create user message item + trigger response
    var msg = /* RealtimeClientMessage for text input */;
    await _session.SendAsync(msg, ct);
}
```

---

## Wave 3: Update VoiceInputAgent

### 3.1 — Replace `IRealtimeClient` with Audio Send Delegate

Since `VoiceInputAgent` only uses `_realtime.IsConnected` and `_realtime.SendAudioChunkAsync()`, inject a delegate instead of the full session.

```
Agents/VoiceInputAgent.cs
```

Replace:
```csharp
private readonly IRealtimeClient _realtime;

public VoiceInputAgent(IAudioInputService audioInput, IRealtimeClient realtime, AecProcessor? aec = null)
```

With:
```csharp
private Func<byte[], CancellationToken, Task>? _audioSink;
private bool _isConnected;

public VoiceInputAgent(IAudioInputService audioInput, AecProcessor? aec = null)

public void SetAudioSink(Func<byte[], CancellationToken, Task> sink) => _audioSink = sink;
public void SetConnected(bool connected) => _isConnected = connected;
```

Update `OnAudioChunk`:
```csharp
private async void OnAudioChunk(object? sender, byte[] chunk)
{
    try
    {
        if (_isConnected && _audioSink is not null)
        {
            byte[] processed = _aec is not null ? _aec.ProcessCapture(chunk) : chunk;
            await _audioSink(processed, CancellationToken.None);
        }
    }
    catch { }
}
```

### 3.2 — Wire Up in AgentOrchestrator.StartAsync()

After session creation:
```csharp
_voiceIn.SetAudioSink(async (pcm, ct) =>
{
    var msg = /* RealtimeClientMessage for audio append */;
    await _session!.SendAsync(msg, ct);
});
_voiceIn.SetConnected(true);
```

In `StopAsync()`:
```csharp
_voiceIn.SetConnected(false);
```

---

## Wave 4: Simplify ToolContext

### 4.1 — Remove `RealtimeClient` Property

No tool uses it — confirmed by audit.

```
Tools/ToolContext.cs
```

Change from:
```csharp
public sealed class ToolContext
{
    public required Func<CancellationToken, Task<byte[]?>> CaptureFrame { get; init; }
    public required SessionContext Session { get; init; }
    public required Action<string> Log { get; init; }
    public required IRealtimeClient RealtimeClient { get; init; }
}
```

To:
```csharp
public sealed class ToolContext
{
    public required Func<CancellationToken, Task<byte[]?>> CaptureFrame { get; init; }
    public required SessionContext Session { get; init; }
    public required Action<string> Log { get; init; }
}
```

### 4.2 — Update `CreateToolContext()` in `AgentOrchestrator`

Remove `RealtimeClient = _realtime` from the initializer:
```csharp
private ToolContext CreateToolContext() => new()
{
    CaptureFrame = _cameraManager.CaptureFrameAsync,
    Session = Session,
    Log = msg => _logger.LogInformation("{ToolMessage}", msg),
};
```

---

## Wave 5: Delete Dead Code

### 5.1 — Delete Files

| File | Reason |
|---|---|
| `Services/IRealtimeClient.cs` | Replaced by `Microsoft.Extensions.AI.IRealtimeClient` |
| `Services/RealtimeClient.cs` | Replaced by MAF session |
| `Services/Realtime/RealtimeMessages.cs` | All DTOs handled by SDK |
| `Services/Realtime/RealtimeJsonContext.cs` | No manual serialization needed |

### 5.2 — Remove `Realtime/` Directory

If empty after deletions, remove `Services/Realtime/`.

### 5.3 — Clean Up Usings

Remove `using BodyCam.Services.Realtime;` from any remaining files.

---

## Wave 6: Update Tests

### 6.1 — Delete Test GlobalUsings Alias

```
BodyCam.Tests/GlobalUsings.cs
```

Remove or delete:
```csharp
global using IRealtimeClient = BodyCam.Services.IRealtimeClient;
```

### 6.2 — Delete `RealtimeMessageTests.cs`

No more custom DTOs to test.

### 6.3 — Update AgentOrchestratorTests

Replace `Substitute.For<IRealtimeClient>()` with `Substitute.For<Microsoft.Extensions.AI.IRealtimeClient>()`. The mock now returns a mock `IRealtimeClientSession` from `CreateSessionAsync()`.

```csharp
var realtimeFactory = Substitute.For<Microsoft.Extensions.AI.IRealtimeClient>();
var session = Substitute.For<Microsoft.Extensions.AI.IRealtimeClientSession>();
realtimeFactory.CreateSessionAsync(Arg.Any<RealtimeSessionOptions>(), Arg.Any<CancellationToken>())
    .Returns(Task.FromResult(session));
```

To test the dispatch loop, feed messages via the mock session's `GetStreamingResponseAsync()`.

### 6.4 — Update VoiceInputAgentTests

Constructor no longer takes `IRealtimeClient`. Tests call `SetAudioSink()` with a test delegate and `SetConnected(true)`.

### 6.5 — Update ToolContext in All Tool Tests (×16)

Remove `RealtimeClient = Substitute.For<IRealtimeClient>()` from `ToolContext` creation. These are mechanical — remove one property from each test fixture.

Affected tests:
- `DescribeSceneToolTests`, `DeepAnalysisToolTests`, `ReadTextToolTests`
- `TakePhotoToolTests`, `SaveMemoryToolTests`, `RecallMemoryToolTests`
- `SetTranslationModeToolTests`, `MakePhoneCallToolTests`, `SendMessageToolTests`
- `LookupAddressToolTests`, `FindObjectToolTests`, `NavigateToToolTests`
- `StartSceneWatchToolTests`, `ToolBaseTests`, `ToolDispatcherTests`

### 6.6 — Update `BodyCamTestHost.cs`

Remove `services.AddSingleton(Substitute.For<IRealtimeClient>());` and add MAF mock:
```csharp
services.AddSingleton(Substitute.For<Microsoft.Extensions.AI.IRealtimeClient>());
```

### 6.7 — Run Full Test Suite

```powershell
dotnet test src/BodyCam.Tests/
```

All 372 tests must pass.

---

## Wave 7: Build Verification

### 7.1 — Windows Build

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
```

### 7.2 — Android Build

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android
```

### 7.3 — Manual Smoke Test (Windows)

1. Launch app
2. Start session — verify audio pipeline connects
3. Speak — verify transcription appears
4. Verify AI response plays through speakers
5. Test interruption (speak while AI is talking)
6. Test function call (e.g., "describe what you see")
7. Stop session — verify clean shutdown

---

## Open Questions (Resolve During Implementation)

1. **Audio message format**: How does `IRealtimeClientSession.SendAsync()` accept audio data? Need to check if M.E.AI has a convenience for audio or if we wrap raw bytes in a `RealtimeClientMessage`.

2. **Speech events**: Does `RealtimeServerMessageType` include `SpeechStarted`/`SpeechStopped`? If not, we need `RawRepresentation` access or custom type values.

3. **Truncation**: Is there a MAF-level API for `conversation.item.truncate`, or do we send raw messages?

4. **Tool definitions**: M.E.AI uses `AITool` / `AIFunction` from the chat abstractions. Need to map `ToolDispatcher.GetToolDefinitions()` → `AITool` instances.

5. **Azure provider**: Confirm `AzureOpenAIClient.AsRealtimeClient()` extension works for Realtime API specifically.

---

## Summary

| Metric | Before | After |
|---|---|---|
| Custom code for Realtime API | ~700 lines | 0 |
| Event subscriptions | 22 (11+11) | 0 |
| `async void` handlers | 4 | 0 |
| Manual JSON DTOs | 20+ records | 0 |
| Schema break risk | Every API change | NuGet update handles it |
| Files to maintain | 4 core + interface | 0 (SDK provides all) |
