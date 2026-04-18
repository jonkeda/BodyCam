# Step 7: SetLayerAsync State Revert

**Priority:** P1 | **Effort:** Trivial | **Risk:** UI shows "Active" when session start failed

---

## Problem

In `MainViewModel.SetLayerAsync`, `CurrentLayer` is set at the bottom of the method, but `ToggleButtonText = "Stop"` and `StatusText = "Connecting..."` are set **before** `_orchestrator.StartAsync()`. If `StartAsync` throws, the code hits the `catch` which resets `IsRunning` and `ToggleButtonText`, but `CurrentLayer` is never set (because `return` skips the assignment). However, `StatusText` stays at "Connecting..." and is never reset.

Additionally, there's no guard against double-clicking while a layer transition is in progress.

## Steps

### 7.1 Fix StatusText in catch block

**File:** `src/BodyCam/ViewModels/MainViewModel.cs`

In `SetLayerAsync`, update the catch block for the ActiveSession escalation:

```csharp
catch (Exception ex)
{
    IsRunning = false;
    ToggleButtonText = "Start";
    StatusText = "Ready";  // <-- Add this line
    DebugLog += $"[{DateTime.Now:HH:mm:ss}] Start failed: {ex.Message}{Environment.NewLine}";
    return;
}
```

### 7.2 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```
