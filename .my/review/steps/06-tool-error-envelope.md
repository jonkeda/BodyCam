# Step 6: ToolDispatcher Error Envelope + Typed Arguments

**Priority:** P1 | **Effort:** Small | **Risk:** Invalid JSON crashes Realtime API pipeline

---

## Problem

1. `ToolDispatcher.ExecuteAsync` and `ITool.ExecuteAsync` accept `string? argumentsJson`. Each tool re-parses this raw string independently in `ToolBase<TArgs>`. Malformed JSON isn't caught until inside the tool.
2. If a tool throws `JsonException`, it propagates to `AgentOrchestrator.OnFunctionCallReceived`. The orchestrator catches it, but the tool name and context are lost.

Better: parse `string?` → `JsonElement?` once at the dispatcher boundary, pass the validated element to tools, and catch parse errors in one place.

## Steps

### 6.1 Add ToolErrorResult type class

**File:** `src/BodyCam/Tools/ToolDispatcher.cs`

Add a typed error response class and source-gen context entry:

```csharp
public class ToolErrorResult
{
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string Error { get; set; } = "";
}

[System.Text.Json.Serialization.JsonSerializable(typeof(ToolErrorResult))]
internal partial class ToolJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
```

### 6.2 Change ITool.ExecuteAsync signature to accept JsonElement?

**File:** `src/BodyCam/Tools/ITool.cs`

```csharp
// Before
Task<ToolResult> ExecuteAsync(string? argumentsJson, ToolContext context, CancellationToken ct);

// After
Task<ToolResult> ExecuteAsync(JsonElement? arguments, ToolContext context, CancellationToken ct);
```

Add `using System.Text.Json;` to the file.

### 6.3 Update ToolBase\<TArgs\> to deserialize from JsonElement?

**File:** `src/BodyCam/Tools/ToolBase.cs`

```csharp
// Before
public async Task<ToolResult> ExecuteAsync(string? argumentsJson, ToolContext context, CancellationToken ct)
{
    TArgs args;
    if (string.IsNullOrWhiteSpace(argumentsJson))
    {
        args = new TArgs();
    }
    else
    {
        args = JsonSerializer.Deserialize<TArgs>(argumentsJson, JsonOptions) ?? new TArgs();
    }

    return await ExecuteAsync(args, context, ct);
}

// After
public async Task<ToolResult> ExecuteAsync(JsonElement? arguments, ToolContext context, CancellationToken ct)
{
    TArgs args;
    if (arguments is null || arguments.Value.ValueKind == JsonValueKind.Undefined)
    {
        args = new TArgs();
    }
    else
    {
        args = arguments.Value.Deserialize<TArgs>(JsonOptions) ?? new TArgs();
    }

    return await ExecuteAsync(args, context, ct);
}
```

### 6.4 Parse string → JsonElement? in ToolDispatcher.ExecuteAsync

**File:** `src/BodyCam/Tools/ToolDispatcher.cs`

Parse the raw string at the boundary — malformed JSON is caught here:

```csharp
public async Task<string> ExecuteAsync(
    string toolName, string? argumentsJson, ToolContext context, CancellationToken ct)
{
    if (!_tools.TryGetValue(toolName, out var tool))
    {
        return SerializeError($"Unknown function: {toolName}");
    }

    if (!tool.IsEnabled)
    {
        return SerializeError($"Tool '{toolName}' is currently disabled.");
    }

    // Parse raw JSON string → typed element at the boundary
    JsonElement? arguments = null;
    if (!string.IsNullOrWhiteSpace(argumentsJson))
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            arguments = doc.RootElement.Clone(); // Clone so doc can be disposed
        }
        catch (JsonException ex)
        {
            return SerializeError($"Invalid arguments for '{toolName}': {ex.Message}");
        }
    }

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

    try
    {
        var result = await tool.ExecuteAsync(arguments, context, timeoutCts.Token);
        return result.Json;
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        return SerializeError($"Tool '{toolName}' timed out after 15 seconds.");
    }
}

private static string SerializeError(string message) =>
    JsonSerializer.Serialize(new ToolErrorResult { Error = message },
        ToolJsonContext.Default.ToolErrorResult);
```

Note: `JsonException` is no longer caught around `tool.ExecuteAsync` because malformed JSON is now caught above. Tools receive a pre-validated `JsonElement?`.

### 6.5 Update any direct ITool implementations

If any tool implements `ITool` directly (not via `ToolBase<TArgs>`), update its `ExecuteAsync` signature from `string?` to `JsonElement?`.

### 6.6 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```

### 6.7 Optional: Add unit test

Add test in `BodyCam.Tests` that passes invalid JSON arguments to `ToolDispatcher.ExecuteAsync` and verifies the error envelope is returned rather than throwing.
