using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001

namespace BodyCam.Tests.TestInfrastructure;

/// <summary>
/// A test double for <see cref="IRealtimeClient"/> that returns a stub session
/// without making real WebSocket connections.
/// </summary>
internal sealed class StubRealtimeClient : IRealtimeClient
{
    public Task<IRealtimeClientSession> CreateSessionAsync(
        RealtimeSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IRealtimeClientSession>(new StubSession(options));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    /// <summary>
    /// A minimal session that accepts sends and blocks on streaming
    /// (simulating an idle connection for unit tests).
    /// </summary>
    private sealed class StubSession : IRealtimeClientSession, IAsyncDisposable
    {
        public RealtimeSessionOptions? Options { get; }

        public StubSession(RealtimeSessionOptions? options) => Options = options;

        public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Block until cancellation — simulates an idle connection
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
