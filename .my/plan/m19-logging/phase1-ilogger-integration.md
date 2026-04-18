# M19 Phase 1 — Core ILogger Integration

**Status:** NOT STARTED  
**Depends on:** None

---

## Goal

Replace the ad-hoc `DebugLog` event/string pattern with `Microsoft.Extensions.Logging.ILogger<T>` throughout the codebase. Create a custom `InAppLoggerProvider` that feeds the debug overlay UI. Remove the `AgentOrchestrator.DebugLog` event entirely.

---

## Wave 1: InAppLoggerProvider

### 1.1 `InAppLogEntry` Model

```
Services/Logging/InAppLogEntry.cs
```

```csharp
public record InAppLogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,    // e.g. "BodyCam.Orchestration.AgentOrchestrator"
    string Message,
    Exception? Exception);
```

### 1.2 `InAppLogSink` (Ring Buffer)

```
Services/Logging/InAppLogSink.cs
```

Thread-safe ring buffer holding last 500 entries. Fires `EntryAdded` event when new entry arrives.

```csharp
public class InAppLogSink
{
    private readonly InAppLogEntry[] _buffer = new InAppLogEntry[500];
    private int _head;
    private int _count;
    private readonly object _lock = new();

    public event EventHandler<InAppLogEntry>? EntryAdded;

    public void Add(InAppLogEntry entry) { /* lock, append, fire event */ }
    public IReadOnlyList<InAppLogEntry> GetEntries() { /* snapshot */ }
    public void Clear() { /* lock, reset */ }
}
```

### 1.3 `InAppLoggerProvider` + `InAppLogger`

```
Services/Logging/InAppLoggerProvider.cs
```

Implements `ILoggerProvider` + `ILogger`. Writes to `InAppLogSink`.

```csharp
public class InAppLoggerProvider : ILoggerProvider
{
    private readonly InAppLogSink _sink;
    public InAppLoggerProvider(InAppLogSink sink) => _sink = sink;
    public ILogger CreateLogger(string categoryName) => new InAppLogger(categoryName, _sink);
    public void Dispose() { }
}

internal class InAppLogger : ILogger
{
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
    {
        _sink.Add(new InAppLogEntry(DateTimeOffset.Now, level, _category, formatter(state, ex), ex));
    }
    // IsEnabled: filter by configured minimum level
    // BeginScope: no-op (or pass-through)
}
```

---

## Wave 2: Migrate AgentOrchestrator

### 2.1 Inject ILogger

```csharp
public class AgentOrchestrator
{
    private readonly ILogger<AgentOrchestrator> _logger;
    
    public AgentOrchestrator(..., ILogger<AgentOrchestrator> logger)
    {
        _logger = logger;
    }
}
```

### 2.2 Replace All DebugLog Calls

Map each call to an appropriate log level:

| Current Call | Level | Replacement |
|-------------|-------|-------------|
| `"Model: {model}"` | Information | `_logger.LogInformation("Realtime model: {Model}", model)` |
| `"Realtime connected."` | Information | `_logger.LogInformation("Realtime connected")` |
| `"Audio pipeline started."` | Information | `_logger.LogInformation("Audio pipeline started")` |
| `"Orchestrator stopped."` | Information | `_logger.LogInformation("Orchestrator stopped")` |
| `"Playback error: {ex}"` | Error | `_logger.LogError(ex, "Playback error")` |
| `"AI said: {text}"` | Debug | `_logger.LogDebug("AI transcript: {Length} chars", text.Length)` |
| `"User said: {text}"` | Debug | `_logger.LogDebug("User transcript received")` |
| `"Interrupted at {ms}ms"` | Debug | `_logger.LogDebug("Interrupted at {PlayedMs}ms", ms)` |
| `"Truncation error: {ex}"` | Warning | `_logger.LogWarning(ex, "Truncation error")` |
| `"Response complete: {id}"` | Debug | `_logger.LogDebug("Response complete: {ResponseId}", id)` |
| `"Realtime error: {err}"` | Error | `_logger.LogError("Realtime error: {Error}", err)` |
| `"Wake word: {kw}"` | Information | `_logger.LogInformation("Wake word: {Keyword} ({Action})", kw, action)` |
| `"Function call: {name}"` | Information | `_logger.LogInformation("Function call: {ToolName}", name)` |
| `"Function result sent"` | Debug | `_logger.LogDebug("Function result sent for {ToolName}", name)` |
| `"Function call error"` | Error | `_logger.LogError(ex, "Function call error ({ToolName})", name)` |

