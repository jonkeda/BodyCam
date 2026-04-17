namespace BodyCam.Models;

/// <summary>
/// Holds conversation state shared across agents.
/// Provides a sliding window over message history to stay within token budget.
/// </summary>
public class SessionContext
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public List<ChatMessage> Messages { get; } = [];
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; }

    /// <summary>Approximate max character budget for history (rough token proxy: 1 token ≈ 4 chars).</summary>
    public int MaxHistoryChars { get; set; } = 16_000; // ~4000 tokens

    /// <summary>Vision context from the most recent camera frame description.</summary>
    public string? LastVisionDescription { get; set; }

    /// <summary>The system prompt injected as the first message for Chat Completions.</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Returns messages trimmed to fit within <see cref="MaxHistoryChars"/>.
    /// Always keeps the system prompt (if any) and the most recent messages.
    /// </summary>
    public List<ChatMessage> GetTrimmedHistory()
    {
        var result = new List<ChatMessage>();

        // Always include system prompt as first message
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            result.Add(new ChatMessage { Role = "system", Content = SystemPrompt });
        }

        // Inject vision context as a system message if available
        if (!string.IsNullOrWhiteSpace(LastVisionDescription))
        {
            result.Add(new ChatMessage
            {
                Role = "system",
                Content = $"[Vision context] You can currently see: {LastVisionDescription}"
            });
        }

        // Walk backwards, accumulating until budget exhausted
        var budget = MaxHistoryChars;
        var kept = new List<ChatMessage>();

        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var msg = Messages[i];
            var cost = msg.Content.Length;
            if (budget - cost < 0 && kept.Count > 0)
                break;
            budget -= cost;
            kept.Add(msg);
        }

        kept.Reverse();
        result.AddRange(kept);
        return result;
    }

    /// <summary>Clears all messages and resets state for a new session.</summary>
    public void Reset()
    {
        Messages.Clear();
        LastVisionDescription = null;
        SessionId = Guid.NewGuid().ToString("N");
        StartedAt = DateTime.UtcNow;
        IsActive = false;
    }
}

public class ChatMessage
{
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
