namespace BodyCam.Models;

/// <summary>
/// Holds conversation state shared across agents.
/// </summary>
public class SessionContext
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public List<ChatMessage> Messages { get; } = [];
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; }
}

public class ChatMessage
{
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
