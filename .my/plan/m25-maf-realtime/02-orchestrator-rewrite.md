# Step 02 — Rewrite AgentOrchestrator

Replace 11 event subscriptions + 11 unsubscriptions + 5 `async void` handlers with one `IAsyncEnumerable` dispatch loop. This is the core of the migration.

**Depends on:** Step 01  
**Touches:** `Orchestration/AgentOrchestrator.cs`  
**Tests affected:** `AgentOrchestratorTests.cs` (updated in step 06)

---

## What to Do

### 2.1 — Change constructor dependencies

```
src/BodyCam/Orchestration/AgentOrchestrator.cs
```

**Replace:**
```csharp
private readonly IRealtimeClient _realtime;
```

**With:**
```csharp
private readonly Microsoft.Extensions.AI.IRealtimeClient _realtimeFactory;
private Microsoft.Extensions.AI.IRealtimeClientSession? _session;
private Task? _messageLoop;
```

**In the constructor, replace:**
```csharp
IRealtimeClient realtime,
```

**With:**
```csharp
Microsoft.Extensions.AI.IRealtimeClient realtimeFactory,
```

**And the assignment:**
```csharp
_realtime = realtime;
```
→
```csharp
_realtimeFactory = realtimeFactory;
```

**Add usings:**
```csharp
using Microsoft.Extensions.AI;
```

### 2.2 — Add `BuildSessionOptions()`

New method that maps `AppSettings` → `RealtimeSessionOptions`:

```csharp
private RealtimeSessionOptions BuildSessionOptions()
{
    // Build AITool list from ToolDispatcher
    var tools = _dispatcher.GetToolDefinitions()
        .Select(dto => AIFunctionFactory.Create(
            method: async (string? args, CancellationToken ct) =>
            {
                var context = CreateToolContext();
                return await _dispatcher.ExecuteAsync(dto.Name, args, context, ct);
            },
            name: dto.Name,
            description: dto.Description))
        .Cast<AITool>()
        .ToList();

    return new RealtimeSessionOptions
    {
        Instructions = _settings.SystemInstructions,
        Tools = tools,
        // Audio format, voice, turn detection, noise reduction
        // Use RawRepresentationFactory for provider-specific fields:
        RawRepresentationFactory = baseOptions =>
        {
            // Cast to SDK options to set provider-specific fields
            // like noise_reduction, turn_detection, voice, audio format
            return baseOptions;
        }
    };
}
```

> **Open question:** Verify exact `RealtimeSessionOptions` properties at implementation time. The `AIFunctionFactory.Create` overload that takes a method delegate may differ — check XML docs. The key point: each tool wraps `_dispatcher.ExecuteAsync()`.

### 2.3 — Rewrite `StartAsync()`

**Current code to replace (lines ~80-150) — the event subscription block:**
```csharp
// Subscribe to Realtime events
_realtime.AudioDelta += OnAudioDelta;
_realtime.OutputTranscriptDelta += OnOutputTranscriptDelta;
_realtime.OutputTranscriptCompleted += OnOutputTranscriptCompleted;
_realtime.InputTranscriptCompleted += OnInputTranscriptCompleted;
_realtime.SpeechStarted += OnSpeechStarted;
_realtime.ResponseDone += OnResponseDone;
_realtime.ErrorOccurred += OnError;
_realtime.OutputItemAdded += OnOutputItemAdded;
_realtime.FunctionCallReceived += OnFunctionCallReceived;
_realtime.ConnectionLost += OnConnectionLost;

// Connect to OpenAI
await _realtime.ConnectAsync(_cts.Token);
_logger.LogInformation("Realtime connected");
```

**Replace with:**
```csharp
var options = BuildSessionOptions();
_session = await _realtimeFactory.CreateSessionAsync(options, _cts.Token);
_messageLoop = Task.Run(() => RunMessageLoopAsync(_session, _cts.Token));
_logger.LogInformation("Realtime session created");
```

### 2.4 — Add `RunMessageLoopAsync()` dispatch loop

This replaces ALL event handlers. Use the resolved open questions for exact message types:

