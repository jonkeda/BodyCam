# Step 11: Layer Transition Guard

**Priority:** P2 | **Effort:** Trivial | **Risk:** Double-click races in SetLayerAsync

---

## Problem

If the user double-taps "Active" quickly, two `SetLayerAsync("Active")` calls race. The first enters `_orchestrator.StartAsync()`, the second passes the `target == CurrentLayer` guard (CurrentLayer is still Sleep). Two `StartAsync` calls run concurrently, potentially creating duplicate WebSocket connections.

## Steps

### 11.1 Add _isTransitioning guard

**File:** `src/BodyCam/ViewModels/MainViewModel.cs`

Add field:

```csharp
private bool _isTransitioning;
```

### 11.2 Guard SetLayerAsync

At the top of `SetLayerAsync`:

```csharp
private async Task SetLayerAsync(string segment)
{
    if (_isTransitioning) return;
    _isTransitioning = true;
    try
    {
        // ... existing method body ...
    }
    finally { _isTransitioning = false; }
}
```

Wrap the existing method body (from `var target = segment switch` through `StatusText = target switch { ... }`) inside the try block.

### 11.3 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```
