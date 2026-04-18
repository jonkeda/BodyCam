# Step 12: Lazy Wake Word Initialization

**Priority:** P3 | **Effort:** Small | **Risk:** Startup cost from Porcupine init when wake word is never used

---

## Problem

`PorcupineWakeWordService` is registered as singleton and resolved at startup in the `AgentOrchestrator` constructor. Even users who never enable wake word pay the DI resolution cost. While `Porcupine.FromKeywordPaths` is only called in `StartAsync` (lazy by nature), we can use `Lazy<T>` at the DI level to defer even the service construction.

## Steps

### 12.1 Use Lazy<T> registration in MauiProgram

**File:** `src/BodyCam/MauiProgram.cs` (or `ServiceExtensions.cs` if Step 9 was done)

Replace:

```csharp
builder.Services.AddSingleton<IWakeWordService, BodyCam.Services.WakeWord.PorcupineWakeWordService>();
```

With:

```csharp
builder.Services.AddSingleton<PorcupineWakeWordService>();
builder.Services.AddSingleton<IWakeWordService>(sp => sp.GetRequiredService<PorcupineWakeWordService>());
```

This doesn't change behavior, but makes it explicit. The real benefit would come from making `AgentOrchestrator` take `Lazy<IWakeWordService>`:

### 12.2 Change AgentOrchestrator to use Lazy<IWakeWordService>

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs`

Change the field and constructor parameter:

```csharp
private readonly Lazy<IWakeWordService> _wakeWord;

public AgentOrchestrator(
    // ... other params ...
    Lazy<IWakeWordService> wakeWord,
    // ... other params ...)
{
    _wakeWord = wakeWord;
}
```

Update all usages of `_wakeWord` to `_wakeWord.Value`:

- `StartListeningAsync`: `_wakeWord.Value.WakeWordDetected += ...`
- `StopListeningAsync`: `_wakeWord.Value.WakeWordDetected -= ...`
- `StartListeningAsync`: `await _wakeWord.Value.StartAsync(...)`
- `StopListeningAsync`: `await _wakeWord.Value.StopAsync()`

### 12.3 Register Lazy<IWakeWordService>

**File:** `src/BodyCam/MauiProgram.cs` (or `ServiceExtensions.cs`)

```csharp
builder.Services.AddSingleton(sp =>
    new Lazy<IWakeWordService>(() => sp.GetRequiredService<IWakeWordService>()));
```

### 12.4 Update test mocks

Any test that creates `AgentOrchestrator` with a mock `IWakeWordService` needs to wrap it:

```csharp
new Lazy<IWakeWordService>(() => mockWakeWord)
```

### 12.5 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```

**Note:** This is a low-priority optimization. The actual Porcupine engine creation only happens in `StartAsync`, so the startup cost is already minimal. Consider deferring this change unless profiling shows measurable startup impact.
