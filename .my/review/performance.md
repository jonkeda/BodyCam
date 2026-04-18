# Performance Review

## Summary

The app is real-time audio focused, so latency matters. Most hot paths (audio chunk forwarding, WebSocket send/receive) are fast. The main concerns are in secondary paths: memory store I/O, session context trimming, and cold startup.

---

## 1. MemoryStore — Full File Reload Per Operation

**Risk: Medium (at scale)**

Every `SaveAsync` call reads the entire JSON file, deserializes all entries, appends one, re-serializes, and writes back. Every `SearchAsync` reads and deserializes the full file.

**Current scale:** Likely <100 entries. No measurable impact.

**At scale (1000+ entries):**
- Each save: ~2 file I/O ops + 2 full deserializations
- Each search: ~1 file I/O + 1 deserialization + linear scan
- Concurrent saves: race condition (see thread-safety.md)

**Proposed fix — In-memory cache with write-through:**
```csharp
private List<MemoryEntry>? _cache;

private async Task<List<MemoryEntry>> GetEntriesAsync()
{
    if (_cache is not null) return _cache;
    _cache = await LoadFromFileAsync();
    return _cache;
}

public async Task SaveAsync(MemoryEntry entry)
{
    var entries = await GetEntriesAsync();
    entries.Add(entry);
    await WriteToFileAsync(entries); // write-through
}

public async Task<List<MemoryEntry>> SearchAsync(string query)
{
    var entries = await GetEntriesAsync();
    return entries.Where(e => Matches(e, query)).ToList();
}
```

Eliminates repeated file I/O. Cache invalidation isn't needed — the app is the only writer.

---

## 2. SessionContext.GetTrimmedHistory — Repeated Walks

**Risk: Low**

`GetTrimmedHistory` walks the message list backward to fit within a ~16KB character budget. This runs each time a new message is sent to Chat Completions (ConversationAgent, VisionAgent).

**Current scale:** Typically <50 messages per session. Sub-millisecond.

**At scale (100+ messages):** Still fast — it's a linear scan with string length checks. No fix needed unless profiling shows otherwise.

---

## 3. Cold Startup — All Services Initialized Upfront

**Risk: Low**

All 12 tools, 4 agents, wake word service, and both audio managers are registered as singletons and resolved at startup. The actual cost is construction only (no I/O in constructors), so this is fast.

**Potential concern:** `PorcupineWakeWordService` loads native Porcupine library at construction time. On cold start, this adds ~200-400ms.

**Proposed fix (if startup becomes an issue):** Use `Lazy<T>` for wake word service:
```csharp
services.AddSingleton<Lazy<IWakeWordService>>(sp => 
    new Lazy<IWakeWordService>(() => sp.GetRequiredService<PorcupineWakeWordService>()));
```

Only initialize Porcupine when wake word mode is first entered.

---

## 4. JSON Parsing on WebSocket Receive Thread

**Risk: Low**

`RealtimeClient.ReceiveLoop` parses JSON on the receive thread. For audio delta messages (frequent, small), this is fast. For large response.done messages with usage metadata, parsing takes longer but doesn't block the UI (it's on a background thread).

No fix needed. If latency becomes measurable, consider parsing audio deltas with a fast path (avoid full JSON deserialization for known message shapes).

---

## 5. VisionAgent 5-Second Cooldown Cache

**Risk: None (by design)**

The 5-second cooldown on `DescribeFrameAsync` prevents repeated API calls when the model calls `describe_scene` multiple times in rapid succession. This is intentional rate-limiting, not a performance concern.

**Note:** Consider making the cooldown configurable if use cases require faster re-description.

---

## 6. ToolDispatcher.GetToolDefinitions

**Risk: None**

Rebuilds the function definition array on each call. With 12 tools, this is negligible. Called once per session start.

---

## Priority

| Fix | Effort | Impact |
|-----|--------|--------|
| MemoryStore in-memory cache | Small | Eliminates repeated file I/O |
| Lazy wake word initialization | Trivial | Faster cold start if not using wake words |
| Everything else | — | No action needed |
