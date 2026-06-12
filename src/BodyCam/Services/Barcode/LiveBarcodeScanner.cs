using System.Runtime.CompilerServices;
using BodyCam.Models;
using BodyCam.Services.Camera;
using BodyCam.Services.QrCode;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Barcode;

public sealed record LiveBarcodeScannerOptions(
    TimeSpan? SampleInterval = null,
    int StabilityThreshold = 2,
    TimeSpan? DuplicateCooldown = null)
{
    public TimeSpan EffectiveSampleInterval => SampleInterval ?? TimeSpan.FromMilliseconds(250);
    public TimeSpan EffectiveDuplicateCooldown => DuplicateCooldown ?? TimeSpan.FromSeconds(5);
}

public sealed record LiveBarcodeDetection(
    string Value,
    QrCodeFormat Format,
    DateTimeOffset DetectedAt);

public interface ILiveBarcodeScanner
{
    IAsyncEnumerable<LiveBarcodeDetection> WatchAsync(
        IAsyncEnumerable<byte[]> frames,
        LiveBarcodeScannerOptions? options = null,
        CancellationToken ct = default);
}

public sealed class LiveBarcodeScanner : ILiveBarcodeScanner
{
    private readonly IQrCodeScanner _scanner;
    private readonly ILogger<LiveBarcodeScanner> _log;

    public LiveBarcodeScanner(IQrCodeScanner scanner, ILogger<LiveBarcodeScanner> log)
    {
        _scanner = scanner;
        _log = log;
    }

    public async IAsyncEnumerable<LiveBarcodeDetection> WatchAsync(
        IAsyncEnumerable<byte[]> frames,
        LiveBarcodeScannerOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var resolved = options ?? new LiveBarcodeScannerOptions();
        var threshold = Math.Max(1, resolved.StabilityThreshold);
        var sampleInterval = resolved.EffectiveSampleInterval;
        var duplicateCooldown = resolved.EffectiveDuplicateCooldown;
        var lastDecodeAttempt = DateTimeOffset.MinValue;
        var candidateValue = string.Empty;
        var candidateFormat = QrCodeFormat.Unknown;
        var candidateHits = 0;
        var recentlyEmitted = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        await foreach (var frame in frames.WithCancellation(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var now = DateTimeOffset.UtcNow;
            if (now - lastDecodeAttempt < sampleInterval)
                continue;

            lastDecodeAttempt = now;

            QrScanResult? scan;
            try
            {
                scan = await _scanner.ScanAsync(frame, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Live barcode frame decode failed");
                continue;
            }

            if (scan is null || string.IsNullOrWhiteSpace(scan.Content))
            {
                candidateValue = string.Empty;
                candidateFormat = QrCodeFormat.Unknown;
                candidateHits = 0;
                continue;
            }

            var value = scan.Content.Trim();
            if (string.Equals(candidateValue, value, StringComparison.Ordinal)
                && candidateFormat == scan.Format)
            {
                candidateHits++;
            }
            else
            {
                candidateValue = value;
                candidateFormat = scan.Format;
                candidateHits = 1;
            }

            if (candidateHits < threshold)
                continue;

            if (recentlyEmitted.TryGetValue(value, out var emittedAt)
                && now - emittedAt < duplicateCooldown)
            {
                continue;
            }

            recentlyEmitted[value] = now;
            candidateHits = 0;

            yield return new LiveBarcodeDetection(value, scan.Format, now);
        }
    }
}
