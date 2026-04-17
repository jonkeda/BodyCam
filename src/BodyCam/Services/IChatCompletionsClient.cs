namespace BodyCam.Services;

/// <summary>
/// Abstraction over Chat Completions API for testability.
/// Wraps Microsoft.Extensions.AI's IChatClient.
/// </summary>
public interface IChatCompletionsClient
{
    /// <summary>
    /// Sends messages to Chat Completions and returns the full reply.
    /// </summary>
    Task<string> CompleteAsync(
        IList<Models.ChatMessage> messages,
        CancellationToken ct = default);

    /// <summary>
    /// Sends messages to Chat Completions and streams reply tokens.
    /// </summary>
    IAsyncEnumerable<string> CompleteStreamingAsync(
        IList<Models.ChatMessage> messages,
        CancellationToken ct = default);
}
