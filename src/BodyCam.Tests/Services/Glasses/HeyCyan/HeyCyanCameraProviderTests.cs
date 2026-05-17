using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

public class HeyCyanCameraProviderTests
{
    private static (HeyCyanCameraProvider provider, FakeSession session, FakeTransfer transfer)
        Build()
    {
        var session = new FakeSession();
        var transfer = new FakeTransfer();
        var provider = new HeyCyanCameraProvider(
            session,
            transfer,
            NullLogger<HeyCyanCameraProvider>.Instance);
        return (provider, session, transfer);
    }

    [Fact]
    public void ProviderId_returns_heycyan_glasses()
    {
        var (provider, _, _) = Build();
        provider.ProviderId.Should().Be("heycyan-glasses");
    }

    [Fact]
    public void DisplayName_returns_HeyCyan_Glasses_Camera()
    {
        var (provider, _, _) = Build();
        provider.DisplayName.Should().Be("HeyCyan Glasses Camera");
    }

    [Fact]
    public void IsAvailable_when_connected_returns_true()
    {
        var (provider, session, _) = Build();
        session.SetState(HeyCyanState.Connected);
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_when_transfer_mode_returns_true()
    {
        var (provider, session, _) = Build();
        session.SetState(HeyCyanState.TransferMode);
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_when_disconnected_returns_false()
    {
        var (provider, session, _) = Build();
        session.SetState(HeyCyanState.Disconnected);
        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureFrameAsync_returns_jpeg_from_transfer_helper()
    {
        var (provider, session, transfer) = Build();
        session.SetState(HeyCyanState.Connected);

        // Setup: media count will increment from 0 to 1
        session.SetMediaCount(new HeyCyanMediaCount(0, 0, 0));
        
        // Transfer will list these entries
        transfer.Entries.Add(new HeyCyanMediaEntry(
            "IMG_20260430_120000.jpg",
            12345,
            DateTimeOffset.UtcNow,
            HeyCyanMediaKind.Photo));

        // Mock JPEG bytes (valid SOI marker)
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0x01, 0x02, 0x03 };
        transfer.Files["IMG_20260430_120000.jpg"] = jpegBytes;

        // Simulate photo capture: TakePhotoAsync will trigger MediaCountUpdated
        session.OnPhotoTrigger = () =>
            session.SetMediaCount(new HeyCyanMediaCount(1, 0, 0));

        var result = await provider.CaptureFrameAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(jpegBytes);
        session.TakePhotoCallCount.Should().Be(1);
        transfer.ListCallCount.Should().BeGreaterThanOrEqualTo(1);
        transfer.DownloadCallCount.Should().Be(1);
    }

    [Fact]
    public async Task CaptureFrameAsync_when_media_count_notify_times_out_falls_back_to_newest_entry()
    {
        var (provider, session, transfer) = Build();
        session.SetState(HeyCyanState.Connected);
        session.SetMediaCount(new HeyCyanMediaCount(0, 0, 0));

        // Add two photos with different timestamps
        var older = new HeyCyanMediaEntry(
            "IMG_20260430_100000.jpg",
            100,
            DateTimeOffset.UtcNow.AddHours(-2),
            HeyCyanMediaKind.Photo);
        var newer = new HeyCyanMediaEntry(
            "IMG_20260430_120000.jpg",
            200,
            DateTimeOffset.UtcNow,
            HeyCyanMediaKind.Photo);
        transfer.Entries.Add(older);
        transfer.Entries.Add(newer);

        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xAA, 0xBB };
        transfer.Files["IMG_20260430_120000.jpg"] = jpegBytes;

        // DON'T trigger MediaCountUpdated — let it time out
        session.OnPhotoTrigger = null;

        var result = await provider.CaptureFrameAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(jpegBytes);
        transfer.DownloadedFile.Should().Be("IMG_20260430_120000.jpg", "should pick newest entry");
    }

    [Fact]
    public async Task CaptureFrameAsync_when_session_disconnected_returns_null()
    {
        var (provider, session, transfer) = Build();
        session.SetState(HeyCyanState.Disconnected);

        var result = await provider.CaptureFrameAsync(CancellationToken.None);

        result.Should().BeNull();
        session.TakePhotoCallCount.Should().Be(0);
    }

    [Fact]
    public async Task CaptureFrameAsync_non_jpeg_bytes_throws_invalid_data()
    {
        var (provider, session, transfer) = Build();
        session.SetState(HeyCyanState.Connected);
        session.SetMediaCount(new HeyCyanMediaCount(0, 0, 0));

        transfer.Entries.Add(new HeyCyanMediaEntry(
            "bad.jpg",
            100,
            DateTimeOffset.UtcNow,
            HeyCyanMediaKind.Photo));

        // Invalid JPEG (no FF D8 magic)
        transfer.Files["bad.jpg"] = new byte[] { 0x00, 0x00, 0x01 };

        session.OnPhotoTrigger = () => session.SetMediaCount(new HeyCyanMediaCount(1, 0, 0));

        var result = await provider.CaptureFrameAsync(CancellationToken.None);

        result.Should().BeNull("provider catches InvalidDataException and returns null");
    }

    [Fact]
    public async Task StreamFramesAsync_yields_consecutive_frames()
    {
        var (provider, session, transfer) = Build();
        session.SetState(HeyCyanState.Connected);
        session.SetMediaCount(new HeyCyanMediaCount(0, 0, 0));

        // Setup: dynamically add photos as they're "captured"
        var frame1 = new byte[] { 0xFF, 0xD8, 0x01 };
        var frame2 = new byte[] { 0xFF, 0xD8, 0x02 };
        transfer.Files["a.jpg"] = frame1;
        transfer.Files["b.jpg"] = frame2;

        int callCount = 0;
        session.OnPhotoTrigger = () =>
        {
            callCount++;
            session.SetMediaCount(new HeyCyanMediaCount(callCount, 0, 0));
            
            // Add the new photo entry when captured
            if (callCount == 1)
            {
                transfer.Entries.Add(new HeyCyanMediaEntry(
                    "a.jpg", 100, DateTimeOffset.UtcNow, HeyCyanMediaKind.Photo));
            }
            else if (callCount == 2)
            {
                transfer.Entries.Add(new HeyCyanMediaEntry(
                    "b.jpg", 100, DateTimeOffset.UtcNow.AddSeconds(1), HeyCyanMediaKind.Photo));
            }
        };

        using var cts = new CancellationTokenSource();
        var frames = new List<byte[]>();

        await foreach (var frame in provider.StreamFramesAsync(cts.Token))
        {
            frames.Add(frame);
            if (frames.Count >= 2)
            {
                cts.Cancel();
                break;
            }
        }

        frames.Should().HaveCount(2);
        frames[0].Should().BeEquivalentTo(frame1);
        frames[1].Should().BeEquivalentTo(frame2);
    }

    [Fact]
    public async Task Disconnected_event_raised_when_session_disconnects()
    {
        var (provider, session, _) = Build();
        session.SetState(HeyCyanState.Connected);

        bool disconnectedFired = false;
        provider.Disconnected += (s, e) => disconnectedFired = true;

        session.SetState(HeyCyanState.Disconnected);

        disconnectedFired.Should().BeTrue();
    }

    [Fact]
    public async Task CaptureFrameAsync_TwiceWithinWarmWindow_EntersTransferModeOnce()
    {
        // Build with real transfer + fake session + fake http factory + fake time
        var session = new FakeSessionForWarmTests();
        var httpFactory = new FakeHttpClientFactory();
        var time = new FakeTimeProvider();
        var transfer = new HeyCyanMediaTransfer(
            session,
            httpFactory,
            NullLogger<HeyCyanMediaTransfer>.Instance,
            time,
            warmIdle: TimeSpan.FromSeconds(8));
        
        var provider = new HeyCyanCameraProvider(
            session,
            transfer,
            NullLogger<HeyCyanCameraProvider>.Instance);

        session.SetState(HeyCyanState.Connected);
        session.SetMediaCount(new HeyCyanMediaCount(0, 0, 0));

        // Setup two captures
        var jpg1 = new byte[] { 0xFF, 0xD8, 0x01 };
        var jpg2 = new byte[] { 0xFF, 0xD8, 0x02 };
        httpFactory.ScriptedMediaConfig = "IMG_A.jpg";
        httpFactory.ScriptedFileContent["IMG_A.jpg"] = jpg1;
        
        int photoCount = 0;
        session.OnPhotoTrigger = () =>
        {
            photoCount++;
            session.SetMediaCount(new HeyCyanMediaCount(photoCount, 0, 0));
            
            // Update media config for next capture
            if (photoCount == 2)
            {
                httpFactory.ScriptedMediaConfig = "IMG_A.jpg\nIMG_B.jpg";
                httpFactory.ScriptedFileContent["IMG_B.jpg"] = jpg2;
            }
        };

        // First capture
        var frame1 = await provider.CaptureFrameAsync(CancellationToken.None);
        frame1.Should().BeEquivalentTo(jpg1);

        // Advance within warm window
        time.Advance(TimeSpan.FromSeconds(3));

        // Second capture
        var frame2 = await provider.CaptureFrameAsync(CancellationToken.None);
        frame2.Should().BeEquivalentTo(jpg2);

        // Should have entered transfer mode only once
        session.EnterCount.Should().Be(1);
    }

    [Fact]
    public async Task CaptureFrameAsync_AfterWarmIdleElapsed_ReentersTransferMode()
    {
        // Build with real transfer + fake session + fake http factory + fake time
        var session = new FakeSessionForWarmTests();
        var httpFactory = new FakeHttpClientFactory();
        var time = new FakeTimeProvider();
        var transfer = new HeyCyanMediaTransfer(
            session,
            httpFactory,
            NullLogger<HeyCyanMediaTransfer>.Instance,
            time,
            warmIdle: TimeSpan.FromSeconds(8));
        
        var provider = new HeyCyanCameraProvider(
            session,
            transfer,
            NullLogger<HeyCyanCameraProvider>.Instance);

        session.SetState(HeyCyanState.Connected);
        session.SetMediaCount(new HeyCyanMediaCount(0, 0, 0));

        // Setup two captures
        var jpg1 = new byte[] { 0xFF, 0xD8, 0x01 };
        var jpg2 = new byte[] { 0xFF, 0xD8, 0x02 };
        httpFactory.ScriptedMediaConfig = "IMG_A.jpg";
        httpFactory.ScriptedFileContent["IMG_A.jpg"] = jpg1;
        
        int photoCount = 0;
        session.OnPhotoTrigger = () =>
        {
            photoCount++;
            session.SetMediaCount(new HeyCyanMediaCount(photoCount, 0, 0));
            
            // Update media config for next capture
            if (photoCount == 2)
            {
                httpFactory.ScriptedMediaConfig = "IMG_A.jpg\nIMG_B.jpg";
                httpFactory.ScriptedFileContent["IMG_B.jpg"] = jpg2;
            }
        };

        // First capture
        var frame1 = await provider.CaptureFrameAsync(CancellationToken.None);
        frame1.Should().BeEquivalentTo(jpg1);
        session.EnterCount.Should().Be(1);

        // Advance past warm window
        time.Advance(TimeSpan.FromSeconds(9));
        await Task.Delay(50); // allow idle teardown task to run
        session.ExitCount.Should().Be(1);

        // Second capture should re-enter transfer mode
        var frame2 = await provider.CaptureFrameAsync(CancellationToken.None);
        frame2.Should().BeEquivalentTo(jpg2);

        session.EnterCount.Should().Be(2, "should re-enter transfer mode after idle timeout");
        session.ExitCount.Should().Be(1);
    }

    // Fake implementations for testing

    private sealed class FakeSession : IHeyCyanGlassesSession
    {
        private HeyCyanState _state = HeyCyanState.Disconnected;
        private HeyCyanMediaCount? _lastMediaCount;

        public HeyCyanState State => _state;
        public HeyCyanDeviceInfo? Device => null;
        public HeyCyanMediaCount? LastMediaCount => _lastMediaCount;

        public int TakePhotoCallCount { get; private set; }
        public Action? OnPhotoTrigger { get; set; }

        public event EventHandler<HeyCyanState>? StateChanged;
        public event EventHandler<HeyCyanBattery>? BatteryUpdated;
        public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
        public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
        public event EventHandler<byte[]>? AiPhotoReceived;

        public void SetState(HeyCyanState state)
        {
            _state = state;
            StateChanged?.Invoke(this, state);
        }

        public void SetMediaCount(HeyCyanMediaCount count)
        {
            _lastMediaCount = count;
            MediaCountUpdated?.Invoke(this, count);
        }

        public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan _, CancellationToken __) =>
            Task.FromResult<IReadOnlyList<HeyCyanDeviceInfo>>(Array.Empty<HeyCyanDeviceInfo>());

        public Task ConnectAsync(HeyCyanDeviceInfo _, CancellationToken __) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken _) => Task.CompletedTask;

