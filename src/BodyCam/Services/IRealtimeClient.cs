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

    event EventHandler<byte[]>? AudioDelta;
    event EventHandler<string>? OutputTranscriptDelta;
    event EventHandler<string>? OutputTranscriptCompleted;
    event EventHandler<string>? InputTranscriptCompleted;
    event EventHandler? SpeechStarted;
    event EventHandler? SpeechStopped;
    event EventHandler<RealtimeResponseInfo>? ResponseDone;
    event EventHandler<string>? ErrorOccurred;
}
