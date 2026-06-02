using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace BodyCam.Services.AiProviders;

public sealed class UnsupportedRealtimeClient : IRealtimeClient
{
    private readonly string _message;

    public UnsupportedRealtimeClient(string message) => _message = message;

    public Task<IRealtimeClientSession> CreateSessionAsync(
        RealtimeSessionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(_message);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}

public sealed class UnsupportedRealtimeClientSession : IRealtimeClientSession
{
    private readonly string _message;

    public UnsupportedRealtimeClientSession(RealtimeSessionOptions options, string message)
    {
        Options = options;
        _message = message;
    }

    public RealtimeSessionOptions Options { get; }

    public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(_message);

    public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        throw new NotSupportedException(_message);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
