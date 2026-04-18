# Resilience Review

## Summary

The app has no automatic recovery for network or device failures. A dropped WebSocket or disconnected camera silently degrades the experience. Tool execution has no timeout, so a hung tool blocks the entire response pipeline.

---

## 1. No WebSocket Reconnection

**Risk: High**

`RealtimeClient.DisconnectAsync` is a one-way operation. If the WebSocket drops (network hiccup, server timeout, mobile network switch), the session dies. The user sees no error and must manually toggle Sleep → Active to reconnect.

**Current behavior:**
```
WebSocket drops → ReceiveLoop catches exception → logs error → exits
→ IsConnected = false → VoiceInputAgent silently swallows audio
→ User sees nothing — no transcript, no error, silence
```

**Proposed fix — Reconnect with exponential backoff in AgentOrchestrator:**
```csharp
private async Task OnConnectionLost()
{
    var delay = TimeSpan.FromSeconds(1);
    const int maxRetries = 5;
    
    for (int i = 0; i < maxRetries; i++)
    {
        StatusChanged?.Invoke(this, $"Reconnecting ({i + 1}/{maxRetries})...");
        try
        {
            await _realtime.ConnectAsync(_cts.Token);
            await _realtime.UpdateSessionAsync(BuildConfig());
            _voiceInput.Start();
            StatusChanged?.Invoke(this, "Reconnected");
            return;
        }
        catch
        {
            await Task.Delay(delay);
            delay *= 2;
        }
    }
    
    // Give up — transition to Sleep
    StatusChanged?.Invoke(this, "Connection lost");
    await StopAsync();
}
```

The orchestrator should subscribe to a `ConnectionLost` event from `RealtimeClient` (currently doesn't exist — needs adding).

---

## 2. No Tool Execution Timeout

**Risk: Medium**

When the Realtime API invokes a function call, the orchestrator executes the tool and sends the result back. If a tool hangs (e.g., `VisionAgent.DescribeFrameAsync` waiting on a slow API), the response pipeline blocks indefinitely. The user hears nothing.

**Proposed fix — Per-tool timeout in ToolDispatcher:**
```csharp
public async Task<string> ExecuteAsync(string name, string args, ToolContext ctx, CancellationToken ct)
{
    var tool = _tools[name];
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(15)); // 15s default
    
    try
    {
        return await tool.ExecuteAsync(args, ctx, cts.Token);
    }
    catch (OperationCanceledException)
    {
        return """{"error":"Tool execution timed out"}""";
    }
}
```

Return a timeout error to the model so it can respond gracefully ("I couldn't complete that action").

---

## 3. Camera Capture TaskCompletionSource Leak

**Risk: Low**

`PhoneCameraProvider.CaptureViaEventAsync` creates a `TaskCompletionSource`, subscribes to `MediaCaptured`, calls `CaptureImage()`, then waits with a 5-second timeout. If the timeout fires and the event arrives later, the event handler remains subscribed until the next capture.

**Proposed fix:**
```csharp
private async Task<byte[]?> CaptureViaEventAsync()
{
    var tcs = new TaskCompletionSource<byte[]?>();
    
    void handler(object? s, MediaCapturedEventArgs e)
    {
        tcs.TrySetResult(e.Media?.ToArray());
    }
    
    _cameraView.MediaCaptured += handler;
    try
    {
        _cameraView.CaptureImage();
        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        return result == tcs.Task ? await tcs.Task : null;
    }
    finally
    {
        _cameraView.MediaCaptured -= handler; // Always unsubscribe
    }
}
```

---

## 4. Porcupine Instance Leak on Error

**Risk: Low**

`PorcupineWakeWordService.StartAsync` creates a `Porcupine` instance, then subscribes to audio events. If the subscription or subsequent code throws, the Porcupine instance is never disposed.

**Proposed fix:** Wrap creation in try/catch or use a local variable with cleanup:
```csharp
public async Task StartAsync()
{
    Porcupine? porcupine = null;
    try
    {
        porcupine = Porcupine.FromKeywordPaths(...);
        // subscribe to audio...
        _porcupine = porcupine;
        porcupine = null; // transfer ownership
    }
    finally
    {
        porcupine?.Dispose(); // only if not transferred
    }
}
```

---

## 5. Microphone Coordinator Hardcoded Delay

**Risk: Low**

`MicrophoneCoordinator` uses a 50ms delay for platform mic release between wake word and Realtime session. This may be insufficient on older Android devices or under heavy load.

**Proposed fix:** Make configurable via `AppSettings`:
```csharp
public int MicReleaseDelayMs { get; set; } = 50; // can increase per-platform
```

Or use a retry loop that polls mic availability.

---

## Priority

| Fix | Effort | Impact |
|-----|--------|--------|
| WebSocket reconnection | Medium | Prevents silent session death |
| Tool execution timeout | Small | Prevents hung response pipeline |
| Camera TCS cleanup | Trivial | Prevents event handler leak |
| Porcupine dispose guard | Trivial | Prevents native memory leak |
| Configurable mic delay | Trivial | Platform robustness |
