#if ANDROID
using Android.Content;
using Android.Provider;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using AndroidUri = Android.Net.Uri;

namespace BodyCam.Platforms.Android.HeyCyan;

/// <summary>
/// Android MediaStore implementation for saving media files to the system library.
/// Uses ContentResolver.Insert with MediaStore.Images/Video/Audio URIs.
/// </summary>
internal sealed class AndroidMediaStore : Services.Glasses.HeyCyan.Media.IMediaStore
{
    private readonly ILogger<AndroidMediaStore> _log;
    private readonly ContentResolver _resolver;

    public AndroidMediaStore(ILogger<AndroidMediaStore> log)
    {
        _log = log;

        var context = Platform.CurrentActivity ?? Platform.AppContext;
        if (context is null)
            throw new InvalidOperationException("Cannot get Android context — MAUI Platform not initialized");

        _resolver = context.ContentResolver
            ?? throw new InvalidOperationException("ContentResolver not available");
    }

    public async Task<string> SaveImageAsync(string fileName, Stream content, CancellationToken ct)
    {
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "image/jpeg");
        values.Put(MediaStore.IMediaColumns.RelativePath, "DCIM/BodyCam");

        var uri = _resolver.Insert(MediaStore.Images.Media.ExternalContentUri!, values);
        if (uri is null)
            throw new InvalidOperationException("Failed to insert image into MediaStore");

        await using var os = _resolver.OpenOutputStream(uri);
        if (os is null)
            throw new InvalidOperationException($"Failed to open output stream for {uri}");

        await content.CopyToAsync(os, ct).ConfigureAwait(false);

        _log.LogInformation("Saved image {FileName} to {Uri}", fileName, uri);
        return uri.ToString()!;
    }

    public async Task<string> SaveVideoAsync(string fileName, Stream content, CancellationToken ct)
    {
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "video/mp4");
        values.Put(MediaStore.IMediaColumns.RelativePath, "DCIM/BodyCam");

        var uri = _resolver.Insert(MediaStore.Video.Media.ExternalContentUri!, values);
        if (uri is null)
            throw new InvalidOperationException("Failed to insert video into MediaStore");

        await using var os = _resolver.OpenOutputStream(uri);
        if (os is null)
            throw new InvalidOperationException($"Failed to open output stream for {uri}");

        await content.CopyToAsync(os, ct).ConfigureAwait(false);

        _log.LogInformation("Saved video {FileName} to {Uri}", fileName, uri);
        return uri.ToString()!;
    }

    public async Task<string> SaveAudioAsync(
        string fileName,
        string mimeType,
        Stream content,
        CancellationToken ct)
    {
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, mimeType);
        values.Put(MediaStore.IMediaColumns.RelativePath, "Music/BodyCam");

        var uri = _resolver.Insert(MediaStore.Audio.Media.ExternalContentUri!, values);
        if (uri is null)
            throw new InvalidOperationException("Failed to insert audio into MediaStore");

        await using var os = _resolver.OpenOutputStream(uri);
        if (os is null)
            throw new InvalidOperationException($"Failed to open output stream for {uri}");

        await content.CopyToAsync(os, ct).ConfigureAwait(false);

        _log.LogInformation("Saved audio {FileName} to {Uri}", fileName, uri);
        return uri.ToString()!;
    }

    public Task<bool> ExistsAsync(string fileName, Services.Glasses.HeyCyan.Media.RecordedMediaKind kind, CancellationToken ct)
    {
        // For audio, check .ogg variant (since we rename .opus to .ogg)
        var checkName = fileName;
        if (kind == Services.Glasses.HeyCyan.Media.RecordedMediaKind.Audio &&
            Path.GetExtension(fileName) == ".opus")
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            checkName = $"{baseName}.ogg";
        }

        AndroidUri? collectionUri = kind switch
        {
            Services.Glasses.HeyCyan.Media.RecordedMediaKind.Photo => MediaStore.Images.Media.ExternalContentUri,
            Services.Glasses.HeyCyan.Media.RecordedMediaKind.Video => MediaStore.Video.Media.ExternalContentUri,
            Services.Glasses.HeyCyan.Media.RecordedMediaKind.Audio => MediaStore.Audio.Media.ExternalContentUri,
            _ => null
        };

        if (collectionUri is null)
            return Task.FromResult(false);

        // Query for DisplayName match
        var projection = new[] { MediaStore.IMediaColumns.DisplayName };
        var selection = $"{MediaStore.IMediaColumns.DisplayName} = ?";
        var selectionArgs = new string[] { checkName };

        using var cursor = _resolver.Query(collectionUri, projection, selection, selectionArgs, null);
        var exists = cursor?.MoveToFirst() == true;

        return Task.FromResult(exists);
    }
}
#endif
