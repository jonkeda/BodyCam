using BodyCam.Services.Glasses.HeyCyan.Media;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan.Media;

public class HeyCyanRecordedMediaServiceTests
{
    [Fact]
    public void RecordedMediaClassifier_classifies_photo_extensions()
    {
        RecordedMediaClassifier.Classify("IMG_001.jpg").Should().Be(RecordedMediaKind.Photo);
        RecordedMediaClassifier.Classify("IMG_001.jpeg").Should().Be(RecordedMediaKind.Photo);
        RecordedMediaClassifier.Classify("IMG_001.png").Should().Be(RecordedMediaKind.Photo);
        RecordedMediaClassifier.Classify("IMG_001.JPG").Should().Be(RecordedMediaKind.Photo); // case-insensitive
    }

    [Fact]
    public void RecordedMediaClassifier_classifies_video_extensions()
    {
        RecordedMediaClassifier.Classify("VID_001.mp4").Should().Be(RecordedMediaKind.Video);
        RecordedMediaClassifier.Classify("VID_001.mov").Should().Be(RecordedMediaKind.Video);
        RecordedMediaClassifier.Classify("VID_001.MP4").Should().Be(RecordedMediaKind.Video);
    }

    [Fact]
    public void RecordedMediaClassifier_classifies_audio_extensions()
    {
        RecordedMediaClassifier.Classify("AUD_001.opus").Should().Be(RecordedMediaKind.Audio);
        RecordedMediaClassifier.Classify("AUD_001.ogg").Should().Be(RecordedMediaKind.Audio);
        RecordedMediaClassifier.Classify("AUD_001.OPUS").Should().Be(RecordedMediaKind.Audio);
    }

    [Fact]
    public void RecordedMediaClassifier_returns_unknown_for_other_extensions()
    {
        RecordedMediaClassifier.Classify("file.txt").Should().Be(RecordedMediaKind.Unknown);
        RecordedMediaClassifier.Classify("file.pdf").Should().Be(RecordedMediaKind.Unknown);
        RecordedMediaClassifier.Classify("file").Should().Be(RecordedMediaKind.Unknown);
    }

    [Fact]
    public async Task EnumerateAsync_parses_media_config_and_classifies_files()
    {
        var (service, transfer, _, _, _) = Build();
        transfer.ScriptedMediaConfig = """
            IMG_001.jpg
            VID_001.mp4
            AUD_001.opus
            README.txt
            """;

        var items = await service.EnumerateAsync(CancellationToken.None).ToListAsync();

        items.Should().HaveCount(4);
        items[0].FileName.Should().Be("IMG_001.jpg");
        items[0].Kind.Should().Be(RecordedMediaKind.Photo);
        items[1].FileName.Should().Be("VID_001.mp4");
        items[1].Kind.Should().Be(RecordedMediaKind.Video);
        items[2].FileName.Should().Be("AUD_001.opus");
        items[2].Kind.Should().Be(RecordedMediaKind.Audio);
        items[3].FileName.Should().Be("README.txt");
        items[3].Kind.Should().Be(RecordedMediaKind.Unknown);
    }

    [Fact]
    public async Task ImportAsync_photo_passes_through_bytes_to_store()
    {
        var (service, transfer, store, _, _) = Build();
        var jpgBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic
        transfer.ScriptedFiles["IMG_001.jpg"] = jpgBytes;

        var item = new RecordedMediaItem("IMG_001.jpg", RecordedMediaKind.Photo, 1024, null);
        var result = await service.ImportAsync(item, CancellationToken.None);

        result.Source.Should().Be(item);
        result.LocalUri.Should().Be("content://photo/IMG_001.jpg");
        result.BytesWritten.Should().Be(4);
        store.SavedImages.Should().ContainKey("IMG_001.jpg");
        store.SavedImages["IMG_001.jpg"].Should().Equal(jpgBytes);
    }

    [Fact]
    public async Task ImportAsync_video_passes_through_bytes_to_store()
    {
        var (service, transfer, store, _, _) = Build();
        var mp4Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 }; // MP4 magic
        transfer.ScriptedFiles["VID_001.mp4"] = mp4Bytes;

        var item = new RecordedMediaItem("VID_001.mp4", RecordedMediaKind.Video, 2048, null);
        var result = await service.ImportAsync(item, CancellationToken.None);