### 2.3 Remove DebugLog Event

Delete `public event EventHandler<string>? DebugLog;` from `AgentOrchestrator`.

---

## Wave 3: Migrate MainViewModel + Other Services

### 3.1 MainViewModel

- Remove `DebugLog` string property (replaced by `InAppLogSink`)
- Add `InAppLogSink` injection
- Bind debug overlay to `InAppLogSink.GetEntries()` or formatted string from sink
- Replace `DebugLog +=` error logging with `ILogger<MainViewModel>`

```csharp
public class MainViewModel : ViewModelBase
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly InAppLogSink _logSink;

    // Debug overlay reads from _logSink
    public string DebugLog => _logSink.GetFormattedLog(); // or bind to collection
}
```

Subscribe to `_logSink.EntryAdded` → `OnPropertyChanged(nameof(DebugLog))`.

### 3.2 Add ILogger to Key Services

| Service | Logger Type | What to Log |
|---------|------------|-------------|
| `RealtimeClient` | `ILogger<RealtimeClient>` | Connect/disconnect, send/receive errors, reconnect |
| `AudioInputManager` | `ILogger<AudioInputManager>` | Provider switches, hot-plug, start/stop |
| `AudioOutputManager` | `ILogger<AudioOutputManager>` | Provider switches, hot-plug, playback errors |
| `CameraManager` | `ILogger<CameraManager>` | Capture attempts, failures, provider switches |
| `ButtonInputManager` | `ILogger<ButtonInputManager>` | Gesture recognized, action dispatched |
| `ToolDispatcher` | `ILogger<ToolDispatcher>` | Tool execution (name, duration, result) |
| `PorcupineWakeWordService` | `ILogger<PorcupineWakeWordService>` | Start/stop, detection events |

### 3.3 ToolContext.Log Migration

`ToolContext.Log` is currently `Action<string>`. Replace with `ILogger`:

```csharp
public ILogger Logger { get; init; }
```

Update all tool implementations to use `context.Logger.LogInformation(...)`.

---

## Wave 4: MauiProgram.cs Logging Pipeline

### 4.1 Configure Logging

```csharp
builder.Logging
    .SetMinimumLevel(LogLevel.Debug)
    .AddProvider(new InAppLoggerProvider(logSink));
```

### 4.2 Register InAppLogSink

```csharp
var logSink = new InAppLogSink();
builder.Services.AddSingleton(logSink);
```

### 4.3 Test Infrastructure

Update `BodyCamTestHost` to configure `ILogger` (use `NullLoggerFactory` or capture logs for test assertions).

---

## Wave 5: Tests

| Test | File |
|------|------|
| Ring buffer stores entries | `InAppLogSinkTests.cs` |
| Ring buffer wraps at capacity | `InAppLogSinkTests.cs` |
| Ring buffer fires EntryAdded | `InAppLogSinkTests.cs` |
| Clear resets buffer | `InAppLogSinkTests.cs` |
| InAppLogger writes to sink | `InAppLoggerProviderTests.cs` |
| InAppLogger respects minimum level | `InAppLoggerProviderTests.cs` |
| GetEntries returns snapshot | `InAppLogSinkTests.cs` |
| Thread safety under concurrent writes | `InAppLogSinkTests.cs` |

---

## Migration Checklist

- [ ] Create `InAppLogEntry`, `InAppLogSink`, `InAppLoggerProvider`
- [ ] Add `ILogger<AgentOrchestrator>` — replace 20+ DebugLog calls
- [ ] Remove `AgentOrchestrator.DebugLog` event
- [ ] Update `MainViewModel` — consume `InAppLogSink` instead of event
- [ ] Add `ILogger<T>` to RealtimeClient, managers, ToolDispatcher
- [ ] Update `ToolContext.Log` → `ILogger`
- [ ] Configure logging in `MauiProgram.cs`
- [ ] Update `BodyCamTestHost` for ILogger
- [ ] Unit tests for InAppLogSink + InAppLoggerProvider
- [ ] Build + run all tests

## Exit Criteria

1. Zero `DebugLog?.Invoke()` calls remain
2. All logging goes through `ILogger<T>` with appropriate levels
3. Debug overlay still shows logs (fed by `InAppLogSink`)
4. Ring buffer prevents unbounded memory growth
5. All existing tests pass + new logging tests pass
