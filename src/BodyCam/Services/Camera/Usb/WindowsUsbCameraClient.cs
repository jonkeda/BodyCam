#if WINDOWS
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace BodyCam.Services.Camera.Usb;

public sealed class WindowsUsbCameraClient : IUsbCameraClient
{
    public bool IsSupported => true;

    public async Task<IReadOnlyList<UsbCameraDeviceInfo>> EnumerateAsync(CancellationToken ct = default)
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        var result = new List<UsbCameraDeviceInfo>(devices.Count);

        foreach (var device in devices)
        {
            ct.ThrowIfCancellationRequested();
            var formats = await TryGetFormatsAsync(device, ct).ConfigureAwait(false);
            result.Add(new UsbCameraDeviceInfo(
                device.Id,
                device.Name,
                device.IsEnabled,
                formats));
        }

        return result;
    }

    public async Task<UsbCameraCaptureResult> CaptureJpegAsync(
        UsbCameraCaptureOptions options,
        CancellationToken ct = default)
    {
        var device = await SelectDeviceAsync(options.DeviceMatch, ct).ConfigureAwait(false);
        if (device is null)
            return UsbCameraCaptureResult.Failed($"No USB camera matched '{options.DeviceMatch}'.");

        try
        {
            using var mediaCapture = new MediaCapture();
            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = device.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            });

            using var stream = new InMemoryRandomAccessStream();
            await mediaCapture.CapturePhotoToStreamAsync(
                ImageEncodingProperties.CreateJpeg(),
                stream);

            ct.ThrowIfCancellationRequested();
            stream.Seek(0);

            using var input = stream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            var length = checked((uint)stream.Size);
            await reader.LoadAsync(length);

            var bytes = new byte[length];
            reader.ReadBytes(bytes);

            return new UsbCameraCaptureResult(
                true,
                bytes,
                device.Id,
                device.Name,
                null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UsbCameraCaptureResult(
                false,
                null,
                device.Id,
                device.Name,
                ex.Message);
        }
    }

    private static async Task<DeviceInformation?> SelectDeviceAsync(
        string deviceMatch,
        CancellationToken ct)
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(deviceMatch))
            return devices.FirstOrDefault();

        return devices.FirstOrDefault(device =>
            device.Id.Contains(deviceMatch, StringComparison.OrdinalIgnoreCase)
            || device.Name.Contains(deviceMatch, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<IReadOnlyList<string>> TryGetFormatsAsync(
        DeviceInformation device,
        CancellationToken ct)
    {
        try
        {
            using var mediaCapture = new MediaCapture();
            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = device.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            });

            ct.ThrowIfCancellationRequested();

            return mediaCapture.VideoDeviceController
                .GetAvailableMediaStreamProperties(MediaStreamType.Photo)
                .OfType<VideoEncodingProperties>()
                .Select(Describe)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string Describe(VideoEncodingProperties video)
    {
        var fps = video.FrameRate.Denominator == 0
            ? "?"
            : $"{video.FrameRate.Numerator / (double)video.FrameRate.Denominator:0.##}";

        return $"{video.Subtype} {video.Width}x{video.Height} {fps}fps";
    }
}
#endif

