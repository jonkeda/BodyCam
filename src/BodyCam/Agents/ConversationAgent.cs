using Microsoft.Extensions.AI;

namespace BodyCam.Agents;

/// <summary>
/// Executes deep analysis queries using a Chat Completions model (e.g. gpt-5.4).
/// Called by AgentOrchestrator when the Realtime API triggers the deep_analysis function.
/// </summary>
public class ConversationAgent
{
    private readonly IChatClient _chatClient;
    private readonly AppSettings _settings;

    public ConversationAgent(IChatClient chatClient, AppSettings settings)
    {
        _chatClient = chatClient;
        _settings = settings;
    }

    /// <summary>
    /// Performs deep analysis on a query using Chat Completions.
    /// Returns the full text result to be sent back as function_call_output.
    /// </summary>
    public async Task<string> AnalyzeAsync(
        string query,
        string? context = null,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>();

        messages.Add(new ChatMessage(ChatRole.System,
            """
            You are a deep analysis assistant. Provide thorough, detailed answers.
            Be comprehensive but structured. Use markdown formatting where helpful.
            The user is interacting via voice — your response will be spoken aloud,
            so avoid overly long or complex formatting.
            """));

        if (!string.IsNullOrWhiteSpace(context))
        {
            messages.Add(new ChatMessage(ChatRole.System,
                $"Conversation context: {context}"));
        }

        messages.Add(new ChatMessage(ChatRole.User, query));

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? string.Empty;
    }
}
