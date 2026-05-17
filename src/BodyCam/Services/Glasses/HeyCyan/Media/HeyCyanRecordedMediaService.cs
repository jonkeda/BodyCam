using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;

namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Bulk-import recorded media files from HeyCyan glasses.
/// Uses HeyCyanMediaTransfer for HTTP download, OpusOggWrapper for audio normalization,
/// and IMediaStore for platform-specific library writes.
/// Writes .bodycam.json sidecars for video and audio files.
/// </summary>
internal sealed class HeyCyanRecordedMediaService : IHeyCyanRecordedMediaService
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly IHeyCyanMediaTransfer _transfer;
    private readonly IMediaStore _store;
    private readonly ISidecarWriter _sidecarWriter;
    private readonly ILogger<HeyCyanRecordedMediaService> _log;

    public event EventHandler<ImportedMediaItem>? AudioImported;

    public HeyCyanRecordedMediaService(
        IHeyCyanGlassesSession session,
        IHeyCyanMediaTransfer transfer,
        IMediaStore store,
        ISidecarWriter sidecarWriter,
        ILogger<HeyCyanRecordedMediaService> log)
    {
        _session = session;
        _transfer = transfer;
        _store = store;
        _sidecarWriter = sidecarWriter;
        _log = log;
    }

    public async IAsyncEnumerable<RecordedMediaItem> EnumerateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Enter transfer mode if not already active
        var wasWarm = _transfer.IsWarm;
        if (!wasWarm)
        {
            _log.LogInformation("EnumerateAsync: entering transfer mode");
        }

        try
        {
            var entries = await _transfer.ListAsync(ct).ConfigureAwait(false);
            _log.LogInformation("Enumerating {Count} recorded media files", entries.Count);

            foreach (var entry in entries)
            {
                var kind = RecordedMediaClassifier.Classify(entry.Name);
                yield return new RecordedMediaItem(
                    entry.Name,
                    kind,
                    entry.Size,
                    entry.Timestamp);
            }
        }
        finally
        {
            // Exit transfer mode if we entered it (cold start)
            if (!wasWarm)
            {
                _log.LogInformation("EnumerateAsync: exiting transfer mode (cold session)");
                await _transfer.ExitAsync(ct).ConfigureAwait(false);
            }
        }
    }

    public async IAsyncEnumerable<ImportedMediaItem> ImportAllAsync(
        IProgress<RecordedMediaImportProgress>? progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Materialize the enumeration so we know Total for progress
        var items = new List<RecordedMediaItem>();
        await foreach (var item in EnumerateAsync(ct).ConfigureAwait(false))
        {
            items.Add(item);
        }

        _log.LogInformation("ImportAllAsync: importing {Count} files", items.Count);

        var completed = 0;
        long bytesSoFar = 0;

        foreach (var item in items)
        {
            // Skip Unknown kind silently
            if (item.Kind == RecordedMediaKind.Unknown)
            {
                _log.LogDebug("Skipping unknown file type: {FileName}", item.FileName);
                continue;
            }

            // Report progress before each download
            progress?.Report(new RecordedMediaImportProgress(
                completed,
                items.Count,
                item.FileName,
                bytesSoFar));

            ImportedMediaItem imported;
            try
            {
                imported = await ImportAsync(item, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to import {FileName}", item.FileName);
                throw;
            }

            bytesSoFar += imported.BytesWritten;
            completed++;

            yield return imported;
        }

        _log.LogInformation("ImportAllAsync: completed {Count} imports, {Bytes} bytes total",
            completed, bytesSoFar);
    }

    public async Task<ImportedMediaItem> ImportAsync(
        RecordedMediaItem item,
        CancellationToken ct)
    {
        // Reject Unknown kind
        if (item.Kind == RecordedMediaKind.Unknown)
        {
            throw new NotSupportedException($"Cannot import file of unknown type: {item.FileName}");
        }

        // Skip if already exists (idempotent re-imports)
        if (await _store.ExistsAsync(item.FileName, item.Kind, ct).ConfigureAwait(false))
        {
            _log.LogInformation("Skipping already-imported file: {FileName}", item.FileName);
            // Return a sentinel with empty URI
            return new ImportedMediaItem(item, string.Empty, 0, TimeSpan.Zero);
        }

        var sw = Stopwatch.StartNew();
        long bytesWritten;
        string localUri;
        string? sha256 = null;

        switch (item.Kind)
        {
            case RecordedMediaKind.Photo:
                // Photos do not get sidecars (EXIF is sufficient)
                await using (var src = await _transfer.OpenAsync(item.FileName, ct).ConfigureAwait(false))
                {
                    localUri = await _store.SaveImageAsync(item.FileName, src, ct).ConfigureAwait(false);
                    bytesWritten = src.CanSeek ? src.Position : item.SizeBytes ?? 0;
                }
                break;

            case RecordedMediaKind.Video:
                await using (var src = await _transfer.OpenAsync(item.FileName, ct).ConfigureAwait(false))
                {
                    var hashingSrc = new HashingStream(src);
                    localUri = await _store.SaveVideoAsync(item.FileName, hashingSrc, ct).ConfigureAwait(false);
                    bytesWritten = hashingSrc.CanSeek ? hashingSrc.Position : item.SizeBytes ?? 0;
                    await hashingSrc.DisposeAsync().ConfigureAwait(false);
                    sha256 = hashingSrc.GetHashHex();
                }
                break;

            case RecordedMediaKind.Audio:
                // Buffer the .opus file (typically < 2 MB) and compute SHA-256 of the raw bytes
                byte[] opusBytes;
                await using (var src = await _transfer.OpenAsync(item.FileName, ct).ConfigureAwait(false))
                {
                    using var ms = new MemoryStream();
                    await src.CopyToAsync(ms, ct).ConfigureAwait(false);
                    opusBytes = ms.ToArray();
                }

                // Compute SHA-256 of the raw OPUS bytes
                sha256 = Convert.ToHexString(SHA256.HashData(opusBytes)).ToLowerInvariant();

                // Wrap to Ogg/Opus container
                var oggBytes = OpusOggWrapper.AutoWrap(opusBytes);
                bytesWritten = oggBytes.Length;

                // Rename to .ogg extension
                var baseName = Path.GetFileNameWithoutExtension(item.FileName);
                var oggName = $"{baseName}.ogg";

                // Save to audio library
                await using (var oggStream = new MemoryStream(oggBytes))
                {
                    localUri = await _store.SaveAudioAsync(oggName, "audio/ogg", oggStream, ct)
                        .ConfigureAwait(false);
                }
                break;

            default:
                throw new NotSupportedException($"Unsupported media kind: {item.Kind}");
        }

        sw.Stop();
        _log.LogInformation("Imported {FileName} ({Bytes} bytes) in {Elapsed:0.00}s to {Uri}",
            item.FileName, bytesWritten, sw.Elapsed.TotalSeconds, localUri);

        // Write sidecar for Video and Audio
        if (sha256 is not null && (item.Kind == RecordedMediaKind.Video || item.Kind == RecordedMediaKind.Audio))
        {
            try
            {
                var sidecar = new RecordedMediaSidecar(
                    Schema: 1,
                    SourceFileName: item.FileName,
                    GlassesMacAddress: _session.Device?.Address ?? "unknown",
                    ImportedAt: DateTimeOffset.UtcNow,
                    GlassesTimestamp: item.GlassesTimestamp,
                    Duration: null, // Will be probed by JsonSidecarWriter
                    SizeBytes: bytesWritten,
                    Sha256: sha256);

                await _sidecarWriter.WriteAsync(localUri, sidecar, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to write sidecar for {FileName}, continuing", item.FileName);
            }
        }

        var imported = new ImportedMediaItem(item, localUri, bytesWritten, sw.Elapsed, sha256);

        // Fire AudioImported event for M16 dictation hook
        if (item.Kind == RecordedMediaKind.Audio && sha256 is not null)
        {
            AudioImported?.Invoke(this, imported);
        }

        return imported;
    }

    public Task<bool> DeleteRemoteAsync(string fileName, CancellationToken ct)
    {
        // Delete-after-import is not yet documented in the SDK API reference.
        // Return false and log a message.
        _log.LogDebug("DeleteRemoteAsync: delete-after-import not supported on this firmware");
        return Task.FromResult(false);
    }
}
