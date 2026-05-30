using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Camera.A9.Vue990;

/// <summary>
/// BodyCam camera provider for Vue990/BK7252N cameras using the managed-direct
/// C# transport proven for @MC-0025644.
/// </summary>
public sealed class Vue990CameraProvider : ICameraProvider
{
    public const string Id = "vue990-camera";
    public const string DefaultHost = "192.168.168.1";

    private readonly ISettingsService _settings;
    private readonly IA9Vue990DirectCaptureClient _captureClient;
    private readonly ILogger<Vue990CameraProvider> _log;

    private byte[]? _latestFrame;
    private bool _started;

    public Vue990CameraProvider(
        ISettingsService settings,
        IA9Vue990DirectCaptureClient captureClient,
        ILogger<Vue990CameraProvider> log)
    {
        _settings = settings;
        _captureClient = captureClient;
        _log = log;
    }

    public string DisplayName => "Vue990 Camera";

    public string ProviderId => Id;

    public bool IsAvailable => _started && !string.IsNullOrWhiteSpace(ConfiguredHost);

    public bool SupportsVideoRecording => true;

    public event EventHandler? Disconnected;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ConfiguredHost))
        {
            _log.LogWarning("Vue990: no camera host configured; cannot start provider");
            _started = false;
            return Task.CompletedTask;
        }

        _started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _started = false;
        _latestFrame = null;
        return Task.CompletedTask;
    }

    public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (!_started)
            await StartAsync(ct).ConfigureAwait(false);

        var host = ConfiguredHost;
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var outputDirectory = BuildCaptureDirectory();
        A9Vue990DirectCaptureResult result;
        try
        {
            result = await _captureClient.CaptureAsync(
                    new A9Vue990DirectCaptureOptions
                    {
                        Host = host,
                        OutputDirectory = outputDirectory,
                        CaptureImage = true,
                        CaptureVideo = false,
                        StreamSeconds = 18,
                        MaxFrames = 1,
                    },
                    line => _log.LogDebug("Vue990: {Line}", line),
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Vue990: direct capture failed");
            Disconnected?.Invoke(this, EventArgs.Empty);
            return null;
        }

        if (!result.Success || !result.CapturedImage)
        {
            _log.LogWarning("Vue990: capture did not return an image. Error={Error}", result.Error);
            return null;
        }

        var stillArtifact = result.Artifacts.FirstOrDefault(artifact =>
            string.Equals(
                Path.GetFileName(artifact.LocalPath),
                "managed-direct-still.jpg",
                StringComparison.OrdinalIgnoreCase));

        if (stillArtifact is null || !File.Exists(stillArtifact.LocalPath))
        {
            _log.LogWarning("Vue990: capture result did not include managed-direct-still.jpg");
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(stillArtifact.LocalPath, ct).ConfigureAwait(false);
        if (!LooksLikeJpeg(bytes))
        {
            _log.LogWarning("Vue990: captured still did not look like JPEG bytes");
            return null;
        }

        _latestFrame = bytes;
        return bytes;
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await CaptureFrameAsync(ct).ConfigureAwait(false);
            if (frame is not null)
                yield return frame;

            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private string? ConfiguredHost
    {
        get
        {
            var host = _settings.Vue990CameraIp;
            return string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        }
    }

    private static string BuildCaptureDirectory()
    {
        var root = GetCacheRoot();
        return Path.Combine(
            root,
            "vue990-camera-provider",
            DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmmssfff"));
    }

    private static string GetCacheRoot()
    {
        try
        {
            var cache = Microsoft.Maui.Storage.FileSystem.CacheDirectory;
            if (!string.IsNullOrWhiteSpace(cache))
                return cache;
        }
        catch
        {
            // Unit tests and some headless hosts may not have MAUI storage initialized.
        }

        return Path.Combine(Path.GetTempPath(), "BodyCam");
    }

    private static bool LooksLikeJpeg(byte[] bytes)
    {
        return bytes.Length >= 4
               && bytes[0] == 0xff
               && bytes[1] == 0xd8
               && bytes[^2] == 0xff
               && bytes[^1] == 0xd9;
    }
}
