# Improvement Backlog

Improvements identified during the M25 planning audit. Excludes anything fixed by the MAF migration (M25) since `RealtimeClient.cs`, `RealtimeMessages.cs`, and `RealtimeJsonContext.cs` are being deleted.

---

## Critical

### 1. `SettingsViewModel.LoadApiKeyDisplay()` is `async void` but NOT an event handler

```
src/BodyCam/ViewModels/SettingsViewModel.cs:300
```

```csharp
private async void LoadApiKeyDisplay()
{
    _fullKey = await _apiKeyService.GetApiKeyAsync();
    ApiKeyDisplay = MaskKey(_fullKey);
}
```

If `GetApiKeyAsync()` throws, the exception is unobserved and crashes the process. This is the only `async void` in the codebase that isn't an event handler (the other 16 are all event handlers — acceptable).

**Fix**: Convert to `async Task`, call with fire-and-forget where needed:
```csharp
private async Task LoadApiKeyDisplayAsync()
{
    _fullKey = await _apiKeyService.GetApiKeyAsync();
    ApiKeyDisplay = MaskKey(_fullKey);
}
```

---

## High

### 2. Swallowed exception in `SettingsViewModel.ParseModelIds()`

```
src/BodyCam/ViewModels/SettingsViewModel.cs:548
```

```csharp
catch { }
return ids;
```

JSON parsing failure during model list fetch is silently swallowed. User sees empty model dropdown with no indication why.

