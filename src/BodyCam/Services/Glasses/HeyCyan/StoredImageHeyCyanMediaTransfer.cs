using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Development/test fallback for the HeyCyan media-download step.
/// It never enters WiFi transfer mode; callers should still trigger capture
/// through the real glasses session before asking this service for bytes.
/// </summary>
public sealed class StoredImageHeyCyanMediaTransfer : IHeyCyanStoredImageMediaTransfer
{
    public const string DefaultFallbackFileName = "stored-heycyan-fallback.jpg";
    private const string PackagedFallbackAssetName = "heycyan_fallback_photo.b64";

    private readonly ILogger<StoredImageHeyCyanMediaTransfer> _log;
    private readonly string? _fallbackDirectory;
    private readonly byte[]? _seedImageBytes;

    public StoredImageHeyCyanMediaTransfer(
        ILogger<StoredImageHeyCyanMediaTransfer> log)
        : this(log, fallbackDirectory: null, seedImageBytes: null)
    {
    }

    internal StoredImageHeyCyanMediaTransfer(
        ILogger<StoredImageHeyCyanMediaTransfer> log,
        string? fallbackDirectory,
        byte[]? seedImageBytes)
    {
        _log = log;
        _fallbackDirectory = fallbackDirectory;
        _seedImageBytes = seedImageBytes;
    }

    public bool IsWarm => false;

    public string FallbackFileName => DefaultFallbackFileName;

    public async Task<IReadOnlyList<HeyCyanMediaEntry>> ListAsync(CancellationToken ct)
    {
        var bytes = await ReadFallbackImageAsync(ct).ConfigureAwait(false);
        return new[]
        {
            new HeyCyanMediaEntry(
                FallbackFileName,
                bytes.LongLength,
                DateTimeOffset.UtcNow,
                HeyCyanMediaKind.Photo)
        };
    }

    public async Task<byte[]> DownloadAsync(string fileName, CancellationToken ct)
    {
        var bytes = await ReadFallbackImageAsync(ct).ConfigureAwait(false);
        _log.LogWarning(
            "HeyCyan stored-image download fallback returned {Size} bytes for {FileName} from {Source}",
            bytes.Length,
            fileName,
            DescribeSource());
        return bytes;
    }

    public async Task<Stream> OpenAsync(string fileName, CancellationToken ct)
    {
        var bytes = await DownloadAsync(fileName, ct).ConfigureAwait(false);
        return new MemoryStream(bytes, writable: false);
    }

    public Task ExitAsync(CancellationToken ct) => Task.CompletedTask;

    public ValueTask DisposeAsync() => default;

    private async Task<byte[]> ReadFallbackImageAsync(CancellationToken ct)
    {
        var path = await EnsureFallbackImageAsync(ct).ConfigureAwait(false);
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        AssertJpeg(bytes);
        return bytes;
    }

    private async Task<string> EnsureFallbackImageAsync(CancellationToken ct)
    {
        var directory = ResolveFallbackDirectory();
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, DefaultFallbackFileName);
        if (!File.Exists(path))
        {
            var seedBytes = _seedImageBytes
                ?? await ReadPackagedFallbackImageAsync(ct).ConfigureAwait(false);
            AssertJpeg(seedBytes);

            await File.WriteAllBytesAsync(path, seedBytes, ct).ConfigureAwait(false);
            _log.LogInformation("Created HeyCyan stored-image fallback at {Path}", path);
        }

        return path;
    }

    private static async Task<byte[]> ReadPackagedFallbackImageAsync(CancellationToken ct)
    {
        await using var stream = await FileSystem
            .OpenAppPackageFileAsync(PackagedFallbackAssetName)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var base64 = await reader.ReadToEndAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var compact = new string(base64.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return Convert.FromBase64String(compact);
    }

    private string DescribeSource() => Path.Combine(ResolveFallbackDirectory(), DefaultFallbackFileName);

    private string ResolveFallbackDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_fallbackDirectory))
        {
            return _fallbackDirectory;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(FileSystem.AppDataDirectory))
            {
                return Path.Combine(FileSystem.AppDataDirectory, "HeyCyanFallback");
            }
        }
        catch
        {
            // FileSystem can be unavailable in some test or early startup contexts.
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            string.IsNullOrWhiteSpace(localAppData) ? AppContext.BaseDirectory : localAppData,
            "BodyCam",
            "HeyCyanFallback");
    }

    private static void AssertJpeg(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            var hex = bytes.Length >= 2 ? $"{bytes[0]:X2}{bytes[1]:X2}" : "empty";
            throw new InvalidDataException(
                $"HeyCyan fallback image is not a JPEG (len={bytes.Length}, first2={hex}).");
        }
    }
}
