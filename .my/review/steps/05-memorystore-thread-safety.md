# Step 5: MemoryStore Thread Safety + Cache

**Priority:** P1 | **Effort:** Small | **Risk:** Data loss from concurrent access + repeated file I/O

---

## Problem

`MemoryStore` has no locking. Concurrent `SaveAsync` + `SearchAsync` can corrupt the JSON file. Every operation reloads from disk even though the in-memory `_entries` list is already populated after first load.

## Steps

### 5.1 Add SemaphoreSlim field

**File:** `src/BodyCam/Services/MemoryStore.cs`

Add field:

```csharp
private readonly SemaphoreSlim _lock = new(1, 1);
```

### 5.2 Wrap SaveAsync

```csharp
public async Task SaveAsync(MemoryEntry entry)
{
    await _lock.WaitAsync();
    try
    {
        await EnsureLoadedAsync();
        _entries.Add(entry);
        await PersistAsync();
    }
    finally { _lock.Release(); }
}
```

### 5.3 Wrap SearchAsync

```csharp
public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(string query)
{
    await _lock.WaitAsync();
    try
    {
        await EnsureLoadedAsync();
        if (string.IsNullOrWhiteSpace(query))
            return _entries.OrderByDescending(e => e.Timestamp).Take(10).ToList();

        var lower = query.ToLowerInvariant();
        return _entries
            .Where(e => e.Content.Contains(lower, StringComparison.OrdinalIgnoreCase)
                     || (e.Category?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .ToList();
    }
    finally { _lock.Release(); }
}
```

### 5.4 Wrap GetRecentAsync

```csharp
public async Task<IReadOnlyList<MemoryEntry>> GetRecentAsync(int count = 10)
{
    await _lock.WaitAsync();
    try
    {
        await EnsureLoadedAsync();
        return _entries.OrderByDescending(e => e.Timestamp).Take(count).ToList();
    }
    finally { _lock.Release(); }
}
```

### 5.5 Verify EnsureLoadedAsync only loads once

`EnsureLoadedAsync` already checks `_loaded` flag, so it only reads the file once. After first load, all operations work against the in-memory `_entries` list. The `_lock` ensures concurrent callers don't race past the `_loaded` check. **No change needed to `EnsureLoadedAsync`.**

### 5.6 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```

Verify `MemoryToolTests` still pass — they exercise Save + Search through the full integration host.
