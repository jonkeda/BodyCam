# Step 4: WebSocket Reconnection

**Priority:** P1 | **Effort:** Medium | **Risk:** Silent session death on network hiccup

---

## Problem

When the WebSocket drops (network switch, server timeout), `ReceiveLoop` exits, `IsConnected` becomes false, and the user hears silence. No reconnection attempt, no user notification.

## Steps

### 4.1 Add ConnectionLost event to RealtimeClient

**File:** `src/BodyCam/Services/RealtimeClient.cs`

Add event declaration near the other events:

```csharp
public event EventHandler<string>? ConnectionLost;
```

### 4.2 Fire ConnectionLost in ReceiveLoop

In the `ReceiveLoopAsync` method, update the `finally` block:

```csharp
finally
{
    var wasConnected = IsConnected;
    IsConnected = false;
    if (wasConnected)
        ConnectionLost?.Invoke(this, "WebSocket connection lost");
}
```

And in the catch block for non-cancellation exceptions:

```csharp
catch (Exception ex)
{
    ErrorOccurred?.Invoke(this, $"Receive loop error: {ex.Message}");
    // ConnectionLost will fire in finally block
}
```

### 4.3 Add ConnectionLost to IRealtimeClient interface

**File:** `src/BodyCam/Services/IRealtimeClient.cs`

Add to the interface:

```csharp
event EventHandler<string>? ConnectionLost;
```

### 4.4 Add reconnection logic to AgentOrchestrator

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs`

Add a reconnect method:

```csharp
private async Task ReconnectAsync()
{
    var delay = TimeSpan.FromSeconds(1);
    const int maxRetries = 5;

    for (int i = 0; i < maxRetries; i++)
    {
        DebugLog?.Invoke(this, $"Reconnecting ({i + 1}/{maxRetries})...");
        try
        {
            await _realtime.ConnectAsync(_cts?.Token ?? CancellationToken.None);
            await _realtime.UpdateSessionAsync(_cts?.Token ?? CancellationToken.None);
            await _voiceIn.StartAsync(_cts?.Token ?? CancellationToken.None);
            DebugLog?.Invoke(this, "Reconnected.");
            return;
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke(this, $"Reconnect failed: {ex.Message}");
            await Task.Delay(delay);
            delay *= 2; // exponential backoff: 1s, 2s, 4s, 8s, 16s
        }
    }

    DebugLog?.Invoke(this, "Reconnection failed after 5 attempts. Stopping session.");
    await StopAsync();
}
```

### 4.5 Subscribe to ConnectionLost in StartAsync

In `StartAsync`, after subscribing to other events:

```csharp
_realtime.ConnectionLost += OnConnectionLost;
```

In `StopAsync`, unsubscribe:

```csharp
_realtime.ConnectionLost -= OnConnectionLost;
```

Add the handler:

```csharp
private async void OnConnectionLost(object? sender, string reason)
{
    try
    {
        DebugLog?.Invoke(this, $"Connection lost: {reason}");
        await ReconnectAsync();
    }
    catch (Exception ex)
    {
        DebugLog?.Invoke(this, $"Reconnect handler error: {ex.Message}");
    }
}
```

### 4.6 Update NSubstitute mock in tests

If `IRealtimeClient` mock in tests now requires `ConnectionLost` event, NSubstitute handles it automatically. No test changes needed.

### 4.7 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```
