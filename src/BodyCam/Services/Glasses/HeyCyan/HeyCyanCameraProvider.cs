using BodyCam.Services.Camera;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Camera provider for HeyCyan smart glasses.
/// Implements file-based snapshot capture: triggers BLE photo command,
/// waits for the captured file to settle, downloads JPG via Wi-Fi Direct HTTP.
/// Does NOT support live streaming (these are not RTSP/MJPEG cameras).
/// </summary>
public sealed class HeyCyanCameraProvider : ICameraProvider
{
    private static readonly TimeSpan DefaultPhotoSettleDelay = TimeSpan.FromSeconds(5);

    public string ProviderId => "heycyan-glasses";
    public string DisplayName => "HeyCyan Glasses Camera";
    public bool SupportsVideoRecording => false;

    public bool IsAvailable =>
        _session.State is HeyCyanState.Connected or HeyCyanState.TransferMode;

    public bool IsStoredImageDownloadFallback => _transfer is IHeyCyanStoredImageMediaTransfer;

    public event EventHandler? Disconnected;

    private readonly IHeyCyanGlassesSession _session;
    private readonly IHeyCyanMediaTransfer _transfer;
    private readonly ILogger<HeyCyanCameraProvider> _log;
    private readonly TimeSpan _photoSettleDelay;
    private bool _started;

    public HeyCyanCameraProvider(
        IHeyCyanGlassesSession session,
        IHeyCyanMediaTransfer transfer,
        ILogger<HeyCyanCameraProvider> log,
        TimeSpan? photoSettleDelay = null)
    {
        _session = session;
        _transfer = transfer;
        _log = log;
        _photoSettleDelay = photoSettleDelay ?? DefaultPhotoSettleDelay;

        _session.StateChanged += (s, state) =>
        {
            if (state == HeyCyanState.Disconnected)
                Disconnected?.Invoke(this, EventArgs.Empty);
        };
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _started = false;
        return Task.CompletedTask;
    }

    public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _log.LogWarning("HeyCyan glasses are not connected");
            return null;
        }

        try
        {
            if (_transfer is IHeyCyanStoredImageMediaTransfer storedImageTransfer)
            {
                _log.LogWarning(
                    "HeyCyan: triggering real photo capture, then using stored-image download fallback");
                await _session.TakePhotoAsync(ct).ConfigureAwait(false);

                var fallbackJpg = await _transfer
                    .DownloadAsync(storedImageTransfer.FallbackFileName, ct)
                    .ConfigureAwait(false);
                AssertJpegMagic(fallbackJpg);

                _log.LogWarning(
                    "HeyCyan: stored-image download fallback returned {Size} bytes",
                    fallbackJpg.Length);
                return fallbackJpg;
            }

            // 1. Snapshot the current photo count for logging only. Opening transfer mode
            // before capture can make the glasses serve a stale or empty media.config.
            var beforeCount = _session.LastMediaCount?.Photos ?? 0;

            // 2. Trigger BLE photo capture
            _log.LogInformation("HeyCyan: triggering photo capture (current count={Count})", beforeCount);
            await _session.TakePhotoAsync(ct).ConfigureAwait(false);

            // 3. Give the glasses time to finalize the file before entering transfer mode.
            // The Android proof showed capture-first-then-transfer is the reliable path.
            await DelayAfterPhotoCaptureAsync(ct).ConfigureAwait(false);

            // 4. Enter transfer mode through the warm helper and pick the newest .jpg.
            var newName = await FindNewestPhotoNameAsync(ct).ConfigureAwait(false);
            if (newName is null)
            {
                _log.LogError("No photo found on glasses after capture");
                return null;
            }

            // 5. Download via the warm transfer helper
            var jpg = await _transfer.DownloadAsync(newName, ct).ConfigureAwait(false);
            AssertJpegMagic(jpg);
            
            _log.LogInformation("HeyCyan: captured {Size} bytes", jpg.Length);
            return jpg;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HeyCyan photo capture failed");
            return null;
        }
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        _log.LogInformation("HeyCyan: starting frame stream (file-based polling)");
        
        while (!ct.IsCancellationRequested && IsAvailable)
        {
            var frame = await CaptureFrameAsync(ct).ConfigureAwait(false);
            if (frame is not null)
            {
                yield return frame;
            }

            // Brief delay between captures to avoid overwhelming the glasses
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        // The provider does NOT own the session or transfer (both are DI singletons)
        return ValueTask.CompletedTask;
    }

    private async Task DelayAfterPhotoCaptureAsync(CancellationToken ct)
    {
        if (_photoSettleDelay <= TimeSpan.Zero)
            return;

        await Task.Delay(_photoSettleDelay, ct).ConfigureAwait(false);
    }

    private async Task<string?> FindNewestPhotoNameAsync(CancellationToken ct)
    {
        var entries = await _transfer.ListAsync(ct).ConfigureAwait(false);
        return entries
            .Where(e => e.Kind == HeyCyanMediaKind.Photo)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault()?.Name;
    }

    /// <summary>
    /// Validate that the bytes start with JPEG SOI marker (FF D8).
    /// </summary>
    private void AssertJpegMagic(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            var hex = bytes.Length >= 2 ? $"{bytes[0]:X2}{bytes[1]:X2}" : "empty";
            _log.LogError("HeyCyan returned non-JPEG bytes (len={Length}, first2={Hex})", bytes.Length, hex);
            throw new InvalidDataException(
                $"HeyCyan returned non-JPEG bytes (len={bytes.Length}, first2={hex}).");
        }
    }
}
