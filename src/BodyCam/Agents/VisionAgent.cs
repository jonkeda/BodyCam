using Microsoft.Extensions.AI;

namespace BodyCam.Agents;

/// <summary>
/// Describes camera frames using a vision-capable model.
/// Called by AgentOrchestrator when the Realtime API triggers the describe_scene function.
/// </summary>
public class VisionAgent
{
    private readonly IChatClient _chatClient;
    private readonly AppSettings _settings;
    private string? _lastDescription;
    private DateTimeOffset _lastCaptureTime = DateTimeOffset.MinValue;

    /// <summary>Minimum interval between vision API calls.</summary>
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromSeconds(5);

    public VisionAgent(IChatClient chatClient, AppSettings settings)
    {
        _chatClient = chatClient;
        _settings = settings;
    }

    /// <summary>Most recent vision description (cached).</summary>
    public string? LastDescription => _lastDescription;

    /// <summary>Last capture time for cooldown checks.</summary>
    public DateTimeOffset LastCaptureTime => _lastCaptureTime;

    /// <summary>
    /// Describes a JPEG frame using the vision model.
    /// </summary>
    public async Task<string> DescribeFrameAsync(byte[] jpegFrame, string? userPrompt = null, CancellationToken ct = default)
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
}
