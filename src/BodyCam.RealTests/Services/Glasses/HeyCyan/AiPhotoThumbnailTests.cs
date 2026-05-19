using System.Collections.Concurrent;
using BodyCam.RealTests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.Services.Glasses.HeyCyan;

/// <summary>
/// Test: send TakeAiPhoto and capture all BLE notifications to see
/// if the glasses send a thumbnail back over BLE.
/// </summary>
[Trait("Category", "RealBLE")]
public sealed class AiPhotoThumbnailTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private WindowsHeyCyanRealFixture? _fixture;

    private static bool RealEnabled =>
        Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN") == "1";

    public AiPhotoThumbnailTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!RealEnabled) return;
        _fixture = await WindowsHeyCyanRealFixture.CreateAsync();

        var mac = Environment.GetEnvironmentVariable("BODYCAM_REAL_HEYCYAN_MAC")
            ?? "D8:79:B8:7F:E6:C9";
        await _fixture.ConnectByAddressAsync(mac, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }

    [SkippableFact]
    public async Task TakeAiPhoto_CapturesBleNotifications()
    {
        Skip.IfNot(RealEnabled, "BODYCAM_REAL_HEYCYAN not set");
        Skip.If(_fixture == null, "Fixture not initialized");

        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;
        var notifications = new ConcurrentBag<byte[]>();
        int totalBytes = 0;

        // Subscribe to ALL raw BLE notifications
        _fixture!.Session.RawNotifyReceived += (_, data) =>
        {
            notifications.Add(data);
            Interlocked.Add(ref totalBytes, data.Length);
        };

        _output.WriteLine("Sending TakeAiPhoto command...");
        await _fixture.Session.TakeAiPhotoAsync(ct);
        _output.WriteLine("Command sent. Listening for BLE notifications for 15 seconds...");

        // Collect notifications for 15 seconds
        await Task.Delay(15_000, ct);

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"Total notifications received: {notifications.Count}");
        _output.WriteLine($"Total bytes received: {totalBytes}");

        // Group by action byte (byte[1] after 0xBC header)
        var grouped = notifications
            .Where(n => n.Length >= 2 && n[0] == 0xBC)
            .GroupBy(n => n[1])
            .OrderByDescending(g => g.Sum(n => n.Length));

        foreach (var group in grouped)
        {
            var action = group.Key;
            var count = group.Count();
            var bytes = group.Sum(n => n.Length);
            _output.WriteLine($"\n  Action 0x{action:X2}: {count} packets, {bytes} bytes total");

            // Show first 3 packets for each action
            foreach (var pkt in group.Take(3))
            {
                var hex = BitConverter.ToString(pkt);
                if (hex.Length > 120) hex = hex[..120] + "...";
                _output.WriteLine($"    [{pkt.Length}B] {hex}");
            }
            if (count > 3)
                _output.WriteLine($"    ... and {count - 3} more");
        }

        // Check for any large notifications (likely image data)
        var largeNotifs = notifications.Where(n => n.Length > 50).ToList();
        if (largeNotifs.Count > 0)
        {
            _output.WriteLine($"\n=== Large notifications (>50 bytes) ===");
            foreach (var pkt in largeNotifs.Take(10))
            {
                _output.WriteLine($"  [{pkt.Length}B] {BitConverter.ToString(pkt[..Math.Min(pkt.Length, 60)]).Replace("-", " ")}...");
            }
        }

        // Check for JPEG markers (FFD8 = SOI, FFD9 = EOI)
        var allData = notifications.SelectMany(n => n).ToArray();
        var jpegStart = FindPattern(allData, new byte[] { 0xFF, 0xD8, 0xFF });
        var jpegEnd = FindPattern(allData, new byte[] { 0xFF, 0xD9 });
        if (jpegStart >= 0)
        {
            _output.WriteLine($"\n*** JPEG SOI marker found at byte offset {jpegStart}! ***");
            if (jpegEnd > jpegStart)
                _output.WriteLine($"*** JPEG EOI marker found at byte offset {jpegEnd}. Image size ~{jpegEnd - jpegStart + 2} bytes ***");
        }
        else
        {
            _output.WriteLine("\nNo JPEG markers found in notification stream.");
        }
    }

    private static int FindPattern(byte[] data, byte[] pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