**Fix**: Log the error and return empty set:
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to parse model list response");
}
```

### 3. Synchronous `SemaphoreSlim.Wait()` in async context

```
src/BodyCam/Services/Audio/AudioInputManager.cs:97
src/BodyCam/Services/Audio/AudioOutputManager.cs:145
```

`RegisterProvider()` calls `_lock.Wait()` synchronously, then fires `_ = SetActiveCoreAsync()` (fire-and-forget async) inside the lock. This blocks the calling thread and the async work can outlive the lock.

**Fix**: Make `RegisterProvider` async, use `await _lock.WaitAsync()`:
```csharp
public async Task RegisterProviderAsync(IAudioInputProvider provider)
{
    await _lock.WaitAsync();
    try { ... }
    finally { _lock.Release(); }
}
```

### 4. AppSettings has no validation for Azure configuration

```
src/BodyCam/AppSettings.cs
```

When `Provider == Azure`, no validation ensures `AzureEndpoint`, `AzureRealtimeDeploymentName` are set. Runtime failure occurs deep in WebSocket connection with a confusing error.

**Fix**: Add validation method called before connection:
```csharp
public void Validate()
{
    if (Provider == OpenAiProvider.Azure)
    {
        if (string.IsNullOrEmpty(AzureEndpoint))
            throw new InvalidOperationException("Azure endpoint required when provider is Azure");
        if (string.IsNullOrEmpty(AzureRealtimeDeploymentName))
            throw new InvalidOperationException("Azure Realtime deployment name required");
    }
    if (SampleRate < 8000 || SampleRate > 48000)
        throw new InvalidOperationException($"SampleRate {SampleRate} out of valid range (8000-48000)");
}
```

### 5. Hard-coded tool timeout (15s)

```
src/BodyCam/Tools/ToolDispatcher.cs:62
```

```csharp
timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
```

Some tools (FindObject: 30s scan, DeepAnalysis: multi-frame vision) need longer than 15s. Currently, `ToolDispatcher` imposes a blanket 15s timeout that can preempt tool-specific timeouts.

**Fix**: Add `TimeoutSeconds` to `AppSettings` or let each tool declare its own max:
```csharp
var timeout = tool.MaxExecutionSeconds ?? _settings.DefaultToolTimeoutSeconds;
timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));
```

### 6. Missing `ICameraManager` interface

```
src/BodyCam/Services/Camera/CameraManager.cs
```

`CameraManager` is a concrete class injected directly. Can't be mocked in tests, tightly couples orchestrator.

**Fix**: Extract `ICameraManager` with `CaptureFrameAsync()`, `SetActiveAsync()`, `Providers`, `InitializeAsync()`.

---

## Medium

### 7. `MemoryStore` doesn't dispose `SemaphoreSlim`

```
src/BodyCam/Services/MemoryStore.cs
```

`SemaphoreSlim` implements `IDisposable` but `MemoryStore` doesn't implement `IDisposable`/`IAsyncDisposable`. As a singleton this is low-impact, but a correctness issue.

**Fix**: Implement `IDisposable` and dispose the semaphore.

### 8. `DotEnvReader` swallows all exceptions silently

```
src/BodyCam/Services/DotEnvReader.cs:21
```

```csharp
catch { /* FileSystem not available during early init */ }
```

Acceptable for the specific case (no filesystem during early init), but should at least log at `Trace` level. If the `.env` file has a syntax error, it's silently ignored.

### 9. Hard-coded reconnection parameters

```
src/BodyCam/Orchestration/AgentOrchestrator.cs:268
```

```csharp
const int maxRetries = 5;
var delay = TimeSpan.FromSeconds(1);
delay *= 2; // exponential backoff
```

Max retries, initial delay, and backoff multiplier are all hard-coded. Different networks (mobile vs WiFi) may need different strategies.

**Fix**: Move to `AppSettings`:
```csharp
public int ReconnectMaxRetries { get; set; } = 5;
public int ReconnectInitialDelayMs { get; set; } = 1000;
public double ReconnectBackoffMultiplier { get; set; } = 2.0;
```

### 10. `VoiceInputAgent.OnAudioChunk` swallows exceptions without logging

```
src/BodyCam/Agents/VoiceInputAgent.cs:34-47
```

```csharp
private async void OnAudioChunk(object? sender, byte[] chunk)
{
    try
    {
        if (_realtime.IsConnected)
        {
            byte[] processed = _aec is not null ? _aec.ProcessCapture(chunk) : chunk;
            await _realtime.SendAudioChunkAsync(processed);
        }
    }
    catch (Exception)
    {
        // Swallow — don't crash the audio capture thread.
    }
}
```

If `SendAudioChunkAsync` consistently fails (e.g., WebSocket is in faulted state), we never learn. This should log at `Debug`/`Warning` with throttling to avoid log spam.

**Fix**: Inject `ILogger<VoiceInputAgent>`, log first/periodic failures:
```csharp
catch (Exception ex)
{
    _logger.LogDebug(ex, "Audio send failed");
}
```

### 11. No `ConfigureAwait(false)` in service code

Multiple service files (`AudioInputManager`, `AudioOutputManager`, `MemoryStore`, `AppChatClient`, `CameraManager`) `await` without `ConfigureAwait(false)`. In MAUI, these run on the UI thread's `SynchronizationContext`. If the UI thread is blocked waiting for a service call, deadlock.

Current risk is low because we don't `Task.Result`/`.Wait()` anywhere, but it's a latent hazard. Adding `ConfigureAwait(false)` to all non-UI service code is defensive best practice.

**Files to update**: All files in `Services/`, `Agents/`, `Orchestration/`, `Tools/`. Skip `ViewModels/` (needs UI context).

### 12. `AgentOrchestrator` has 13 constructor parameters

```
src/BodyCam/Orchestration/AgentOrchestrator.cs:43-57
```

13 injected dependencies signals the class has too many responsibilities. The MAF migration (M25) will reduce some, but the class should be split further.

**Potential extractions post-M25**:
- `SessionLifecycleManager` — start/stop/reconnect logic
- `AudioPipelineController` — VoiceIn/VoiceOut/AEC coordination
- Leave `AgentOrchestrator` as the thin coordinator

### 13. `StartSceneWatchTool` — background task with no cleanup guarantee

```
src/BodyCam/Tools/StartSceneWatchTool.cs
```

Scene watch starts a background monitoring loop. If the session ends while scene watch is active, there's no guaranteed cleanup. The tool should register its `CancellationToken` with the session lifecycle.

### 14. No test for reconnection logic

`AgentOrchestratorTests.cs` exists but doesn't test `ReconnectAsync()` (exponential backoff, max retries, failure fallback). This is a critical path for mobile users on unreliable networks.

### 15. `FindObjectTool` doesn't throttle vision API calls

```
src/BodyCam/Tools/FindObjectTool.cs:51
```

Scans every 3 seconds for up to 30 seconds — 10 vision API calls per invocation. No rate limiting. If user triggers multiple concurrent "find object" calls, API bill spikes.

**Fix**: Add concurrency guard or global vision API rate limiter.

---

## Low

### 16. `AsyncRelayCommand.Execute` is `async void`

```
src/BodyCam/Mvvm/AsyncRelayCommand.cs:40
```

This is by design for `ICommand.Execute()`, but unhandled exceptions in the async delegate crash the app. The command should catch and surface errors.

Currently already has try/catch — verify error surfacing is adequate.

### 17. Model names in `AppSettings` defaults may go stale

```
src/BodyCam/AppSettings.cs:13-16
```

```csharp
public string RealtimeModel { get; set; } = "gpt-realtime-1.5";
public string ChatModel { get; set; } = "gpt-5.4-mini";
public string VisionModel { get; set; } = "gpt-5.4";
public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";
```

Defaults are hard-coded. If OpenAI retires a model, new installs get an error on first run. Consider fetching latest defaults from API or settings file.

### 18. `ButtonInputManager` has no interface

```
src/BodyCam/Services/Input/ButtonInputManager.cs
```

Same as #6 (`CameraManager`) — concrete class, can't mock.

---

## Summary by Priority

| Priority | Count | Key Theme |
|---|---|---|
| Critical | 1 | `async void` non-event-handler |
| High | 5 | Swallowed exceptions, sync locks, missing validation, hard-coded timeouts, missing interface |
| Medium | 9 | Config, logging, god class, test gaps, `ConfigureAwait` |
| Low | 3 | Design nits, stale defaults |

## Suggested Execution Order

1. **Quick wins** (< 1 hour): #1, #2, #10 — fix the async void, add logging to catch blocks
2. **Before M25**: #4 (settings validation), #5 (tool timeout) — prevents runtime surprises
3. **During M25**: #12 (god class split), #3 (async locks) — natural refactor point
4. **After M25**: #6, #18 (interfaces), #11 (ConfigureAwait), #14 (reconnect tests)
5. **Backlog**: #7, #8, #9, #13, #15, #16, #17