```csharp
// Custom speech event types (not in MAF abstraction)
private static readonly RealtimeServerMessageType SpeechStarted =
    new("input_audio_buffer.speech_started");
private static readonly RealtimeServerMessageType SpeechStopped =
    new("input_audio_buffer.speech_stopped");

private async Task RunMessageLoopAsync(
    IRealtimeClientSession session,
    CancellationToken ct)
{
    try
    {
        await foreach (var message in session.GetStreamingResponseAsync(ct))
        {
            var type = message.Type;

            if (type == RealtimeServerMessageType.OutputAudioDelta)
            {
                await HandleAudioDelta(message);
            }
            else if (type == RealtimeServerMessageType.OutputTextDelta)
            {
                HandleOutputTranscriptDelta(message);
            }
            else if (type == RealtimeServerMessageType.OutputTextDone
                  || type == RealtimeServerMessageType.OutputAudioTranscriptionDone)
            {
                HandleOutputTranscriptCompleted(message);
            }
            else if (type == RealtimeServerMessageType.InputAudioTranscriptionCompleted)
            {
                HandleInputTranscriptCompleted(message);
            }
            else if (type == RealtimeServerMessageType.ResponseOutputItemAdded)
            {
                HandleOutputItemAdded(message);
            }
            else if (type == RealtimeServerMessageType.ResponseDone)
            {
                HandleResponseDone(message);
            }
            else if (type == RealtimeServerMessageType.Error)
            {
                HandleError(message);
            }
            else if (type == SpeechStarted)
            {
                await HandleSpeechStarted(session, ct);
            }
            // FunctionCallReceived is handled by FunctionInvokingRealtimeClient middleware
            // No manual handling needed — the middleware intercepts, invokes, and responds
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Clean shutdown
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Message loop error");
    }
    finally
    {
        if (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Session ended unexpectedly — reconnecting");
            await ReconnectAsync();
        }
    }
}
```

> **Key:** No `OnFunctionCallReceived` handler. `FunctionInvokingRealtimeClient` middleware (registered in step 01) handles the entire function call lifecycle automatically.

### 2.5 — Implement handler methods

Replace the old `async void` event handlers with proper methods:

```csharp
private async Task HandleAudioDelta(RealtimeServerMessage message)
{
    try
    {
        // OutputTextAudioRealtimeServerMessage has .Audio (DataContent)
        var audioMsg = (OutputTextAudioRealtimeServerMessage)message;
        var pcm = audioMsg.Audio?.Data.ToArray();
        if (pcm is not null)
            await _voiceOut.PlayAudioDeltaAsync(pcm);
    }
    catch (Exception ex) { _logger.LogError(ex, "Playback error"); }
}

private void HandleOutputTranscriptDelta(RealtimeServerMessage message)
{
    var audioMsg = (OutputTextAudioRealtimeServerMessage)message;
    var delta = audioMsg.Text;
    if (delta is not null)
    {
        TranscriptUpdated?.Invoke(this, $"AI: {delta}");
        TranscriptDelta?.Invoke(this, delta);
    }
}

private void HandleOutputTranscriptCompleted(RealtimeServerMessage message)
{
    var audioMsg = (OutputTextAudioRealtimeServerMessage)message;
    var transcript = audioMsg.Text;
    if (transcript is not null)
    {
        TranscriptCompleted?.Invoke(this, $"AI:{transcript}");
        _logger.LogDebug("AI transcript: {Length} chars", transcript.Length);
    }
}

private void HandleInputTranscriptCompleted(RealtimeServerMessage message)
{
    // Extract transcript — check concrete type or RawRepresentation
    var transcript = /* extract from message */;
    if (transcript is not null)
    {
        TranscriptUpdated?.Invoke(this, $"You: {transcript}");
        TranscriptCompleted?.Invoke(this, $"You:{transcript}");
        _logger.LogDebug("User transcript received");
    }
}

private void HandleOutputItemAdded(RealtimeServerMessage message)
{
    // Extract item ID from ResponseOutputItemRealtimeServerMessage
    var itemMsg = (ResponseOutputItemRealtimeServerMessage)message;
    var itemId = itemMsg.Item?.Id;
    if (itemId is not null)
        _voiceOut.SetCurrentItem(itemId);
}

private void HandleResponseDone(RealtimeServerMessage message)
{
    _voiceOut.ResetTracker();
    _logger.LogDebug("Response complete");
}

private void HandleError(RealtimeServerMessage message)
{
    var errorMsg = (ErrorRealtimeServerMessage)message;
    _logger.LogError("Realtime error: {Error}", errorMsg.Error);
}
```

### 2.6 — Implement `HandleSpeechStarted()` with truncation

Uses `RawRepresentation` for the truncation command (not in MAF):

```csharp
private async Task HandleSpeechStarted(
    IRealtimeClientSession session, CancellationToken ct)
{
    try
    {
        if (_voiceOut.Tracker.CurrentItemId is not null)
        {
            _voiceOut.HandleInterruption();
            var itemId = _voiceOut.Tracker.CurrentItemId;
            var playedMs = _voiceOut.Tracker.PlayedMs;
            _voiceOut.ResetTracker();

            // Truncation not in MAF — use SDK command via RawRepresentation
            var truncateCmd = new OpenAI.Realtime.RealtimeClientCommandConversationItemTruncate(
                itemId, 0, playedMs);
            var msg = new RealtimeClientMessage { RawRepresentation = truncateCmd };
            await session.SendAsync(msg, ct);
            _logger.LogDebug("Interrupted at {PlayedMs}ms", playedMs);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "SpeechStarted handler error");
    }
}
```

