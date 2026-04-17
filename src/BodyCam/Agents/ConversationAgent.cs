using BodyCam.Models;
using BodyCam.Services;

namespace BodyCam.Agents;

/// <summary>
/// Mode A: Records transcripts (passthrough — Realtime API handles reasoning).
/// Mode B: Calls Chat Completions API for custom reasoning with streaming.
/// </summary>
public class ConversationAgent
{
    private readonly IChatCompletionsClient _chatClient;
    private readonly AppSettings _settings;

    public ConversationAgent(IChatCompletionsClient chatClient, AppSettings settings)
    {
        _chatClient = chatClient;
        _settings = settings;
    }

    // --- Mode A (passthrough) methods — unchanged from M1 ---

    public void AddUserMessage(string transcript, SessionContext session)
    {
        session.Messages.Add(new ChatMessage { Role = "user", Content = transcript });
    }

    public void AddAssistantMessage(string transcript, SessionContext session)
    {
        session.Messages.Add(new ChatMessage { Role = "assistant", Content = transcript });
    }

    // --- Mode B (separated pipeline) methods — NEW ---

    /// <summary>
    /// Process a user transcript through Chat Completions and stream reply tokens.
    /// Adds both the user message and assistant reply to session history.
    /// </summary>
    public async IAsyncEnumerable<string> ProcessTranscriptAsync(
        string transcript,
        SessionContext session,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Add user message to history
        AddUserMessage(transcript, session);

        // Ensure system prompt is set
        if (string.IsNullOrWhiteSpace(session.SystemPrompt))
            session.SystemPrompt = _settings.SystemInstructions;

        // Get trimmed history with system prompt + vision context
        var messages = session.GetTrimmedHistory();

        // Stream reply from Chat Completions
        var replyBuilder = new System.Text.StringBuilder();

        await foreach (var token in _chatClient.CompleteStreamingAsync(messages, ct))
        {
            replyBuilder.Append(token);
            yield return token;
        }

        // Add complete reply to session history
        var fullReply = replyBuilder.ToString();
        if (fullReply.Length > 0)
        {
            AddAssistantMessage(fullReply, session);
        }
    }

    /// <summary>
    /// Non-streaming variant for simple cases.
    /// </summary>
    public async Task<string> ProcessTranscriptFullAsync(
        string transcript,
        SessionContext session,
        CancellationToken ct = default)
    {
        AddUserMessage(transcript, session);

        if (string.IsNullOrWhiteSpace(session.SystemPrompt))
            session.SystemPrompt = _settings.SystemInstructions;

        var messages = session.GetTrimmedHistory();
        var reply = await _chatClient.CompleteAsync(messages, ct);

        if (reply.Length > 0)
            AddAssistantMessage(reply, session);

        return reply;
    }
}
