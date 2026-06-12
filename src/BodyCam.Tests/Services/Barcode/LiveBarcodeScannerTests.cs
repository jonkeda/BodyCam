using System.Runtime.CompilerServices;
using BodyCam.Models;
using BodyCam.Services.Barcode;
using BodyCam.Services.QrCode;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Barcode;

public sealed class LiveBarcodeScannerTests
{
    [Fact]
    public async Task WatchAsync_EmitsDetectionAfterStableRepeatedValue()
    {
        var scanner = new SequenceQrCodeScanner(
            new QrScanResult("4006420012345", QrCodeFormat.Ean13, DateTimeOffset.UtcNow),
            new QrScanResult("4006420012345", QrCodeFormat.Ean13, DateTimeOffset.UtcNow));
        var liveScanner = CreateScanner(scanner);

        var detections = await CollectAsync(liveScanner.WatchAsync(
            Frames(2),
            StableOptions()));

        detections.Should().ContainSingle();
        detections[0].Value.Should().Be("4006420012345");
        detections[0].Format.Should().Be(QrCodeFormat.Ean13);
    }

    [Fact]
    public async Task WatchAsync_DoesNotEmitForSingleFrameNoise()
    {
        var scanner = new SequenceQrCodeScanner(
            new QrScanResult("noise", QrCodeFormat.QrCode, DateTimeOffset.UtcNow),
            null);
        var liveScanner = CreateScanner(scanner);

        var detections = await CollectAsync(liveScanner.WatchAsync(
            Frames(2),
            StableOptions()));

        detections.Should().BeEmpty();
    }

    [Fact]
    public async Task WatchAsync_SuppressesDuplicateValueDuringCooldown()
    {
        var scanner = new SequenceQrCodeScanner(
            new QrScanResult("4006420012345", QrCodeFormat.Ean13, DateTimeOffset.UtcNow),
            new QrScanResult("4006420012345", QrCodeFormat.Ean13, DateTimeOffset.UtcNow),
            new QrScanResult("4006420012345", QrCodeFormat.Ean13, DateTimeOffset.UtcNow),
            new QrScanResult("4006420012345", QrCodeFormat.Ean13, DateTimeOffset.UtcNow));
        var liveScanner = CreateScanner(scanner);

        var detections = await CollectAsync(liveScanner.WatchAsync(
            Frames(4),
            StableOptions()));

        detections.Should().ContainSingle();
    }

    private static LiveBarcodeScanner CreateScanner(IQrCodeScanner scanner) =>
        new(scanner, NullLogger<LiveBarcodeScanner>.Instance);

    private static LiveBarcodeScannerOptions StableOptions() =>
        new(
            SampleInterval: TimeSpan.Zero,
            StabilityThreshold: 2,
            DuplicateCooldown: TimeSpan.FromMinutes(1));

    private static async Task<List<LiveBarcodeDetection>> CollectAsync(
        IAsyncEnumerable<LiveBarcodeDetection> source)
    {
        var result = new List<LiveBarcodeDetection>();
        await foreach (var item in source)
            result.Add(item);
        return result;
    }

    private static async IAsyncEnumerable<byte[]> Frames(
        int count,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return [(byte)i];
            await Task.Yield();
        }
    }

    private sealed class SequenceQrCodeScanner : IQrCodeScanner
    {
        private readonly Queue<QrScanResult?> _results;

        public SequenceQrCodeScanner(params QrScanResult?[] results)
        {
            _results = new Queue<QrScanResult?>(results);
        }

        public Task<QrScanResult?> ScanAsync(byte[] jpegFrame, CancellationToken ct = default)
        {
            var result = _results.Count == 0 ? null : _results.Dequeue();
            return Task.FromResult(result);
        }
    }
}
