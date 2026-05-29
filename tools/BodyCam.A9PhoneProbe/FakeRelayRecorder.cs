using System.Net;
using System.Net.Sockets;
using Android.Util;

namespace BodyCam.A9PhoneProbe;

internal sealed class FakeRelayRecorder : IDisposable
{
    private const string LogTag = "A9FakeRelay";
    private const int MaxBytesPerConnection = 4096;

    private readonly CancellationTokenSource _stop = new();
    private readonly TcpListener _listener;
    private readonly Task _acceptLoop;
    private readonly object _gate = new();
    private readonly List<FakeRelayConnection> _connections = [];

    private bool _disposed;

    private FakeRelayRecorder(TcpListener listener)
    {
        _listener = listener;
        BoundEndpoint = _listener.LocalEndpoint?.ToString() ?? "<unknown>";
        _acceptLoop = Task.Run(AcceptLoop);
    }

    public string BoundEndpoint { get; }

    public static FakeRelayRecorder Start(int port)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start(8);
        Log.Info(LogTag, $"listening on {listener.LocalEndpoint}");
        return new FakeRelayRecorder(listener);
    }

    public void Stop()
    {
        if (_disposed)
            return;

        _stop.Cancel();
        _listener.Stop();

        try
        {
            _acceptLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }

    public IReadOnlyList<string> BuildReportLines()
    {
        List<FakeRelayConnection> snapshot;
        lock (_gate)
        {
            snapshot = [.. _connections];
        }

        var lines = new List<string>
        {
            $"fake relay: connections={snapshot.Count}",
        };

        for (var i = 0; i < snapshot.Count; i++)
        {
            var connection = snapshot[i];
            var bytes = connection.Bytes;
            lines.Add(
                $"fake relay[{i}]: remote={connection.RemoteEndpoint} bytes={bytes.Length} " +
                $"durationMs={(int)(connection.EndedAt - connection.StartedAt).TotalMilliseconds} " +
                $"hex={Convert.ToHexString(bytes)}");
        }

        return lines;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _stop.Dispose();
        _disposed = true;
    }

    private void AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var client = _listener.AcceptTcpClient();
                Record(client);
            }
            catch (SocketException ex) when (_stop.IsCancellationRequested)
            {
                Log.Info(LogTag, $"accept stopped: {ex.SocketErrorCode}");
                break;
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AddConnection(new FakeRelayConnection
                {
                    RemoteEndpoint = $"accept-error:{ex.GetType().Name}",
                    StartedAt = DateTimeOffset.Now,
                    EndedAt = DateTimeOffset.Now,
                    Bytes = [],
                });
            }
        }
    }

    private void Record(TcpClient client)
    {
        var started = DateTimeOffset.Now;
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "<unknown>";
        using var output = new MemoryStream();

        try
        {
            client.NoDelay = true;
            client.Client.ReceiveTimeout = 1000;

            using var stream = client.GetStream();
            var buffer = new byte[512];
            while (output.Length < MaxBytesPerConnection && !_stop.IsCancellationRequested)
            {
                var limit = Math.Min(buffer.Length, MaxBytesPerConnection - (int)output.Length);
                int read;
                try
                {
                    read = stream.Read(buffer, 0, limit);
                }
                catch (IOException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                if (read <= 0)
                    break;

                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            var bytes = output.ToArray();
            Log.Info(LogTag, $"recorded remote={remote} bytes={bytes.Length} hex={Convert.ToHexString(bytes)}");
            AddConnection(new FakeRelayConnection
            {
                RemoteEndpoint = remote,
                StartedAt = started,
                EndedAt = DateTimeOffset.Now,
                Bytes = bytes,
            });
        }
    }

    private void AddConnection(FakeRelayConnection connection)
    {
        lock (_gate)
        {
            _connections.Add(connection);
        }
    }

    private sealed class FakeRelayConnection
    {
        public required string RemoteEndpoint { get; init; }

        public required DateTimeOffset StartedAt { get; init; }

        public required DateTimeOffset EndedAt { get; init; }

        public required byte[] Bytes { get; init; }
    }
}