### 2.7 — Rewrite `StopAsync()`

**Replace the entire event unsubscription block:**
```csharp
// Unsubscribe events
_realtime.AudioDelta -= OnAudioDelta;
_realtime.OutputTranscriptDelta -= OnOutputTranscriptDelta;
// ... 8 more lines
_realtime.ConnectionLost -= OnConnectionLost;

_cts?.Cancel();

await _voiceIn.StopAsync();
await _voiceOut.StopAsync();

await _realtime.DisconnectAsync();
```

**With:**
```csharp
_cts?.Cancel();

if (_messageLoop is not null)
{
    try { await _messageLoop; } catch (OperationCanceledException) { }
    _messageLoop = null;
}

await _voiceIn.StopAsync();
await _voiceOut.StopAsync();

if (_session is not null)
{
    await _session.DisposeAsync();
    _session = null;
}
```

### 2.8 — Rewrite `ReconnectAsync()`

**Replace `_realtime.ConnectAsync()` + `_realtime.UpdateSessionAsync()` with new session creation:**

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
            if (_session is not null)
                await _session.DisposeAsync();

            var options = BuildSessionOptions();
            var ct = _cts?.Token ?? CancellationToken.None;
            _session = await _realtimeFactory.CreateSessionAsync(options, ct);
            _messageLoop = Task.Run(() => RunMessageLoopAsync(_session, ct));
            await _voiceIn.StartAsync(ct);
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

    _logger.LogError("Reconnection failed after {MaxRetries} attempts. Stopping session", maxRetries);
    await StopAsync();
}
```

### 2.9 — Rewrite `SendTextInputAsync()`

```csharp
public async Task SendTextInputAsync(string text, CancellationToken ct = default)
{
    if (_session is null)
        throw new InvalidOperationException("Session is not active.");

    // Create user message + trigger response
    var item = new RealtimeConversationItem
    {
        // Set role, content, etc.
    };
    var createMsg = new CreateConversationItemRealtimeClientMessage(item);
    await _session.SendAsync(createMsg, ct);
    await _session.SendAsync(new CreateResponseRealtimeClientMessage(), ct);
}
```

### 2.10 — Update `CreateToolContext()`

Remove `RealtimeClient = _realtime` (moved to step 04, but the orchestrator side needs updating here):

```csharp
private ToolContext CreateToolContext() => new()
{
    CaptureFrame = _cameraManager.CaptureFrameAsync,
    Session = Session,
    Log = msg => _logger.LogInformation("{ToolMessage}", msg),
    // RealtimeClient removed — no tool uses it
};
```

> This will cause a build error until step 04 removes the property from `ToolContext`. Execute steps 02+04 together, or temporarily comment the property.

### 2.11 — Delete old event handler methods

Remove these methods entirely (replaced by dispatch loop handlers):
- `OnAudioDelta`
- `OnOutputTranscriptDelta`
- `OnOutputTranscriptCompleted`
- `OnInputTranscriptCompleted`
- `OnSpeechStarted`
- `OnResponseDone`
- `OnError`
- `OnOutputItemAdded`
- `OnFunctionCallReceived`
- `OnConnectionLost`

---

## What's Eliminated

| Before | After |
|---|---|
| 11 event subscriptions in `StartAsync()` | 0 |
| 11 event unsubscriptions in `StopAsync()` | 0 |
| 5 `async void` event handlers | 0 |
| Manual `OnFunctionCallReceived` + `SendFunctionCallOutputAsync` + `CreateResponseAsync` | Handled by `FunctionInvokingRealtimeClient` middleware |
| `ConnectionLost` event handler | `finally` block in dispatch loop |

---

## Acceptance Criteria

- [ ] Constructor takes `Microsoft.Extensions.AI.IRealtimeClient` instead of `BodyCam.Services.IRealtimeClient`
- [ ] `StartAsync()` creates session + starts dispatch loop
- [ ] `StopAsync()` cancels loop + disposes session
- [ ] No `async void` handlers remain
- [ ] No event subscriptions/unsubscriptions
- [ ] Function calls handled automatically by middleware
- [ ] Speech interruption works via custom `RealtimeServerMessageType` + SDK `RawRepresentation`
- [ ] Reconnection creates new session
- [ ] Build compiles (may need step 04 done simultaneously)

---

## Risk Notes

- **Cast types**: The exact concrete types (`OutputTextAudioRealtimeServerMessage`, `ResponseOutputItemRealtimeServerMessage`, etc.) need verification at implementation time. If a cast fails, use `message.RawRepresentation` to get the underlying SDK type.
- **InputAudioTranscription**: The `HandleInputTranscriptCompleted` handler needs to extract the transcript text — verify the concrete message type properties.
- **`AIFunctionFactory.Create` signature**: Verify the overload that accepts a delegate. The method signature may require specific parameter types for the function invocation middleware to work correctly.
