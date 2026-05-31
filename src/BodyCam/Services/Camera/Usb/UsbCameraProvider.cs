using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Camera.Usb;

public sealed class UsbCameraProvider : ICameraProvider
{
    public const string Id = "usb-camera";
    public const string DefaultDeviceMatch = "VID_349C&PID_0411";

    private readonly ISettingsService _settings;
    private readonly IUsbCameraClient _client;
    private readonly ILogger<UsbCameraProvider> _log;

    private bool _started;
    private bool _available;
    private byte[]? _latestFrame;

    public UsbCameraProvider(
        ISettingsService settings,
        IUsbCameraClient client,
        ILogger<UsbCameraProvider> log)
    {
        _settings = settings;
        _client = client;
        _log = log;
    }

    public string DisplayName => "USB Camera";

    public string ProviderId => Id;

    public bool IsAvailable => _started && _available;

    public bool SupportsVideoRecording => true;

    public event EventHandler? Disconnected;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _started = true;
        _available = false;

        if (!_client.IsSupported)
        {
            _log.LogWarning("USB Camera: capture client is not supported on this platform");
            _started = false;
            return;
        }

        var match = ConfiguredDeviceMatch;
        var devices = await _client.EnumerateAsync(ct).ConfigureAwait(false);
        _available = devices.Any(device => Matches(device, match));

        if (!_available)
        {
            _log.LogWarning("USB Camera: no device matched {DeviceMatch}", match);
            _started = false;
        }
    }

    public Task StopAsync()
    {
        _started = false;
        _available = false;
        _latestFrame = null;
        return Task.CompletedTask;
    }

    public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (!_started)
            await StartAsync(ct).ConfigureAwait(false);

        if (!_client.IsSupported)
            return null;

        var match = ConfiguredDeviceMatch;
        var result = await _client.CaptureJpegAsync(
                new UsbCameraCaptureOptions(match),
                ct)
            .ConfigureAwait(false);

        if (!result.Success || result.JpegBytes is null)
        {
            _log.LogWarning("USB Camera: capture failed. Error={Error}", result.Error);
            if (_available)
                Disconnected?.Invoke(this, EventArgs.Empty);

            _available = false;
            return null;
        }

        if (!LooksLikeJpeg(result.JpegBytes))
        {
            _log.LogWarning("USB Camera: capture returned non-JPEG bytes from {DeviceName}", result.DeviceName);
            return null;
        }

        _started = true;
        _available = true;
        _latestFrame = result.JpegBytes;
        return result.JpegBytes;
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await CaptureFrameAsync(ct).ConfigureAwait(false);
            if (frame is not null)
                yield return frame;

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        _latestFrame = null;
        _started = false;
        _available = false;
        return ValueTask.CompletedTask;
    }

    private string ConfiguredDeviceMatch =>
        string.IsNullOrWhiteSpace(_settings.UsbCameraDeviceMatch)
            ? DefaultDeviceMatch
            : _settings.UsbCameraDeviceMatch.Trim();

    private static bool Matches(UsbCameraDeviceInfo device, string match)
    {
        return string.IsNullOrWhiteSpace(match)
               || device.Id.Contains(match, StringComparison.OrdinalIgnoreCase)
               || device.Name.Contains(match, StringComparison.OrdinalIgnoreCase);
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

