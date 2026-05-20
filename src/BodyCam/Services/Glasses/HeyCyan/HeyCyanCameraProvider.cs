using BodyCam.Services.Camera;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Camera provider for HeyCyan smart glasses.
/// Implements file-based snapshot capture: triggers BLE photo command,
/// waits for media-count notification, downloads JPG via Wi-Fi Direct HTTP.
/// Does NOT support live streaming (these are not RTSP/MJPEG cameras).
/// </summary>
public sealed class HeyCyanCameraProvider : ICameraProvider
{
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
    private bool _started;

    public HeyCyanCameraProvider(
        IHeyCyanGlassesSession session,
        IHeyCyanMediaTransfer transfer,
        ILogger<HeyCyanCameraProvider> log)
    {
        _session = session;
        _transfer = transfer;
        _log = log;

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

            // 1. Snapshot the current photo count so we can detect the new file
            var beforeCount = _session.LastMediaCount?.Photos ?? 0;
            List<string>? beforeNames = null;
            try
            {
                var entries = await _transfer.ListAsync(ct).ConfigureAwait(false);
                beforeNames = entries.Select(e => e.Name).ToList();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to list media before capture (will rely on count notify)");
            }

            // 2. Trigger BLE photo capture
            _log.LogInformation("HeyCyan: triggering photo capture (current count={Count})", beforeCount);
            await _session.TakePhotoAsync(ct).ConfigureAwait(false);

            // 3. Wait for MediaCountUpdated event or timeout after 6s
            var newName = await WaitForNewPhotoAsync(beforeCount, beforeNames, TimeSpan.FromSeconds(6), ct)
                .ConfigureAwait(false);

            // 4. Fallback if the count notify was missed: pick the newest .jpg
            if (newName is null)
            {
                _log.LogWarning("HeyCyan: media-count notify timed out, falling back to newest entry");
                var entries = await _transfer.ListAsync(ct).ConfigureAwait(false);
                newName = entries
                    .Where(e => e.Kind == HeyCyanMediaKind.Photo)
                    .OrderByDescending(e => e.Timestamp)
                    .FirstOrDefault()?.Name;

                if (newName is null)
                {
                    _log.LogError("No photo found on glasses after capture");
                    return null;
                }
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

    /// <summary>
    /// Wait for MediaCountUpdated event indicating a new photo exists.
    /// </summary>
    private async Task<string?> WaitForNewPhotoAsync(
        int beforeCount,
        List<string>? beforeNames,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnMediaCountUpdated(object? sender, HeyCyanMediaCount count)
        {
            if (count.Photos > beforeCount)
            {
                _log.LogInformation("HeyCyan: media count updated to {Photos}", count.Photos);
                // We know a new photo exists, but we don't know the name yet
                // Signal success and we'll list entries to find it
                tcs.TrySetResult(null);
            }
        }

        _session.MediaCountUpdated += OnMediaCountUpdated;
        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            linkedCts.Token.Register(() => tcs.TrySetCanceled(linkedCts.Token));

            await tcs.Task.ConfigureAwait(false);

            // Now try to find the new photo by listing again
            try
            {
                var entries = await _transfer.ListAsync(ct).ConfigureAwait(false);
                var newEntry = entries
                    .Where(e => e.Kind == HeyCyanMediaKind.Photo)
                    .Where(e => beforeNames is null || !beforeNames.Contains(e.Name))
                    .OrderByDescending(e => e.Timestamp)
                    .FirstOrDefault();
                
                return newEntry?.Name;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to list entries after media count notify");
                return null;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout — return null to trigger fallback
            return null;
        }
        finally
        {
            _session.MediaCountUpdated -= OnMediaCountUpdated;
        }
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
