namespace BodyCam.Services.Transcript;

public sealed class InMemoryTranscriptStore : ITranscriptStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<TranscriptRecord>> _sessions = new(StringComparer.Ordinal);

    public Task AppendAsync(TranscriptRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_sessions.TryGetValue(record.SessionId, out var records))
            {
                records = [];
                _sessions[record.SessionId] = records;
            }

            records.Add(record);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TranscriptRecord>> GetSessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var records = _sessions.TryGetValue(sessionId, out var found)
                ? found.ToArray()
                : [];
            return Task.FromResult<IReadOnlyList<TranscriptRecord>>(records);
        }
    }

    public Task<IReadOnlyList<string>> ListSessionsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
            return Task.FromResult<IReadOnlyList<string>>(_sessions.Keys.ToArray());
    }

    public Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
            _sessions.Remove(sessionId);

        return Task.CompletedTask;
    }

    public Task ClearAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
            _sessions.Clear();

        return Task.CompletedTask;
    }
}
