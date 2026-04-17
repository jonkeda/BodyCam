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
