using BodyCam.Services.Actions;

namespace BodyCam.Services.Transcript;

public enum TranscriptRetention
{
    Ephemeral,
    Session,
    Persistent,
}

public sealed record TranscriptMediaReference(
    string Kind,
    string? Caption,
    string? ContentType = null,
    string? Uri = null,
    long? ByteLength = null);

public sealed record TranscriptRecord(
    string SessionId,
    DateTimeOffset Timestamp,
    string Role,
    string Text,
    IReadOnlyList<TranscriptMediaReference> MediaReferences,
    string? ActionId = null,
    ActionTriggerOrigin? TriggerOrigin = null,
    string? SourceProfileId = null,
    string? ProviderId = null,
    string? ModelId = null,
    TranscriptRetention Retention = TranscriptRetention.Session);

public interface ITranscriptStore
{
    Task AppendAsync(TranscriptRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<TranscriptRecord>> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListSessionsAsync(CancellationToken ct = default);
    Task ClearSessionAsync(string sessionId, CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);
}
