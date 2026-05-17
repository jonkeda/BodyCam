#if IOS
using Foundation;
using Microsoft.Extensions.Logging;
using Photos;

namespace BodyCam.Platforms.iOS.HeyCyan;

/// <summary>
/// iOS Photos implementation for saving media files to the system library.
/// Uses PHPhotoLibrary for images/videos, NSFileManager for audio.
/// </summary>
internal sealed class IosMediaStore : Services.Glasses.HeyCyan.Media.IMediaStore
{
    private readonly ILogger<IosMediaStore> _log;
    private readonly string _audioRoot;

    public IosMediaStore(ILogger<IosMediaStore> log)
    {
        _log = log;

        // Audio is not a Photos asset type — write to Documents/BodyCam/Audio
        var docs = NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User)[0];
        _audioRoot = Path.Combine(docs.Path!, "BodyCam", "Audio");
        Directory.CreateDirectory(_audioRoot);
    }

    public async Task<string> SaveImageAsync(string fileName, Stream content, CancellationToken ct)
    {
        // Write to temp file first (PHPhotoLibrary needs a file URL for images)
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        await using (var fs = File.Create(tempPath))
        {
            await content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }

        var tcs = new TaskCompletionSource<string>();

        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(() =>
        {
            var request = PHAssetCreationRequest.CreationRequestForAssetFromImage(NSUrl.FromFilename(tempPath));
            // Request.PlaceholderForCreatedAsset?.LocalIdentifier is available after commit
        }, (success, error) =>
        {
            try
            {
                File.Delete(tempPath);
            }
            catch { }

            if (success)
            {
                // Return a file:// URI for consistency (Photos doesn't expose content:// like Android)
                var uri = $"file://{tempPath}";
                _log.LogInformation("Saved image {FileName} to Photos", fileName);
                tcs.SetResult(uri);
            }
            else
            {
                _log.LogError("Failed to save image {FileName}: {Error}", fileName, error?.LocalizedDescription);
                tcs.SetException(new InvalidOperationException($"Failed to save image: {error?.LocalizedDescription}"));
            }
        });

        return await tcs.Task;
    }

    public async Task<string> SaveVideoAsync(string fileName, Stream content, CancellationToken ct)
    {
        // Write to temp file first (PHPhotoLibrary needs a file URL for videos)
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        await using (var fs = File.Create(tempPath))
        {
            await content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }

        var tcs = new TaskCompletionSource<string>();

        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(() =>
        {
            var request = PHAssetCreationRequest.CreationRequestForAssetFromVideo(NSUrl.FromFilename(tempPath));
            // Request.PlaceholderForCreatedAsset?.LocalIdentifier is available after commit
        }, (success, error) =>
        {
            try
            {
                File.Delete(tempPath);
            }
            catch { }

            if (success)
            {
                var uri = $"file://{tempPath}";
                _log.LogInformation("Saved video {FileName} to Photos", fileName);
                tcs.SetResult(uri);
            }
            else
            {
                _log.LogError("Failed to save video {FileName}: {Error}", fileName, error?.LocalizedDescription);
                tcs.SetException(new InvalidOperationException($"Failed to save video: {error?.LocalizedDescription}"));
            }
        });

        return await tcs.Task;
    }

    public async Task<string> SaveAudioAsync(
        string fileName,
        string mimeType,
        Stream content,
        CancellationToken ct)
    {
        // Audio is not a Photos asset type — write to Documents/BodyCam/Audio
        var path = Path.Combine(_audioRoot, fileName);

        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct).ConfigureAwait(false);

        _log.LogInformation("Saved audio {FileName} to {Path}", fileName, path);
        return new Uri(path).AbsoluteUri;
    }

    public Task<bool> ExistsAsync(
        string fileName,
        Services.Glasses.HeyCyan.Media.RecordedMediaKind kind,
        CancellationToken ct)
    {
        // For audio, check .ogg variant (since we rename .opus to .ogg)
        var checkName = fileName;
        if (kind == Services.Glasses.HeyCyan.Media.RecordedMediaKind.Audio &&
            Path.GetExtension(fileName) == ".opus")
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            checkName = $"{baseName}.ogg";
        }

        if (kind == Services.Glasses.HeyCyan.Media.RecordedMediaKind.Audio)
        {
            // Audio is in file system
            var path = Path.Combine(_audioRoot, checkName);
            return Task.FromResult(File.Exists(path));
        }

        // Photos/Videos: query PHPhotoLibrary by filename is complex (requires fetching all assets).
        // For simplicity, return false — re-imports will be idempotent at the file level anyway.
        // A production implementation could query PHAsset.FetchAssets with a predicate on
        // PHAssetResource.OriginalFilename, but that's overkill for this phase.
        _log.LogDebug("ExistsAsync for Photos assets is not implemented — assuming not exists");
        return Task.FromResult(false);
    }
}
#endif
