# Step 5: Implement VisionAgent

Replace the stub with a real implementation that captures a camera frame and describes it using a vision model.

## Depends On: Step 2 (function calling infrastructure)

## Files Modified

### 1. `src/BodyCam/Agents/VisionAgent.cs`

**Rewrite** with real vision model integration:

```csharp
using Microsoft.Extensions.AI;
using BodyCam.Services;

namespace BodyCam.Agents;

/// <summary>
/// Captures camera frames and describes them using a vision-capable model.
/// Called by AgentOrchestrator when the Realtime API triggers the describe_scene function.
/// </summary>
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

    /// <summary>
    /// Describes a JPEG frame using the vision model.
    /// </summary>
    public async Task<string> DescribeFrameAsync(byte[] jpegFrame, CancellationToken ct = default)
    {
        var base64 = Convert.ToBase64String(jpegFrame);
        var dataUri = $"data:image/jpeg;base64,{base64}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                "Describe what you see concisely in 1-3 sentences. Focus on notable objects, people, text, and spatial layout."),
            new(ChatRole.User, [
                new ImageContent(new Uri(dataUri)),
                new TextContent("What do you see?")
            ])
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? "Unable to describe the scene.";
    }

    /// <summary>
    /// Captures a frame from the camera and describes it.
    /// Returns null if no frame is available.
    /// </summary>
    public async Task<string?> CaptureAndDescribeAsync(CancellationToken ct = default)
    {
        var frame = await _camera.CaptureFrameAsync(ct);
        if (frame is null) return null;
        return await DescribeFrameAsync(frame, ct);
    }
}
```

**Key changes:**
- Constructor now takes `IChatClient` for vision model calls
- `DescribeFrameAsync` sends the image as a data URI via `ImageContent`
- Uses `Microsoft.Extensions.AI` types directly

### 2. `src/BodyCam/MauiProgram.cs`

**Add** a separate `IChatClient` registration for vision, or reuse the same one.

**Option A (recommended — separate named client):** Since the vision model may differ from the chat model, register a second `IChatClient` specifically for vision. But DI doesn't support named registrations easily without a factory.

**Option B (simpler — use same client):** VisionAgent uses the same `IChatClient` (gpt-5.4 or gpt-5.4-mini). The vision model and chat model are often the same model. Just pass the same `IChatClient` instance.

**Recommendation:** Use Option B for now. Both ConversationAgent and VisionAgent share the `IChatClient` singleton. The model used is the `ChatModel` setting. If we need a separate vision model later, we can introduce a factory or keyed services.

The `VisionAgent` registration doesn't change:
```csharp
builder.Services.AddSingleton<VisionAgent>();
```

DI will resolve `IChatClient` from the existing singleton registration.

### Design Notes

- **Image format:** `ImageContent(new Uri(dataUri))` with a data URI (`data:image/jpeg;base64,...`) is the standard way to send images via `Microsoft.Extensions.AI`. The underlying OpenAI client will format it correctly for the API.
- **Prompt:** Keep it short — "Describe what you see concisely in 1-3 sentences." This gives the function caller (Realtime model) a brief description it can work with. The Realtime model will then speak about it in its own words.
- **Model selection:** The vision model setting (`_settings.VisionModel`) is not directly used here because `IChatClient` is pre-configured with a specific model at DI registration time. If we need per-request model selection, we'd need to restructure the DI (out of scope for M3).
- **Token cost:** One JPEG frame at 512x512 is ~85 tokens. At 1024x1024 it's ~170 tokens. Manageable for on-demand calls.

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore -v q
```
