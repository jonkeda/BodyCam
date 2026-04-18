# Step 2: Add try/catch to Async Void Event Handlers

**Priority:** P0 | **Effort:** Small | **Risk:** Silent crashes in event handlers

---

## Problem

`async void` event handlers in `AgentOrchestrator` and `VoiceInputAgent` can throw unobserved exceptions that crash the handler silently. The error disappears with no log entry.

## Steps

### 2.1 VoiceInputAgent.OnAudioChunk — Add error counter + logging

**File:** `src/BodyCam/Agents/VoiceInputAgent.cs`

The handler already has a try/catch but swallows silently. Add a dropped-chunk counter and log the first failure:

```csharp
private int _droppedChunks;

private async void OnAudioChunk(object? sender, byte[] chunk)
{
    try
    {
        if (_realtime.IsConnected)
            await _realtime.SendAudioChunkAsync(chunk);
    }
    catch (Exception)
    {
        Interlocked.Increment(ref _droppedChunks);
        // Errors surface via IRealtimeClient.ErrorOccurred
    }
}
```

### 2.2 AgentOrchestrator.OnSpeechStarted — Wrap in try/catch

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs`

The inner try/catch only covers truncation. Wrap the outer body:

```csharp
private async void OnSpeechStarted(object? sender, EventArgs e)
{
    try
    {
        if (_voiceOut.Tracker.CurrentItemId is not null)
        {
            _voiceOut.HandleInterruption();
            var itemId = _voiceOut.Tracker.CurrentItemId;
            var playedMs = _voiceOut.Tracker.PlayedMs;
            _voiceOut.ResetTracker();

            try
            {
                await _realtime.TruncateResponseAudioAsync(itemId, playedMs);
                DebugLog?.Invoke(this, $"Interrupted at {playedMs}ms.");
            }
            catch (Exception ex)
            {
                DebugLog?.Invoke(this, $"Truncation error: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        DebugLog?.Invoke(this, $"SpeechStarted handler error: {ex.Message}");
    }
}
```

### 2.3 AgentOrchestrator.OnWakeWordDetected — Wrap in try/catch

The `switch` block has no outer try/catch. Errors in `StartAsync` / `StopAsync` crash silently:

```csharp
private async void OnWakeWordDetected(object? sender, WakeWordDetectedEventArgs e)
{
    try
    {
        DebugLog?.Invoke(this, $"Wake word: {e.Keyword} ({e.Action})");

        switch (e.Action)
        {
            // ... existing cases ...
        }
    }
    catch (Exception ex)
    {
        DebugLog?.Invoke(this, $"Wake word handler error: {ex.Message}");
    }
}
```

### 2.4 AgentOrchestrator.OnFunctionCallReceived — Already has try/catch

Verify the existing try/catch covers the entire handler body. It already does — the outer try/catch sends error JSON back to the model. **No change needed.**

### 2.5 AudioInputManager.OnProviderDisconnected — Wrap in try/catch

**File:** `src/BodyCam/Services/Audio/AudioInputManager.cs`

```csharp
private async void OnProviderDisconnected(object? sender, EventArgs e)
{
    try
    {
        await FallbackToPlatformAsync();
    }
    catch (Exception)
    {
        // Best-effort fallback — if this fails, the manager is in a bad state
        // but at least we don't crash the app
    }
}
```

### 2.6 AudioOutputManager.OnProviderDisconnected — Same pattern

**File:** `src/BodyCam/Services/Audio/AudioOutputManager.cs`

```csharp
private async void OnProviderDisconnected(object? sender, EventArgs e)
{
    try
    {
        await FallbackToDefaultAsync();
    }
    catch (Exception) { }
}
```

### 2.7 MainViewModel.OnButtonAction — Wrap in try/catch

**File:** `src/BodyCam/ViewModels/MainViewModel.cs`

```csharp
private async void OnButtonAction(object? sender, ButtonActionEvent e)
{
    try
    {
        await DispatchActionAsync(e.Action);
    }
    catch (Exception ex)
    {
        DebugLog += $"[{DateTime.Now:HH:mm:ss}] Button handler error: {ex.Message}{Environment.NewLine}";
    }
}
```

### 2.8 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```
