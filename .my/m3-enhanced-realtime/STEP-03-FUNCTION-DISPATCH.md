# Step 3: Wire Function Dispatch in AgentOrchestrator

Subscribe to `FunctionCallReceived` and route function calls to the appropriate agents.

## Depends On: Steps 2, 4, 5

## Files Modified

### 1. `src/BodyCam/Orchestration/AgentOrchestrator.cs`

**Subscribe** to `FunctionCallReceived` in `StartAsync`:

```csharp
// In StartAsync, add after existing subscriptions:
_realtime.FunctionCallReceived += OnFunctionCallReceived;
```

**Unsubscribe** in `StopAsync`:

```csharp
// In StopAsync, add:
_realtime.FunctionCallReceived -= OnFunctionCallReceived;
```

**Add** the function call handler:

```csharp
private async void OnFunctionCallReceived(object? sender, FunctionCallInfo info)
{
    DebugLog?.Invoke(this, $"Function call: {info.Name}({info.Arguments})");

    try
    {
        var result = info.Name switch
        {
            "describe_scene" => await ExecuteDescribeSceneAsync(),
            "deep_analysis" => await ExecuteDeepAnalysisAsync(info.Arguments),
            _ => System.Text.Json.JsonSerializer.Serialize(new { error = $"Unknown function: {info.Name}" })
        };

        await _realtime.SendFunctionCallOutputAsync(info.CallId, result);
        DebugLog?.Invoke(this, $"Function result sent for {info.Name}");
    }
    catch (Exception ex)
    {
        DebugLog?.Invoke(this, $"Function call error ({info.Name}): {ex.Message}");

        try
        {
            await _realtime.SendFunctionCallOutputAsync(
                info.CallId,
                System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message }));
        }
        catch (Exception sendEx)
        {
            DebugLog?.Invoke(this, $"Failed to send error result: {sendEx.Message}");
        }
    }
}
```

**Add** tool execution methods:

```csharp
private async Task<string> ExecuteDescribeSceneAsync()
{
    var description = await _vision.CaptureAndDescribeAsync();
    return System.Text.Json.JsonSerializer.Serialize(new
    {
        description = description ?? "Camera not available or no frame captured."
    });
}

private async Task<string> ExecuteDeepAnalysisAsync(string argumentsJson)
{
    using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
    var root = doc.RootElement;

    var query = root.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
    var context = root.TryGetProperty("context", out var c) ? c.GetString() : null;

    var result = await _conversation.AnalyzeAsync(query, context);
    return System.Text.Json.JsonSerializer.Serialize(new { analysis = result });
}
```

### Design Notes

- **Error handling:** If a function fails, we still send a function_call_output with an error message. The model needs the output to continue generating. Swallowing the error would leave the API hanging.
- **No CancellationToken:** Function calls from the Realtime API don't have a natural cancellation point. If the user interrupts (SpeechStarted), the Realtime API will cancel the response itself. The function call is already complete by then.
- **Async void:** `OnFunctionCallReceived` is an event handler (async void is acceptable). Errors are caught and logged.

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore -v q
```
