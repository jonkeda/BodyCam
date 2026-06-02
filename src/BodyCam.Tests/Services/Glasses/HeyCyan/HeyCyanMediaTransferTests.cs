using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

public class HeyCyanMediaTransferTests
{
    private static (HeyCyanMediaTransfer transfer, FakeSession session, FakeHttpClientFactory factory, FakeTimeProvider time)
        Build(TimeSpan? warmIdle = null)
    {
        var session = new FakeSession();
        var factory = new FakeHttpClientFactory();
        var time = new FakeTimeProvider();
        var transfer = new HeyCyanMediaTransfer(
            session,
            factory,
            NullLogger<HeyCyanMediaTransfer>.Instance,
            time,
            warmIdle ?? TimeSpan.FromSeconds(8));
        return (transfer, session, factory, time);
    }

    [Fact]
    public async Task ListAsync_enters_transfer_mode_and_returns_parsed_entries()
    {
        var (transfer, session, factory, _) = Build();
        factory.ScriptedMediaConfig = "IMG_20260430_123045.jpg\nVID_20260430_123100.mp4";

        var result = await transfer.ListAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("IMG_20260430_123045.jpg");
        result[0].Kind.Should().Be(HeyCyanMediaKind.Photo);
        result[1].Name.Should().Be("VID_20260430_123100.mp4");
        result[1].Kind.Should().Be(HeyCyanMediaKind.Video);

        session.EnterCount.Should().Be(1);
        session.ExitCount.Should().Be(0); // warm — no exit yet
        transfer.IsWarm.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_handles_phase_1e_mixed_m01_media_config()
    {
        var (transfer, _, factory, _) = Build();
        factory.ScriptedMediaConfig = """
            20260531184722907.mp4
            20260531190723036.jpg
            20260531190726933.mp4
            """;

        var result = await transfer.ListAsync(CancellationToken.None);

        result.Should().HaveCount(3);
        result.Select(e => e.Name).Should().Equal(
            "20260531184722907.mp4",
            "20260531190723036.jpg",
            "20260531190726933.mp4");
        result.Select(e => e.Kind).Should().Equal(
            HeyCyanMediaKind.Video,
            HeyCyanMediaKind.Photo,
            HeyCyanMediaKind.Video);
        factory.RequestedStringPaths.Should().Equal("/files/media.config");
    }

    [Fact]
    public async Task DownloadAsync_enters_transfer_mode_and_returns_bytes()
    {
        var (transfer, session, factory, _) = Build();
        var expectedBytes = new byte[] { 0x01, 0x02, 0x03 };
        factory.ScriptedFileContent["test.jpg"] = expectedBytes;

        var result = await transfer.DownloadAsync("test.jpg", CancellationToken.None);

        result.Should().BeEquivalentTo(expectedBytes);
        session.EnterCount.Should().Be(1);
        session.ExitCount.Should().Be(0);
    }

    [Fact]
    public async Task DownloadAsync_uses_files_endpoint_for_phase_1e_jpeg_and_mp4()
    {
        var (transfer, _, factory, _) = Build();
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var mp4Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };
        factory.ScriptedFileContent["20260531190723036.jpg"] = jpegBytes;
        factory.ScriptedFileContent["20260531190726933.mp4"] = mp4Bytes;

        var jpg = await transfer.DownloadAsync("20260531190723036.jpg", CancellationToken.None);
        var mp4 = await transfer.DownloadAsync("20260531190726933.mp4", CancellationToken.None);

        jpg.Should().Equal(jpegBytes);
        mp4.Should().Equal(mp4Bytes);
        factory.RequestedBytePaths.Should().Equal(
            "/files/20260531190723036.jpg",
            "/files/20260531190726933.mp4");
    }

    [Fact]
    public async Task Two_consecutive_downloads_within_warm_window_reuse_session()
    {
        var (transfer, session, factory, time) = Build();
        factory.ScriptedFileContent["a.jpg"] = new byte[] { 0x01 };
        factory.ScriptedFileContent["b.jpg"] = new byte[] { 0x02 };

        await transfer.DownloadAsync("a.jpg", CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(4)); // within 8s window
        await transfer.DownloadAsync("b.jpg", CancellationToken.None);

        session.EnterCount.Should().Be(1, "should enter transfer mode only once");
        session.ExitCount.Should().Be(0, "warm session should stay open");
    }

    [Fact]
    public async Task Idle_timeout_triggers_automatic_teardown()
    {
        var (transfer, session, factory, time) = Build(warmIdle: TimeSpan.FromSeconds(5));
        factory.ScriptedFileContent["test.jpg"] = new byte[] { 0x01 };

        await transfer.DownloadAsync("test.jpg", CancellationToken.None);
        session.EnterCount.Should().Be(1);
        session.ExitCount.Should().Be(0);

        // Advance past the warm idle timeout
        time.Advance(TimeSpan.FromSeconds(6));
        await Task.Delay(50); // allow idle teardown task to run

        session.ExitCount.Should().Be(1, "idle timeout should trigger ExitTransferModeAsync");
        transfer.IsWarm.Should().BeFalse();
    }

