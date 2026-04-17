# Step 2: Vision Agent Enhancements

Extend `VisionAgent` with frame caching, custom prompts, and cost control. Currently it does basic describe-frame; this step makes it production-ready.

## Files Modified

### 1. `src/BodyCam/Agents/VisionAgent.cs`

**Add** frame caching to avoid re-describing identical scenes. **Add** custom prompt support so the orchestrator can pass the user's question. **Add** `detail` parameter control.

```csharp
// BEFORE (full file):
public class VisionAgent
{
    private readonly ICameraService _camera;
    private readonly IChatClient _chatClient;
    private readonly AppSettings _settings;

    public VisionAgent(ICameraService camera, IChatClient chatClient, AppSettings settings)
    {
        _camera = camera;
        _chatClient = chatClient;
        _settings = settings;
    }

    public async Task<string> DescribeFrameAsync(byte[] jpegFrame, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                "Describe what you see concisely in 1-3 sentences. Focus on notable objects, people, text, and spatial layout."),
            new(ChatRole.User, [
                new DataContent(jpegFrame, "image/jpeg"),
                new TextContent("What do you see?")
            ])
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? "Unable to describe the scene.";
    }

    public async Task<string?> CaptureAndDescribeAsync(CancellationToken ct = default)
    {
        var frame = await _camera.CaptureFrameAsync(ct);
        if (frame is null) return null;
        return await DescribeFrameAsync(frame, ct);
    }
}

// AFTER (full file):
public class VisionAgent
{
    private readonly ICameraService _camera;
    private readonly IChatClient _chatClient;
    private readonly AppSettings _settings;
    private string? _lastDescription;
    private DateTimeOffset _lastCaptureTime = DateTimeOffset.MinValue;

    /// <summary>Minimum interval between vision API calls.</summary>
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromSeconds(5);

    public VisionAgent(ICameraService camera, IChatClient chatClient, AppSettings settings)
    {
        _camera = camera;
        _chatClient = chatClient;
        _settings = settings;
    }

    /// <summary>Most recent vision description (cached).</summary>
    public string? LastDescription => _lastDescription;

    public async Task<string> DescribeFrameAsync(
        byte[] jpegFrame, string? userPrompt = null, CancellationToken ct = default)
    {
        var systemText = "Describe what you see concisely in 1-3 sentences. Focus on notable objects, people, text, and spatial layout.";
        var userText = userPrompt ?? "What do you see?";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemText),
            new(ChatRole.User, [
                new DataContent(jpegFrame, "image/jpeg"),
                new TextContent(userText)
            ])
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var description = response.Text ?? "Unable to describe the scene.";

        _lastDescription = description;
        _lastCaptureTime = DateTimeOffset.UtcNow;

        return description;
    }

    public async Task<string?> CaptureAndDescribeAsync(
        string? userPrompt = null, CancellationToken ct = default)
    {
        // Rate-limit: return cached description if within cooldown
        if (_lastDescription is not null
            && DateTimeOffset.UtcNow - _lastCaptureTime < CooldownPeriod)
        {
            return _lastDescription;
        }

        var frame = await _camera.CaptureFrameAsync(ct);
        if (frame is null) return _lastDescription; // Return stale if camera unavailable
        return await DescribeFrameAsync(frame, userPrompt, ct);
    }
}
```

**Changes:**
- `DescribeFrameAsync` accepts optional `userPrompt` (defaults to "What do you see?")
- `CaptureAndDescribeAsync` accepts optional `userPrompt` (forwarded to describe)
- Cooldown rate-limiting (5s) — returns cached description if called too frequently
- `LastDescription` property for orchestrator/UI to read without triggering a new API call
- Falls back to stale description if camera returns null

### 2. `src/BodyCam/Orchestration/AgentOrchestrator.cs`

**Update** `ExecuteDescribeSceneAsync` to extract the optional `query` argument from the function call and pass it as the user prompt:

```csharp
// BEFORE:
private async Task<string> ExecuteDescribeSceneAsync()
{
    var description = await _vision.CaptureAndDescribeAsync();
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
    return System.Text.Json.JsonSerializer.Serialize(new
    {
        description = description ?? "Camera not available or no frame captured."
    });
}
```

**Update** the switch in `OnFunctionCallReceived` to pass arguments:

```csharp
// BEFORE:
"describe_scene" => await ExecuteDescribeSceneAsync(),

// AFTER:
"describe_scene" => await ExecuteDescribeSceneAsync(info.Arguments),
```

### 3. `src/BodyCam/Services/RealtimeClient.cs`

**Update** the `describe_scene` tool definition to include a `query` parameter:

```json
// BEFORE:
{
  "type": "function",
  "name": "describe_scene",
  "description": "Capture what the camera currently sees...",
  "parameters": {
    "type": "object",
    "properties": {},
    "required": []
  }
}

// AFTER:
{
  "type": "function",
  "name": "describe_scene",
  "description": "Capture what the camera currently sees and describe it. Use when the user asks about their surroundings, asks you to look at something, or when visual context would improve your response.",
  "parameters": {
    "type": "object",
    "properties": {
      "query": {
        "type": "string",
        "description": "Optional question about the scene, e.g. 'What text is on the sign?' or 'How many people are visible?'"
      }
    },
    "required": []
  }
}
```

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -v q
```

Existing `VisionAgentTests` should still pass (the `userPrompt` parameter is optional). New tests for caching/cooldown are added in Step 6.