        result.Source.Should().Be(item);
        result.LocalUri.Should().Be("content://video/VID_001.mp4");
        result.BytesWritten.Should().Be(8);
        store.SavedVideos.Should().ContainKey("VID_001.mp4");
        store.SavedVideos["VID_001.mp4"].Should().Equal(mp4Bytes);
    }

    [Fact]
    public async Task ImportAsync_audio_wraps_opus_to_ogg_and_renames()
    {
        var (service, transfer, store, _, _) = Build();
        // Fixed 40-byte raw OPUS packet (will be wrapped to Ogg)
        var opusBytes = new byte[40];
        transfer.ScriptedFiles["AUD_001.opus"] = opusBytes;

        var item = new RecordedMediaItem("AUD_001.opus", RecordedMediaKind.Audio, 40, null);
        var result = await service.ImportAsync(item, CancellationToken.None);

        result.Source.Should().Be(item);
        result.LocalUri.Should().Be("content://audio/AUD_001.ogg"); // renamed to .ogg
        store.SavedAudio.Should().ContainKey("AUD_001.ogg");
        store.SavedAudio["AUD_001.ogg"].Should().NotBeEmpty(); // Ogg wrapper adds headers
        store.SavedAudio["AUD_001.ogg"].Length.Should().BeGreaterThan(40); // wrapped
    }

    [Fact]
    public async Task ImportAsync_throws_for_unknown_kind()
    {
        var (service, _, _, _, _) = Build();
        var item = new RecordedMediaItem("file.txt", RecordedMediaKind.Unknown, 100, null);

        Func<Task> act = () => service.ImportAsync(item, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*unknown type*");
    }

    [Fact]
    public async Task ImportAsync_skips_if_already_exists()
    {
        var (service, transfer, store, _, _) = Build();
        var jpgBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        transfer.ScriptedFiles["IMG_001.jpg"] = jpgBytes;
        store.ExistingFiles.Add(("IMG_001.jpg", RecordedMediaKind.Photo));

        var item = new RecordedMediaItem("IMG_001.jpg", RecordedMediaKind.Photo, 1024, null);
        var result = await service.ImportAsync(item, CancellationToken.None);

        result.LocalUri.Should().BeEmpty(); // sentinel for "already exists"
        result.BytesWritten.Should().Be(0);
        store.SavedImages.Should().BeEmpty(); // not saved again
    }

    [Fact]
    public async Task ImportAllAsync_imports_all_non_unknown_files()
    {
        var (service, transfer, store, _, _) = Build();
        transfer.ScriptedMediaConfig = """
            IMG_001.jpg
            VID_001.mp4
            AUD_001.opus
            README.txt
            """;
        transfer.ScriptedFiles["IMG_001.jpg"] = new byte[] { 0xFF, 0xD8 };
        transfer.ScriptedFiles["VID_001.mp4"] = new byte[] { 0x00, 0x00 };
        transfer.ScriptedFiles["AUD_001.opus"] = new byte[40];

        var imported = await service.ImportAllAsync(null, CancellationToken.None).ToListAsync();

        imported.Should().HaveCount(3); // 3 known types, 1 Unknown skipped
        store.SavedImages.Should().ContainKey("IMG_001.jpg");
        store.SavedVideos.Should().ContainKey("VID_001.mp4");
        store.SavedAudio.Should().ContainKey("AUD_001.ogg");
    }

    [Fact]
    public async Task ImportAllAsync_reports_progress()
    {
        var (service, transfer, _, _, _) = Build();
        transfer.ScriptedMediaConfig = "IMG_001.jpg\nIMG_002.jpg";
        transfer.ScriptedFiles["IMG_001.jpg"] = new byte[] { 0x01 };
        transfer.ScriptedFiles["IMG_002.jpg"] = new byte[] { 0x02 };

        var progressReports = new List<RecordedMediaImportProgress>();
        var progress = new Progress<RecordedMediaImportProgress>(p => progressReports.Add(p));

        await service.ImportAllAsync(progress, CancellationToken.None).ToListAsync();

        progressReports.Should().HaveCount(2);
        progressReports[0].Completed.Should().Be(0);
        progressReports[0].Total.Should().Be(2);
        progressReports[0].CurrentFile.Should().Be("IMG_001.jpg");
        progressReports[1].Completed.Should().Be(1);
        progressReports[1].Total.Should().Be(2);
        progressReports[1].CurrentFile.Should().Be("IMG_002.jpg");
    }

    [Fact]
    public async Task DeleteRemoteAsync_returns_false_not_supported()
    {
        var (service, _, _, _, _) = Build();

        var result = await service.DeleteRemoteAsync("test.jpg", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ImportAsync_video_writes_sidecar_with_sha256()
    {
        var (service, transfer, store, session, sidecarWriter) = Build();
        var mp4Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };
        transfer.ScriptedFiles["VID_001.mp4"] = mp4Bytes;
        session.Device = new BodyCam.Services.Glasses.HeyCyan.HeyCyanDeviceInfo("Test", "AA:BB:CC:DD:EE:FF", -50);

        var item = new RecordedMediaItem("VID_001.mp4", RecordedMediaKind.Video, 8, null);
        await service.ImportAsync(item, CancellationToken.None);

        sidecarWriter.WrittenSidecars.Should().HaveCount(1);
        var (mediaUri, sidecar) = sidecarWriter.WrittenSidecars[0];
        mediaUri.Should().Be("content://video/VID_001.mp4");
        sidecar.Schema.Should().Be(1);
        sidecar.SourceFileName.Should().Be("VID_001.mp4");
        sidecar.GlassesMacAddress.Should().Be("AA:BB:CC:DD:EE:FF");
        sidecar.SizeBytes.Should().Be(8);
        sidecar.Sha256.Should().NotBeNullOrEmpty();

        // Verify SHA-256 matches the MP4 bytes
        var expectedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(mp4Bytes)).ToLowerInvariant();
        sidecar.Sha256.Should().Be(expectedSha);
    }

    [Fact]
    public async Task ImportAsync_audio_writes_sidecar_with_sha256_of_raw_opus()
    {
        var (service, transfer, store, session, sidecarWriter) = Build();
        var opusBytes = new byte[40]; // Fixed 40-byte raw OPUS packet
        opusBytes[0] = 0x42; // Some marker so we can verify it's the raw bytes
        transfer.ScriptedFiles["AUD_001.opus"] = opusBytes;
        session.Device = new BodyCam.Services.Glasses.HeyCyan.HeyCyanDeviceInfo("Test", "11:22:33:44:55:66", -60);

        var item = new RecordedMediaItem("AUD_001.opus", RecordedMediaKind.Audio, 40, null);
        await service.ImportAsync(item, CancellationToken.None);

        sidecarWriter.WrittenSidecars.Should().HaveCount(1);
        var (mediaUri, sidecar) = sidecarWriter.WrittenSidecars[0];
        mediaUri.Should().Be("content://audio/AUD_001.ogg");
        sidecar.SourceFileName.Should().Be("AUD_001.opus");
        sidecar.GlassesMacAddress.Should().Be("11:22:33:44:55:66");

        // SHA-256 should be of the RAW OPUS bytes, not the wrapped Ogg
        var expectedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(opusBytes)).ToLowerInvariant();
        sidecar.Sha256.Should().Be(expectedSha);

        // Verify the saved file is actually wrapped (bigger than raw)
        store.SavedAudio["AUD_001.ogg"].Length.Should().BeGreaterThan(40);
    }

    [Fact]
    public async Task ImportAsync_photo_does_not_write_sidecar()
    {
        var (service, transfer, store, session, sidecarWriter) = Build();
        var jpgBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        transfer.ScriptedFiles["IMG_001.jpg"] = jpgBytes;

        var item = new RecordedMediaItem("IMG_001.jpg", RecordedMediaKind.Photo, 4, null);
        await service.ImportAsync(item, CancellationToken.None);

        sidecarWriter.WrittenSidecars.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_uses_unknown_mac_when_session_device_is_null()
    {
        var (service, transfer, store, session, sidecarWriter) = Build();
        session.Device = null; // Simulate disconnect race
        var mp4Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };
        transfer.ScriptedFiles["VID_001.mp4"] = mp4Bytes;

        var item = new RecordedMediaItem("VID_001.mp4", RecordedMediaKind.Video, 8, null);
        await service.ImportAsync(item, CancellationToken.None);

        sidecarWriter.WrittenSidecars.Should().HaveCount(1);
        sidecarWriter.WrittenSidecars[0].sidecar.GlassesMacAddress.Should().Be("unknown");
    }

    [Fact]
    public async Task ImportAsync_includes_glasses_timestamp_in_sidecar()
    {
        var (service, transfer, store, session, sidecarWriter) = Build();
        var mp4Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };
        transfer.ScriptedFiles["VID_001.mp4"] = mp4Bytes;
        session.Device = new BodyCam.Services.Glasses.HeyCyan.HeyCyanDeviceInfo("Test", "AA:BB:CC:DD:EE:FF", -50);

        var glassesTime = new DateTimeOffset(2026, 4, 29, 10, 30, 0, TimeSpan.Zero);
        var item = new RecordedMediaItem("VID_001.mp4", RecordedMediaKind.Video, 8, glassesTime);
        await service.ImportAsync(item, CancellationToken.None);

        sidecarWriter.WrittenSidecars.Should().HaveCount(1);
        sidecarWriter.WrittenSidecars[0].sidecar.GlassesTimestamp.Should().Be(glassesTime);
    }

    // Helpers

    private static (HeyCyanRecordedMediaService service, FakeTransfer transfer, FakeMediaStore store, FakeSession session, FakeSidecarWriter sidecarWriter)
        Build()
    {
        var session = new FakeSession();
        var transfer = new FakeTransfer(session);
        var store = new FakeMediaStore();
        var sidecarWriter = new FakeSidecarWriter();
        var service = new HeyCyanRecordedMediaService(
            session,
            transfer,
            store,
            sidecarWriter,
            NullLogger<HeyCyanRecordedMediaService>.Instance);
        return (service, transfer, store, session, sidecarWriter);
    }

    private sealed class FakeSession : BodyCam.Services.Glasses.HeyCyan.IHeyCyanGlassesSession
    {
        public BodyCam.Services.Glasses.HeyCyan.HeyCyanState State => BodyCam.Services.Glasses.HeyCyan.HeyCyanState.Connected;
        public BodyCam.Services.Glasses.HeyCyan.HeyCyanDeviceInfo? Device { get; set; }
        public BodyCam.Services.Glasses.HeyCyan.HeyCyanMediaCount? LastMediaCount => null;

        public event EventHandler<BodyCam.Services.Glasses.HeyCyan.HeyCyanState>? StateChanged;
        public event EventHandler<BodyCam.Services.Glasses.HeyCyan.HeyCyanBattery>? BatteryUpdated;
        public event EventHandler<BodyCam.Services.Glasses.HeyCyan.HeyCyanButtonEvent>? ButtonPressed;
        public event EventHandler<BodyCam.Services.Glasses.HeyCyan.HeyCyanMediaCount>? MediaCountUpdated;
        public event EventHandler<byte[]>? AiPhotoReceived;

        public Task<IReadOnlyList<BodyCam.Services.Glasses.HeyCyan.HeyCyanDeviceInfo>> ScanAsync(TimeSpan _, CancellationToken __) =>
            Task.FromResult<IReadOnlyList<BodyCam.Services.Glasses.HeyCyan.HeyCyanDeviceInfo>>(Array.Empty<BodyCam.Services.Glasses.HeyCyan.HeyCyanDeviceInfo>());
        public Task ConnectAsync(BodyCam.Services.Glasses.HeyCyan.HeyCyanDeviceInfo _, CancellationToken __) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken _) => Task.CompletedTask;
        public Task<BodyCam.Services.Glasses.HeyCyan.HeyCyanVersionInfo> GetVersionAsync(CancellationToken _) =>
            Task.FromResult(new BodyCam.Services.Glasses.HeyCyan.HeyCyanVersionInfo("hw", "fw", "whw", "wfw", "00:00:00:00:00:00"));
        public Task<BodyCam.Services.Glasses.HeyCyan.HeyCyanBattery> GetBatteryAsync(CancellationToken _) =>
            Task.FromResult(new BodyCam.Services.Glasses.HeyCyan.HeyCyanBattery(100, false));
        public Task SyncTimeAsync(CancellationToken _) => Task.CompletedTask;
        public Task TakePhotoAsync(CancellationToken _) => Task.CompletedTask;
        public Task StartVideoAsync(CancellationToken _) => Task.CompletedTask;
        public Task StopVideoAsync(CancellationToken _) => Task.CompletedTask;
        public Task StartAudioAsync(CancellationToken _) => Task.CompletedTask;
        public Task StopAudioAsync(CancellationToken _) => Task.CompletedTask;
        public Task TakeAiPhotoAsync(CancellationToken _) => Task.CompletedTask;
        public Task<BodyCam.Services.Glasses.HeyCyan.HeyCyanTransferSession> EnterTransferModeAsync(CancellationToken _) =>
            Task.FromResult(new BodyCam.Services.Glasses.HeyCyan.HeyCyanTransferSession("http://192.168.49.10/", Array.Empty<string>()));
        public Task ExitTransferModeAsync(CancellationToken _) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private sealed class FakeTransfer : BodyCam.Services.Glasses.HeyCyan.IHeyCyanMediaTransfer
    {
        private readonly FakeSession _session;

        public FakeTransfer(FakeSession session)
        {
            _session = session;
        }

        public bool IsWarm { get; set; }
        public string ScriptedMediaConfig { get; set; } = string.Empty;
        public Dictionary<string, byte[]> ScriptedFiles { get; } = new();

        public async Task<IReadOnlyList<BodyCam.Services.Glasses.HeyCyan.HeyCyanMediaEntry>> ListAsync(CancellationToken ct)
        {
            // Parse the scripted media.config
            var lines = ScriptedMediaConfig.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Select(name =>
            {
                var kind = name.EndsWith(".jpg") || name.EndsWith(".jpeg") || name.EndsWith(".png") ?
                    BodyCam.Services.Glasses.HeyCyan.HeyCyanMediaKind.Photo :
                    name.EndsWith(".mp4") || name.EndsWith(".mov") ?
                    BodyCam.Services.Glasses.HeyCyan.HeyCyanMediaKind.Video :
                    name.EndsWith(".opus") || name.EndsWith(".ogg") ?
                    BodyCam.Services.Glasses.HeyCyan.HeyCyanMediaKind.Audio :
                    BodyCam.Services.Glasses.HeyCyan.HeyCyanMediaKind.Other;
                return new BodyCam.Services.Glasses.HeyCyan.HeyCyanMediaEntry(name, 0, DateTimeOffset.MinValue, kind);
            }).ToList();
        }

        public Task<byte[]> DownloadAsync(string fileName, CancellationToken ct)
        {
            if (ScriptedFiles.TryGetValue(fileName, out var bytes))
                return Task.FromResult(bytes);
            throw new InvalidOperationException($"File not scripted: {fileName}");
        }

        public Task<Stream> OpenAsync(string fileName, CancellationToken ct)
        {
            if (ScriptedFiles.TryGetValue(fileName, out var bytes))
                return Task.FromResult<Stream>(new MemoryStream(bytes));
            throw new InvalidOperationException($"File not scripted: {fileName}");
        }

        public Task ExitAsync(CancellationToken ct)
        {
            IsWarm = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }

    private sealed class FakeMediaStore : IMediaStore
    {
        public Dictionary<string, byte[]> SavedImages { get; } = new();
        public Dictionary<string, byte[]> SavedVideos { get; } = new();
        public Dictionary<string, byte[]> SavedAudio { get; } = new();
        public HashSet<(string fileName, RecordedMediaKind kind)> ExistingFiles { get; } = new();

        public async Task<string> SaveImageAsync(string fileName, Stream content, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            SavedImages[fileName] = ms.ToArray();
            return $"content://photo/{fileName}";
        }

        public async Task<string> SaveVideoAsync(string fileName, Stream content, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            SavedVideos[fileName] = ms.ToArray();
            return $"content://video/{fileName}";
        }

        public async Task<string> SaveAudioAsync(string fileName, string mimeType, Stream content, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            SavedAudio[fileName] = ms.ToArray();
            return $"content://audio/{fileName}";
        }

        public Task<bool> ExistsAsync(string fileName, RecordedMediaKind kind, CancellationToken ct)
        {
            // For audio, also check .ogg variant
            if (kind == RecordedMediaKind.Audio && Path.GetExtension(fileName) == ".opus")
            {
                var baseName = Path.GetFileNameWithoutExtension(fileName);
                var oggName = $"{baseName}.ogg";
                return Task.FromResult(ExistingFiles.Contains((oggName, kind)));
            }

            return Task.FromResult(ExistingFiles.Contains((fileName, kind)));
        }
    }

    private sealed class FakeSidecarWriter : ISidecarWriter
    {
        public List<(string mediaUri, RecordedMediaSidecar sidecar)> WrittenSidecars { get; } = new();

        public Task<string> WriteAsync(string mediaLocalUri, RecordedMediaSidecar sidecar, CancellationToken ct)
        {
            WrittenSidecars.Add((mediaLocalUri, sidecar));
            return Task.FromResult($"/sidecars/{sidecar.Sha256}.bodycam.json");
        }
    }
}