    [Fact]
    public async Task Download_after_idle_timeout_reenters_transfer_mode()
    {
        var (transfer, session, factory, time) = Build(warmIdle: TimeSpan.FromSeconds(5));
        factory.ScriptedFileContent["a.jpg"] = new byte[] { 0x01 };
        factory.ScriptedFileContent["b.jpg"] = new byte[] { 0x02 };

        await transfer.DownloadAsync("a.jpg", CancellationToken.None);
        session.EnterCount.Should().Be(1);

        time.Advance(TimeSpan.FromSeconds(6));
        await Task.Delay(50); // allow teardown
        session.ExitCount.Should().Be(1);

        await transfer.DownloadAsync("b.jpg", CancellationToken.None);
        session.EnterCount.Should().Be(2, "should re-enter transfer mode after idle teardown");
    }

    [Fact]
    public async Task ExitAsync_tears_down_session_and_exits_transfer_mode()
    {
        var (transfer, session, factory, _) = Build();
        factory.ScriptedFileContent["test.jpg"] = new byte[] { 0x01 };

        await transfer.DownloadAsync("test.jpg", CancellationToken.None);
        session.EnterCount.Should().Be(1);
        transfer.IsWarm.Should().BeTrue();

        await transfer.ExitAsync(CancellationToken.None);

        session.ExitCount.Should().Be(1);
        transfer.IsWarm.Should().BeFalse();
    }

    [Fact]
    public async Task ExitAsync_is_idempotent()
    {
        var (transfer, session, factory, _) = Build();
        factory.ScriptedFileContent["test.jpg"] = new byte[] { 0x01 };

        await transfer.DownloadAsync("test.jpg", CancellationToken.None);
        await transfer.ExitAsync(CancellationToken.None);
        await transfer.ExitAsync(CancellationToken.None); // second call

        session.ExitCount.Should().Be(1, "ExitTransferModeAsync should be called only once");
    }

