using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan.Media;

/// <summary>
/// Fallback media store that writes to FileSystem.AppDataDirectory.
/// Used on Windows and in tests where platform libraries are unavailable.
/// </summary>
internal sealed class NoopMediaStore : IMediaStore
{
    private readonly ILogger<NoopMediaStore> _log;
    private readonly string _rootPath;

    public NoopMediaStore(ILogger<NoopMediaStore> log)
    {
        _log = log;
        _rootPath = Path.Combine(FileSystem.Current.AppDataDirectory, "RecordedMedia");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveImageAsync(string fileName, Stream content, CancellationToken ct)
    {
        var dir = Path.Combine(_rootPath, "Photo");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);

        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct).ConfigureAwait(false);

        _log.LogInformation("Saved image to {Path}", path);
        return new Uri(path).AbsoluteUri;
    }

    public async Task<string> SaveVideoAsync(string fileName, Stream content, CancellationToken ct)
    {
        var dir = Path.Combine(_rootPath, "Video");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);

        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct).ConfigureAwait(false);

        _log.LogInformation("Saved video to {Path}", path);
        return new Uri(path).AbsoluteUri;
    }

    public async Task<string> SaveAudioAsync(
        string fileName,
        string mimeType,
        Stream content,
        CancellationToken ct)
    {
        var dir = Path.Combine(_rootPath, "Audio");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);

        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct).ConfigureAwait(false);

        _log.LogInformation("Saved audio to {Path}", path);
        return new Uri(path).AbsoluteUri;
    }

    public Task<bool> ExistsAsync(string fileName, RecordedMediaKind kind, CancellationToken ct)
    {
        var dir = Path.Combine(_rootPath, kind.ToString());
        var path = Path.Combine(dir, fileName);

        // For audio, also check .ogg variant (since we rename .opus to .ogg)
        if (kind == RecordedMediaKind.Audio && Path.GetExtension(fileName) == ".opus")
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var oggPath = Path.Combine(dir, $"{baseName}.ogg");
            return Task.FromResult(File.Exists(oggPath));
        }

        return Task.FromResult(File.Exists(path));
    }
}
