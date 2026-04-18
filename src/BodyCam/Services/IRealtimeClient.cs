using BodyCam.Models;

namespace BodyCam.Services;

public interface IRealtimeClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    bool IsConnected { get; }

    Task SendAudioChunkAsync(byte[] pcm16Data, CancellationToken ct = default);
    Task CommitAudioBufferAsync(CancellationToken ct = default);
    Task CreateResponseAsync(CancellationToken ct = default);
    Task CancelResponseAsync(CancellationToken ct = default);
    Task TruncateResponseAudioAsync(string itemId, int audioEndMs, CancellationToken ct = default);
    Task UpdateSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends a text message as user input and triggers a response.
    /// </summary>
    Task SendTextInputAsync(string text, CancellationToken ct = default);

    event EventHandler<byte[]>? AudioDelta;
    event EventHandler<string>? OutputTranscriptDelta;
    event EventHandler<string>? OutputTranscriptCompleted;
    event EventHandler<string>? InputTranscriptCompleted;
    event EventHandler? SpeechStarted;
    event EventHandler? SpeechStopped;
    event EventHandler<RealtimeResponseInfo>? ResponseDone;
    event EventHandler<string>? ErrorOccurred;
    event EventHandler<string>? ConnectionLost;

    /// <summary>
    /// Fired when a new output item is added to the response, providing the item ID for truncation tracking.
    /// </summary>
    event EventHandler<string>? OutputItemAdded;

    /// <summary>
    /// Fired when the Realtime API requests a function call.
    /// </summary>
    event EventHandler<FunctionCallInfo>? FunctionCallReceived;

    /// <summary>
    /// Sends the result of a function call back to the Realtime API.
    /// </summary>
    Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken ct = default);
}