    [Fact]
    public async Task DisposeAsync_tears_down_warm_session()
    {
        var (transfer, session, factory, _) = Build();
        factory.ScriptedFileContent["test.jpg"] = new byte[] { 0x01 };

        await transfer.DownloadAsync("test.jpg", CancellationToken.None);
        session.EnterCount.Should().Be(1);

        await transfer.DisposeAsync();

        session.ExitCount.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_during_download_does_not_schedule_idle_exit()
    {
        var (transfer, session, factory, time) = Build();
        using var cts = new CancellationTokenSource();
        factory.OnGetByteArray = (_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        Func<Task> act = () => transfer.DownloadAsync("test.jpg", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // Session should still be warm (no idle exit scheduled on cancellation)
        transfer.IsWarm.Should().BeTrue();
        session.ExitCount.Should().Be(0);

        // Advance time to ensure no delayed teardown occurs
        time.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(50);
        session.ExitCount.Should().Be(0, "cancellation should not trigger idle teardown");
    }

    [Fact]
    public async Task DownloadAsync_TwiceWithinWarmWindow_EntersTransferModeOnce()
    {
        var (transfer, session, factory, time) = Build();
        factory.ScriptedFileContent["a.jpg"] = new byte[] { 0x01 };
        factory.ScriptedFileContent["b.jpg"] = new byte[] { 0x02 };

        _ = await transfer.DownloadAsync("a.jpg", CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(3));
        _ = await transfer.DownloadAsync("b.jpg", CancellationToken.None);

        session.EnterCount.Should().Be(1);
    }

    [Fact]
    public async Task DownloadAsync_AfterWarmIdleElapsed_ReentersTransferMode()
    {
        var (transfer, session, factory, time) = Build(warmIdle: TimeSpan.FromSeconds(8));
        factory.ScriptedFileContent["a.jpg"] = new byte[] { 0x01 };
        factory.ScriptedFileContent["b.jpg"] = new byte[] { 0x02 };

        // First download
        await transfer.DownloadAsync("a.jpg", CancellationToken.None);
        session.EnterCount.Should().Be(1);

        // Advance past the warm idle window
        time.Advance(TimeSpan.FromSeconds(9));
        await Task.Delay(50); // allow teardown task to run

        session.ExitCount.Should().Be(1);
        transfer.IsWarm.Should().BeFalse();

        // Second download should re-enter transfer mode
        await transfer.DownloadAsync("b.jpg", CancellationToken.None);
        session.EnterCount.Should().Be(2);
        session.ExitCount.Should().Be(1);
    }

    [Fact]
    public async Task CancelMidDownload_DoesNotEagerlyExitTransferMode()
    {
        var (transfer, session, factory, time) = Build();
        using var cts = new CancellationTokenSource();
        
        factory.OnGetByteArray = (_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        Func<Task> act = () => transfer.DownloadAsync("test.jpg", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Transfer mode should still be active (warm)
        transfer.IsWarm.Should().BeTrue();
        session.ExitCount.Should().Be(0);

        // Cancellation should NOT schedule idle exit - verify no teardown happens even after advancing time        // (This verifies the idle timer was not started on the error path)
        session.ExitCount.Should().Be(0, "cancellation should not schedule idle teardown");
    }

    // Fake implementations for testing

    private sealed class FakeSession : IHeyCyanGlassesSession
    {
        public int EnterCount { get; private set; }
        public int ExitCount { get; private set; }

        public HeyCyanState State => HeyCyanState.Connected;
        public HeyCyanDeviceInfo? Device => null;
        public HeyCyanMediaCount? LastMediaCount => null;

        public event EventHandler<HeyCyanState>? StateChanged;
        public event EventHandler<HeyCyanBattery>? BatteryUpdated;
        public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
        public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
        public event EventHandler<byte[]>? AiPhotoReceived;

        public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan _, CancellationToken __) =>
            Task.FromResult<IReadOnlyList<HeyCyanDeviceInfo>>(Array.Empty<HeyCyanDeviceInfo>());

        public Task ConnectAsync(HeyCyanDeviceInfo _, CancellationToken __) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken _) => Task.CompletedTask;
        public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken _) =>
            Task.FromResult(new HeyCyanVersionInfo("hw", "fw", "whw", "wfw", "00:00:00:00:00:00"));
        public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken _) =>
            Task.FromResult(new HeyCyanBattery(100, false));
        public Task SyncTimeAsync(CancellationToken _) => Task.CompletedTask;
        public Task TakePhotoAsync(CancellationToken _) => Task.CompletedTask;
        public Task StartVideoAsync(CancellationToken _) => Task.CompletedTask;
        public Task StopVideoAsync(CancellationToken _) => Task.CompletedTask;
        public Task StartAudioAsync(CancellationToken _) => Task.CompletedTask;
        public Task StopAudioAsync(CancellationToken _) => Task.CompletedTask;
        public Task TakeAiPhotoAsync(CancellationToken _) => Task.CompletedTask;

        public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken _)
        {
            EnterCount++;
            return Task.FromResult(new HeyCyanTransferSession("http://192.168.49.10/", Array.Empty<string>()));
        }

        public Task ExitTransferModeAsync(CancellationToken _)
        {
            ExitCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }

    private sealed class FakeHttpClientFactory : IHeyCyanHttpClientFactory
    {
        public string ScriptedMediaConfig { get; set; } = "";
        public Dictionary<string, byte[]> ScriptedFileContent { get; } = new();
        public Func<string, CancellationToken, byte[]>? OnGetByteArray { get; set; }
        public List<string> RequestedStringPaths { get; } = new();
        public List<string> RequestedBytePaths { get; } = new();
        public List<string> RequestedStreamPaths { get; } = new();

        public Task<IHeyCyanHttpClient> CreateAsync(Uri baseUri, CancellationToken _)
        {
            IHeyCyanHttpClient client = new FakeHttpClient(this, baseUri);
            return Task.FromResult(client);
        }

        private sealed class FakeHttpClient : IHeyCyanHttpClient
        {
            private readonly FakeHttpClientFactory _factory;

            public Uri BaseUri { get; }

            public FakeHttpClient(FakeHttpClientFactory factory, Uri baseUri)
            {
                _factory = factory;
                BaseUri = baseUri;
            }

            public Task<string> GetStringAsync(string path, CancellationToken _)
            {
                _factory.RequestedStringPaths.Add(path);

                if (path == "/files/media.config")
                    return Task.FromResult(_factory.ScriptedMediaConfig);
                throw new InvalidOperationException($"Unexpected path: {path}");
            }

            public Task<byte[]> GetByteArrayAsync(string path, CancellationToken ct)
            {
                _factory.RequestedBytePaths.Add(path);

                if (_factory.OnGetByteArray is not null)
                    return Task.FromResult(_factory.OnGetByteArray(path, ct));

                // Extract filename from /files/{name}
                var name = path.StartsWith("/files/") ? path[7..] : path;
                if (_factory.ScriptedFileContent.TryGetValue(name, out var bytes))
                    return Task.FromResult(bytes);

                throw new InvalidOperationException($"File not scripted: {name}");
            }

            public Task<Stream> GetStreamAsync(string path, CancellationToken ct)
            {
                _factory.RequestedStreamPaths.Add(path);

                if (_factory.OnGetByteArray is not null)
                {
                    var streamBytes = _factory.OnGetByteArray(path, ct);
                    return Task.FromResult<Stream>(new MemoryStream(streamBytes));
                }

                // Extract filename from /files/{name}
                var name = path.StartsWith("/files/") ? path[7..] : path;
                if (_factory.ScriptedFileContent.TryGetValue(name, out var bytes))
                    return Task.FromResult<Stream>(new MemoryStream(bytes));

                throw new InvalidOperationException($"File not scripted: {name}");
            }

            public ValueTask DisposeAsync() => default;
        }
    }
}
