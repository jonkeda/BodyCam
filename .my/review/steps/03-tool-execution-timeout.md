# Step 3: Add Tool Execution Timeout

**Priority:** P0 | **Effort:** Trivial | **Risk:** Hung responses from slow tools

---

## Problem

`ToolDispatcher.ExecuteAsync` has no timeout. If a tool hangs (e.g., `VisionAgent.DescribeFrameAsync` waiting on a slow API), the Realtime API response pipeline blocks indefinitely.

## Steps

### 3.1 Add timeout to ToolDispatcher.ExecuteAsync

**File:** `src/BodyCam/Tools/ToolDispatcher.cs`

Replace the current `ExecuteAsync` method:

```csharp
public async Task<string> ExecuteAsync(
    string toolName, string? argumentsJson, ToolContext context, CancellationToken ct)
{
    if (!_tools.TryGetValue(toolName, out var tool))
    {
        return JsonSerializer.Serialize(new { error = $"Unknown function: {toolName}" });
    }

    if (!tool.IsEnabled)
    {
        return JsonSerializer.Serialize(new { error = $"Tool '{toolName}' is currently disabled." });
    }

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

    try
    {
        var result = await tool.ExecuteAsync(argumentsJson, context, timeoutCts.Token);
        return result.Json;
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        return JsonSerializer.Serialize(new { error = $"Tool '{toolName}' timed out after 15 seconds." });
    }
}
```

Key: The `when (!ct.IsCancellationRequested)` clause distinguishes a timeout from the caller's cancellation. If the caller cancelled, we re-throw. If it was our 15s timeout, we return an error to the model.

### 3.2 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```
