# Step 13: Porcupine Dispose Guard

**Priority:** P3 | **Effort:** Trivial | **Risk:** Porcupine native handle leak on StartAsync error

---

## Problem

In `PorcupineWakeWordService.StartAsync`, if `Porcupine.FromKeywordPaths` succeeds but `_audioInput.AudioChunkAvailable += OnAudioChunk` or the adapter creation fails, `_porcupine` is set but `StopAsync` may not be called (caller depends on exception behavior). The native Porcupine handle leaks.

Current code:

```csharp
_porcupine = Porcupine.FromKeywordPaths(
    accessKey, keywordPaths, sensitivities: sensitivities);

_adapter = new PorcupineAudioAdapter(_porcupine.FrameLength);

_audioInput.AudioChunkAvailable += OnAudioChunk;
IsListening = true;
```

If `PorcupineAudioAdapter` constructor throws, `_porcupine` is allocated but not disposed.

## Steps

### 13.1 Wrap StartAsync in try/catch with cleanup

**File:** `src/BodyCam/Services/WakeWord/PorcupineWakeWordService.cs`

```csharp
public Task StartAsync(CancellationToken ct = default)
{
    if (IsListening) return Task.CompletedTask;
    if (_entries.Count == 0) return Task.CompletedTask;

    var accessKey = ResolveAccessKey();
    if (string.IsNullOrWhiteSpace(accessKey))
        throw new InvalidOperationException(
            "Picovoice AccessKey not configured. Set PICOVOICE_ACCESS_KEY environment variable, "
            + "add it to .env, or configure in Settings.");

    var keywordPaths = _entries.Select(e => e.KeywordPath).ToList();
    var sensitivities = _entries.Select(e => e.Sensitivity).ToList();

    Porcupine? porcupine = null;
    try
    {
        porcupine = Porcupine.FromKeywordPaths(
            accessKey, keywordPaths, sensitivities: sensitivities);

        _adapter = new PorcupineAudioAdapter(porcupine.FrameLength);
        _porcupine = porcupine;
        porcupine = null; // Prevent dispose in finally — ownership transferred

        _audioInput.AudioChunkAvailable += OnAudioChunk;
        IsListening = true;
    }
    finally
    {
        porcupine?.Dispose(); // Only disposes if ownership was NOT transferred
    }

    return Task.CompletedTask;
}
```

The pattern: create into a local, transfer to the field only after all dependent setup succeeds, null the local to prevent `finally` from disposing it.

### 13.2 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```
