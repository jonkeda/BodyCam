using Microsoft.Extensions.AI;
using BodyCam.Models;
using ChatRole = Microsoft.Extensions.AI.ChatRole;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BodyCam.Services;

public class ChatCompletionsClient : IChatCompletionsClient
{
    private readonly IChatClient _chatClient;

    public ChatCompletionsClient(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> CompleteAsync(
        IList<Models.ChatMessage> messages,
        CancellationToken ct = default)
    {
        var aiMessages = MapMessages(messages);
        var response = await _chatClient.GetResponseAsync(aiMessages, cancellationToken: ct);
        return response.Text ?? string.Empty;
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        IList<Models.ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var aiMessages = MapMessages(messages);
        await foreach (var update in _chatClient.GetStreamingResponseAsync(aiMessages, cancellationToken: ct))
        {
            if (update.Text is { Length: > 0 } text)
                yield return text;
        }
    }

    private static List<AiChatMessage> MapMessages(IList<Models.ChatMessage> messages)
    {
        return messages.Select(m => new AiChatMessage(
            m.Role switch
            {
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                _ => ChatRole.User
            },
            m.Content
        )).ToList();
    }
}
