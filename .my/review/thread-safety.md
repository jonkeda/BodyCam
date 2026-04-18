# Thread Safety Review

## Summary

Several core services lack synchronization primitives. The app currently works because most calls are serialized through the UI thread, but hot-plug events (Bluetooth connect/disconnect) arrive on background threads and can race with user-initiated actions.

---

## 1. AudioInputManager / AudioOutputManager

**Risk: High**

Both managers maintain mutable state (`_providers` list, `Active` provider reference) with no locking. Hot-plug methods (`RegisterProvider`, `UnregisterProviderAsync`) are called from device notification threads (MMNotificationClient on Windows, BroadcastReceiver on Android).

**Race scenario:**
```
Thread A (BT notification): RegisterProvider(btMic)
  → _providers.Add(btMic)
  → checks Active.ProviderId
  
Thread B (user tap Settings): SetActiveAsync("platform-mic")
  → Active = platformMic
  → calls StopAsync on old provider

→ Thread A reads stale Active reference → auto-switch logic uses wrong provider
```

**Proposed fix:**
```csharp
private readonly SemaphoreSlim _lock = new(1, 1);

public async Task SetActiveAsync(string providerId)
{
    await _lock.WaitAsync();
    try
    {
        // existing logic
    }
    finally { _lock.Release(); }
}

public void RegisterProvider(IAudioInputProvider provider)
{
    _lock.Wait();
    try
    {
        // existing logic
    }
    finally { _lock.Release(); }
}
```

Use `SemaphoreSlim` (not `lock`) because `SetActiveAsync` is async.

---

## 2. MemoryStore

**Risk: Medium**

`SaveAsync` reads the file, deserializes, appends, re-serializes, and writes back. `SearchAsync` reads the same file. No locking means concurrent calls can:
- Lose entries (read-modify-write race)
- Corrupt JSON (partial write + concurrent read)

**Proposed fix:**
```csharp
private readonly SemaphoreSlim _fileLock = new(1, 1);

public async Task SaveAsync(MemoryEntry entry)
{
    await _fileLock.WaitAsync();
    try { /* read, append, write */ }
    finally { _fileLock.Release(); }
}
```

Also consider keeping entries in memory after first load (see [performance.md](performance.md)).

---

## 3. AgentOrchestrator Event Handlers

**Risk: Medium**

`OnFunctionCallReceived` runs tool execution and sends the result back. If two function calls arrive in rapid succession (e.g., the model calls `describe_scene` and `recall_memory` in parallel), both execute concurrently. Most tools are stateless, but:
- `DescribeSceneTool` and `ReadTextTool` both call `CameraManager.CaptureFrameAsync()` → concurrent camera access
- `SaveMemoryTool` hits the unprotected MemoryStore

**Proposed fix:** Serialize tool execution within the orchestrator:
```csharp
private readonly SemaphoreSlim _toolLock = new(1, 1);

private async Task OnFunctionCallReceived(string callId, string name, string args)
{
    await _toolLock.WaitAsync();
    try
    {
        var result = await _toolDispatcher.ExecuteAsync(name, args, context, _cts.Token);
        await _realtime.SendFunctionCallOutputAsync(callId, result);
    }
    finally { _toolLock.Release(); }
}
```

---

## 4. MainViewModel State Transitions

**Risk: Low**

`SetLayerAsync` is not guarded. Rapid button presses could trigger concurrent transitions (e.g., double-tap on Ask while first transition is still connecting). The orchestrator's `StartAsync`/`StopAsync` pair could overlap.

**Proposed fix:** Guard with a boolean flag:
```csharp
private bool _isTransitioning;

private async Task SetLayerAsync(string segment)
{
    if (_isTransitioning) return;
    _isTransitioning = true;
    try { /* existing logic */ }
    finally { _isTransitioning = false; }
}
```

This is simpler than a lock since `SetLayerAsync` is always called on the UI thread.

---

## 5. GestureRecognizer

**Risk: None** — Already uses `lock` for state dictionary access. Well-implemented.

---

## Priority

| Fix | Effort | Impact |
|-----|--------|--------|
| Audio manager locking | Small | Prevents BT hot-plug races |
| MemoryStore locking | Small | Prevents data loss |
| Tool execution serialization | Small | Prevents concurrent camera/memory access |
| ViewModel transition guard | Trivial | Prevents double-start |
