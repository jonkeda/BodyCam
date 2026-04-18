# Step 1: Add SemaphoreSlim to Audio Managers

**Priority:** P0 | **Effort:** Small | **Risk:** BT hot-plug race conditions

---

## Problem

`AudioInputManager` and `AudioOutputManager` have no locking. `RegisterProvider` / `UnregisterProviderAsync` are called from BT notification threads (MMNotificationClient on Windows, BroadcastReceiver on Android) while `SetActiveAsync` can be called from the UI thread simultaneously.

## Steps

### 1.1 AudioInputManager — Add SemaphoreSlim field

**File:** `src/BodyCam/Services/Audio/AudioInputManager.cs`

Add field after existing fields:

```csharp
private readonly SemaphoreSlim _lock = new(1, 1);
```

### 1.2 AudioInputManager — Wrap SetActiveAsync

Wrap the body of `SetActiveAsync` in `_lock.WaitAsync()` / `_lock.Release()`:

```csharp
public async Task SetActiveAsync(string providerId, CancellationToken ct = default)
{
    await _lock.WaitAsync(ct);
    try
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider is null) return;

        if (_active is not null)
        {
            _active.AudioChunkAvailable -= OnProviderChunk;
            _active.Disconnected -= OnProviderDisconnected;
            await _active.StopAsync();
        }

        _active = provider;
        _settings.ActiveAudioInputProvider = providerId;
        _active.AudioChunkAvailable += OnProviderChunk;
        _active.Disconnected += OnProviderDisconnected;
    }
    finally { _lock.Release(); }
}
```

### 1.3 AudioInputManager — Wrap RegisterProvider

```csharp
public void RegisterProvider(IAudioInputProvider provider)
{
    _lock.Wait();
    try
    {
        if (_providers.Any(p => p.ProviderId == provider.ProviderId))
            return;

        _providers.Add(provider);
        ProvidersChanged?.Invoke(this, EventArgs.Empty);

        if (provider.ProviderId == _settings.ActiveAudioInputProvider
            && (_active is null || _active.ProviderId == "platform"))
        {
            _ = SetActiveAsync(provider.ProviderId);
        }
    }
    finally { _lock.Release(); }
}
```

### 1.4 AudioInputManager — Wrap UnregisterProviderAsync

```csharp
public async Task UnregisterProviderAsync(string providerId)
{
    await _lock.WaitAsync();
    try
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider is null) return;

        if (_active?.ProviderId == providerId)
            await FallbackToPlatformAsync();

        _providers.Remove(provider);
        await provider.DisposeAsync();
        ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }
    finally { _lock.Release(); }
}
```

### 1.5 AudioOutputManager — Same pattern

**File:** `src/BodyCam/Services/Audio/AudioOutputManager.cs`

Repeat steps 1.1–1.4 for `AudioOutputManager`:
- Add `private readonly SemaphoreSlim _lock = new(1, 1);`
- Wrap `SetActiveAsync` body in `_lock.WaitAsync()` / `finally { _lock.Release(); }`
- Wrap `RegisterProvider` body in `_lock.Wait()` / `finally { _lock.Release(); }`
- Wrap `UnregisterProviderAsync` body in `_lock.WaitAsync()` / `finally { _lock.Release(); }`

### 1.6 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```

Verify all existing tests pass — especially `AudioOutputManagerHotPlugTests` and `AudioInputManagerTests`.
