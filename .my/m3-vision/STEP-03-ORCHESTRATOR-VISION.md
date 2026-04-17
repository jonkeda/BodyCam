# Step 3: Orchestrator Vision Triggers

Wire automatic camera start/stop and periodic background capture into the orchestrator. The camera starts with the session and the orchestrator pre-caches vision context in the background so it's ready when the model calls `describe_scene`.

## Files Modified

### 1. `src/BodyCam/Orchestration/AgentOrchestrator.cs`

**Add** camera lifecycle management to `StartAsync`/`StopAsync`. **Add** optional periodic capture for background scene awareness.

#### StartAsync — start camera after audio pipeline

```csharp
// AFTER existing "Audio pipeline started." line, ADD:

        // Start camera
        try
        {
            await _vision.Camera.StartAsync(_cts.Token);
            DebugLog?.Invoke(this, "Camera started.");
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke(this, $"Camera unavailable: {ex.Message}");
            // Non-fatal — vision will return "camera not available"
        }
```

Note: This requires exposing `ICameraService` through `VisionAgent` — see below.

#### StopAsync — stop camera before disconnecting

```csharp
// AFTER _voiceOut.StopAsync(), ADD:

        try { await _vision.Camera.StopAsync(); }
        catch { /* camera may not have started */ }
```

### 2. `src/BodyCam/Agents/VisionAgent.cs`

**Expose** the camera service so the orchestrator can manage its lifecycle:

```csharp
// ADD property:
public ICameraService Camera => _camera;
```

This follows the same pattern as `VoiceOutputAgent.Tracker` which exposes `AudioPlaybackTracker` for the orchestrator to read.

### 3. `src/BodyCam/Models/SessionContext.cs`

**Update** `LastVisionDescription` to be set by the orchestrator whenever a vision call completes. Already exists — verify it's wired:

```csharp
// Existing (no change needed):
public string? LastVisionDescription { get; set; }
```

The orchestrator should update this after each successful describe call:

```csharp
// In AgentOrchestrator.ExecuteDescribeSceneAsync, AFTER getting description:
if (description is not null)
    Session.LastVisionDescription = description;
```

## Full diff for AgentOrchestrator.cs

### StartAsync additions

```csharp
// After "Audio pipeline started." debug line:
        try
        {
            await _vision.Camera.StartAsync(_cts.Token);
            DebugLog?.Invoke(this, "Camera started.");
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke(this, $"Camera unavailable: {ex.Message}");
        }
```

### StopAsync additions

```csharp
// After await _voiceOut.StopAsync();
        try { await _vision.Camera.StopAsync(); }
        catch { /* best-effort */ }
```

### ExecuteDescribeSceneAsync update

```csharp
// BEFORE:
private async Task<string> ExecuteDescribeSceneAsync(string? argumentsJson = null)
{
    string? userPrompt = null;
    if (argumentsJson is not null)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
        if (doc.RootElement.TryGetProperty("query", out var q))
            userPrompt = q.GetString();
    }

    var description = await _vision.CaptureAndDescribeAsync(userPrompt);
    return System.Text.Json.JsonSerializer.Serialize(new
    {
        description = description ?? "Camera not available or no frame captured."
    });
}

// AFTER:
private async Task<string> ExecuteDescribeSceneAsync(string? argumentsJson = null)
{
    string? userPrompt = null;
    if (argumentsJson is not null)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
        if (doc.RootElement.TryGetProperty("query", out var q))
            userPrompt = q.GetString();
    }

    var description = await _vision.CaptureAndDescribeAsync(userPrompt);

    if (description is not null)
        Session.LastVisionDescription = description;

    return System.Text.Json.JsonSerializer.Serialize(new
    {
        description = description ?? "Camera not available or no frame captured."
    });
}
```

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -v q
```

Manual: Start the app → verify debug log shows "Camera started." (or "Camera unavailable:" on machines without a webcam). Ask "what do you see?" → verify the function call flow completes even if the camera returns null.