        public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken _) =>
            Task.FromResult(new HeyCyanVersionInfo("hw", "fw", "whw", "wfw", "00:00:00:00:00:00"));

        public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken _) =>
            Task.FromResult(new HeyCyanBattery(100, false));

        public Task SyncTimeAsync(CancellationToken _) => Task.CompletedTask;

        public Task TakePhotoAsync(CancellationToken _)
        {
            TakePhotoCallCount++;
            OnPhotoTrigger?.Invoke();
            return Task.CompletedTask;
        }

        public Task StartVideoAsync(CancellationToken _) => Task.CompletedTask;
        public Task StopVideoAsync(CancellationToken _) => Task.CompletedTask;
        public Task StartAudioAsync(CancellationToken _) => Task.CompletedTask;
        public Task StopAudioAsync(CancellationToken _) => Task.CompletedTask;
        public Task TakeAiPhotoAsync(CancellationToken _) => Task.CompletedTask;

        public Task<HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken _) =>
            Task.FromResult(new HeyCyanTransferSession("http://192.168.49.10/", Array.Empty<string>()));

        public Task ExitTransferModeAsync(CancellationToken _) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }

    private sealed class FakeTransfer : IHeyCyanMediaTransfer
    {
        public List<HeyCyanMediaEntry> Entries { get; } = new();
        public Dictionary<string, byte[]> Files { get; } = new();
        
        public int ListCallCount { get; private set; }
        public int DownloadCallCount { get; private set; }
        public string? DownloadedFile { get; private set; }

        public bool IsWarm => false;

        public Task<IReadOnlyList<HeyCyanMediaEntry>> ListAsync(CancellationToken _)
        {
            ListCallCount++;
            return Task.FromResult<IReadOnlyList<HeyCyanMediaEntry>>(Entries);
        }

        public Task<byte[]> DownloadAsync(string fileName, CancellationToken _)
        {
            DownloadCallCount++;
            DownloadedFile = fileName;
            
            if (Files.TryGetValue(fileName, out var bytes))
                return Task.FromResult(bytes);
            
            throw new InvalidOperationException($"File not found: {fileName}");
        }

        public Task<Stream> OpenAsync(string fileName, CancellationToken ct)
        {
            DownloadCallCount++;
            DownloadedFile = fileName;
            
            if (Files.TryGetValue(fileName, out var bytes))
                return Task.FromResult<Stream>(new MemoryStream(bytes));
            
            throw new InvalidOperationException($"File not found: {fileName}");
        }

        public Task ExitAsync(CancellationToken _) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }

    private sealed class FakeSessionForWarmTests : IHeyCyanGlassesSession
    {
        private HeyCyanState _state = HeyCyanState.Disconnected;
        private HeyCyanMediaCount? _lastMediaCount;

        public int EnterCount { get; private set; }
        public int ExitCount { get; private set; }
        public Action? OnPhotoTrigger { get; set; }

        public HeyCyanState State => _state;
        public HeyCyanDeviceInfo? Device => null;
        public HeyCyanMediaCount? LastMediaCount => _lastMediaCount;

        public event EventHandler<HeyCyanState>? StateChanged;
        public event EventHandler<HeyCyanBattery>? BatteryUpdated;
        public event EventHandler<HeyCyanButtonEvent>? ButtonPressed;
        public event EventHandler<HeyCyanMediaCount>? MediaCountUpdated;
        public event EventHandler<byte[]>? AiPhotoReceived;

        public void SetState(HeyCyanState state)
        {
            _state = state;
            StateChanged?.Invoke(this, state);
        }

        public void SetMediaCount(HeyCyanMediaCount count)
        {
            _lastMediaCount = count;
            MediaCountUpdated?.Invoke(this, count);
        }

        public Task<IReadOnlyList<HeyCyanDeviceInfo>> ScanAsync(TimeSpan _, CancellationToken __) =>
            Task.FromResult<IReadOnlyList<HeyCyanDeviceInfo>>(Array.Empty<HeyCyanDeviceInfo>());

        public Task ConnectAsync(HeyCyanDeviceInfo _, CancellationToken __) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken _) => Task.CompletedTask;

        public Task<HeyCyanVersionInfo> GetVersionAsync(CancellationToken _) =>
            Task.FromResult(new HeyCyanVersionInfo("hw", "fw", "whw", "wfw", "00:00:00:00:00:00"));

        public Task<HeyCyanBattery> GetBatteryAsync(CancellationToken _) =>
            Task.FromResult(new HeyCyanBattery(100, false));

        public Task SyncTimeAsync(CancellationToken _) => Task.CompletedTask;

        public Task TakePhotoAsync(CancellationToken _)
        {
            OnPhotoTrigger?.Invoke();
            return Task.CompletedTask;
        }

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
                if (path.EndsWith("media.config"))
                    return Task.FromResult(_factory.ScriptedMediaConfig);
                throw new InvalidOperationException($"Unexpected path: {path}");
            }

            public Task<byte[]> GetByteArrayAsync(string path, CancellationToken _)
            {
                var fileName = path.Replace("/files/", "");
                if (_factory.ScriptedFileContent.TryGetValue(fileName, out var bytes))
                    return Task.FromResult(bytes);
                throw new InvalidOperationException($"File not found: {fileName}");
            }

            public Task<Stream> GetStreamAsync(string path, CancellationToken _)
            {
                var fileName = path.Replace("/files/", "");
                if (_factory.ScriptedFileContent.TryGetValue(fileName, out var bytes))
                    return Task.FromResult<Stream>(new MemoryStream(bytes));
                throw new InvalidOperationException($"File not found: {fileName}");
            }

            public ValueTask DisposeAsync() => default;
        }
    }
}
