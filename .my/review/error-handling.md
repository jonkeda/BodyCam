# Error Handling Review

## Summary

The app uses a mix of try/catch, fire-and-forget, and event-based error reporting. Several critical paths swallow exceptions silently, making failures invisible to both the user and the debug log.

---

## 1. VoiceInputAgent — Silent Audio Loss

**Risk: High**

`OnAudioChunk` calls `SendAudioChunkAsync` on the realtime client. If the WebSocket is disconnected or the send fails, the error is swallowed. The user continues speaking but nothing reaches the API.

```csharp
// Current — no error handling
private async void OnAudioChunk(object? sender, byte[] chunk)
{
    if (_realtime.IsConnected)
        await _realtime.SendAudioChunkAsync(chunk);
}
```

**Problems:**
- `async void` — any exception crashes the event handler silently
- No feedback to user that audio is being lost
- No metric/counter for dropped chunks

**Proposed fix:**
```csharp
private async void OnAudioChunk(object? sender, byte[] chunk)
{
    try
    {
        if (_realtime.IsConnected)
            await _realtime.SendAudioChunkAsync(chunk);
    }
    catch (Exception ex)
    {
        Interlocked.Increment(ref _droppedChunks);
        if (_droppedChunks == 1) // log once, not per chunk
            _log?.Invoke($"Audio send failed: {ex.Message}");
    }
}
```

---

## 2. Orchestrator Event Handlers — Async Void

**Risk: Medium**

Several event handlers in `AgentOrchestrator` are `async void` (required by event delegate signature). If they throw, the exception is unobserved.

**Affected handlers:**
- `OnFunctionCallReceived` — tool execution could throw
- `OnWakeWordDetected` — session start could throw
- `OnAudioDelta` — playback could throw

**Proposed fix:** Wrap each handler body in try/catch:
```csharp
private async void OnFunctionCallReceived(object? sender, FunctionCallEventArgs e)
{
    try
    {
        var result = await _toolDispatcher.ExecuteAsync(e.Name, e.Arguments, context, _cts.Token);
        await _realtime.SendFunctionCallOutputAsync(e.CallId, result);
    }
    catch (Exception ex)
    {
        DebugLog?.Invoke(this, $"Tool error ({e.Name}): {ex.Message}");
        // Send error result so the model can respond
        await _realtime.SendFunctionCallOutputAsync(e.CallId, 
            $$$"""{"error":"{{{ex.Message}}}"}""");
    }
}
```

---

## 3. RealtimeClient Receive Loop

**Risk: Medium**

The receive loop catches all exceptions and exits. After exiting, no reconnection or notification occurs.

```csharp
// Current pattern
while (!ct.IsCancellationRequested)
{
    try { /* receive + parse */ }
    catch (OperationCanceledException) { break; }
    catch (Exception ex) { Log(ex.Message); break; } // ← exits silently
}
```

**Proposed fix:** Raise a `ConnectionLost` event before exiting:
```csharp
catch (Exception ex)
{
    Log(ex.Message);
    ConnectionLost?.Invoke(this, ex);
    break;
}
```

---

## 4. MainViewModel.SetLayerAsync — Partial State on Failure

**Risk: Medium**

If `_orchestrator.StartAsync()` throws, the catch block reverts `IsRunning` and `ToggleButtonText` but doesn't revert `CurrentLayer`. The UI shows the correct button text but the segment control may reflect the wrong layer.

```csharp
catch (Exception ex)
{
    IsRunning = false;
    ToggleButtonText = "Start";
    // BUG: CurrentLayer is already set to ActiveSession before try block
    // Should revert: CurrentLayer = previous layer
}
```

**Proposed fix:** Set `CurrentLayer` only after successful start:
```csharp
// Move CurrentLayer assignment to after orchestrator.StartAsync() succeeds
await _orchestrator.StartAsync();
IsRunning = true;
CurrentLayer = target; // ← move here from before the try
```

---

## 5. Tool Execution — No Error Envelope

**Risk: Low**

`ToolBase<T>.ExecuteAsync` deserializes arguments and calls the concrete tool. If deserialization fails (bad JSON from the model), the exception propagates up to the orchestrator's async void handler.

**Proposed fix:** Catch in ToolDispatcher:
```csharp
public async Task<string> ExecuteAsync(string name, string args, ToolContext ctx, CancellationToken ct)
{
    if (!_tools.TryGetValue(name, out var tool))
        return """{"error":"Unknown tool"}""";
    
    try
    {
        return await tool.ExecuteAsync(args, ctx, ct);
    }
    catch (JsonException)
    {
        return """{"error":"Invalid arguments"}""";
    }
    catch (Exception ex)
    {
        return $$$"""{"error":"{{{ex.Message}}}"}""";
    }
}
```

This ensures the model always gets a response, even on error.

---

## 6. PhoneCameraProvider — No Error Feedback

**Risk: Low**

`CaptureFrameAsync` returns `null` on failure (timeout, camera not ready). The caller (VisionAgent, tools) receives null and must handle it. Currently, null → "Camera not available" message in transcript, which is adequate.

No change needed — this is acceptable.

---

## Priority

| Fix | Effort | Impact |
|-----|--------|--------|
| VoiceInputAgent try/catch | Trivial | Prevents silent audio loss |
| Orchestrator handler guards | Small | Prevents silent tool/playback crashes |
| Receive loop ConnectionLost event | Small | Enables reconnection (see resilience.md) |
| SetLayerAsync state revert | Trivial | Prevents UI state inconsistency |
| ToolDispatcher error envelope | Small | Prevents unhandled exceptions from bad model output |
