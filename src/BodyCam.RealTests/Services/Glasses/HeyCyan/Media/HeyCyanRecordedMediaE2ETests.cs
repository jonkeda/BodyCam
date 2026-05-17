using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Glasses.HeyCyan.Media;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan.Media;

/// <summary>
/// End-to-end test for HeyCyan recorded media pipeline.
/// Requires physical HeyCyan glasses hardware.
/// </summary>
[Trait("RequiresGlasses", "true")]
public class HeyCyanRecordedMediaE2ETests
{
    /// <summary>
    /// Full workflow: connect → record audio → import → verify playback-ready Ogg/Opus.
    /// </summary>
    /// <remarks>
    /// This test is MANUAL — requires physical HeyCyan glasses paired via Bluetooth.
    /// Run with: dotnet test --filter "Trait=RequiresGlasses"
    /// 
    /// Prerequisites:
    /// 1. HeyCyan glasses powered on and in range
    /// 2. Bluetooth enabled on test device
    /// 3. Glasses paired (may require initial pairing via official app)
    /// 
    /// Test flow:
    /// 1. Scan and connect to glasses
    /// 2. Record 5-second audio clip
    /// 3. Enter transfer mode and import all media
    /// 4. Verify imported .ogg file is playable (starts with OggS magic)
    /// </remarks>
    [Fact(Skip = "Requires physical HeyCyan glasses — enable manually for hardware testing")]
    public async Task ConnectRecordImport_VoiceNote_PlaysBack()
    {
        // Arrange: Create session and services
        // Note: In a real test, you would inject or create the actual platform-specific session
        // For now, this is a skeleton that documents the expected flow
        var session = CreateRealSession();
        var transfer = CreateRealTransfer(session);
        var store = CreateRealMediaStore();
        var sidecarWriter = CreateRealSidecarWriter();
        var service = new HeyCyanRecordedMediaService(
            session,
            transfer,
            store,
            sidecarWriter,
            NullLogger<HeyCyanRecordedMediaService>.Instance);

        try
        {
            // Act 1: Scan and connect
            var devices = await session.ScanAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            devices.Should().NotBeEmpty("at least one HeyCyan device should be discoverable");

            var glasses = devices.First(d => d.Name.Contains("HeyCyan", StringComparison.OrdinalIgnoreCase));
            await session.ConnectAsync(glasses, CancellationToken.None);
            session.State.Should().Be(HeyCyanState.Connected);

            // Act 2: Record audio for 5 seconds
            await session.StartAudioAsync(CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(5));
            await session.StopAudioAsync(CancellationToken.None);

            // Act 3: Enter transfer mode and import
            await using var transferSession = await session.EnterTransferModeAsync(CancellationToken.None);
            var imports = await service.ImportAllAsync(null, CancellationToken.None).ToListAsync();

            // Assert: At least one audio file imported
            var audioImports = imports.Where(i => i.Source.Kind == RecordedMediaKind.Audio).ToList();
            audioImports.Should().NotBeEmpty("at least one audio file should be imported");

            // Assert: Imported audio is valid Ogg/Opus
            var newestAudio = audioImports.OrderByDescending(i => i.Source.GlassesTimestamp).First();
            newestAudio.LocalUri.Should().EndWith(".ogg", "audio should be renamed to .ogg after wrapping");
            
            // Verify the file exists and is playable
            var localPath = GetLocalPathFromUri(newestAudio.LocalUri);
            File.Exists(localPath).Should().BeTrue("imported audio file should exist on disk");

            var oggBytes = await File.ReadAllBytesAsync(localPath);
            oggBytes.Should().StartWith(new byte[] { 0x4F, 0x67, 0x67, 0x53 }, "file should start with OggS magic");

            // Smoke test: Try to open with platform media player
            // (This is platform-specific; for Android: MediaPlayer.Create, for iOS: AVAudioPlayer)
            // For now, we just verify the Ogg structure is valid
            oggBytes.Length.Should().BeGreaterThan(100, "wrapped Ogg should have headers + data");
        }
        finally
        {
            await session.DisconnectAsync(CancellationToken.None);
        }
    }

    // --- Test Helpers ---
    // These would be replaced with actual implementations in a real E2E test harness

    private static IHeyCyanGlassesSession CreateRealSession()
    {
        throw new NotImplementedException(
            "Replace with actual platform session: " +
            "Android: new AndroidHeyCyanGlassesSession(bridge, logger), " +
            "iOS: new IosHeyCyanGlassesSession(bridge, logger)");
    }

    private static IHeyCyanMediaTransfer CreateRealTransfer(IHeyCyanGlassesSession session)
    {
        throw new NotImplementedException(
            "Replace with actual transfer: " +
            "new HeyCyanMediaTransfer(session, httpClientFactory, logger)");
    }

    private static IMediaStore CreateRealMediaStore()
    {
        throw new NotImplementedException(
            "Replace with actual media store: " +
            "Android: new AndroidMediaStore(...), " +
            "iOS: new IosMediaStore(...)");
    }

    private static ISidecarWriter CreateRealSidecarWriter()
    {
        throw new NotImplementedException(
            "Replace with actual sidecar writer: " +
            "new JsonSidecarWriter(fileSystem, logger)");
    }

    private static string GetLocalPathFromUri(string uri)
    {
        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return Uri.UnescapeDataString(uri.Substring("file://".Length));

        if (uri.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            // On Android, content:// URIs need to be resolved via ContentResolver
            // For this test, we'd use the actual platform API
            throw new NotImplementedException("Resolve content:// URI via Android ContentResolver");
        }

        return uri;
    }
}
