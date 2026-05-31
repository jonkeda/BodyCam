namespace BodyCam.Services.Camera.Usb;

public interface IUsbCameraClient
{
    bool IsSupported { get; }

    Task<IReadOnlyList<UsbCameraDeviceInfo>> EnumerateAsync(CancellationToken ct = default);

    Task<UsbCameraCaptureResult> CaptureJpegAsync(
        UsbCameraCaptureOptions options,
        CancellationToken ct = default);
}

public sealed record UsbCameraCaptureOptions(string DeviceMatch);

public sealed record UsbCameraDeviceInfo(
    string Id,
    string Name,
    bool IsEnabled,
    IReadOnlyList<string> Formats);

public sealed record UsbCameraCaptureResult(
    bool Success,
    byte[]? JpegBytes,
    string? DeviceId,
    string? DeviceName,
    string? Error)
{
    public static UsbCameraCaptureResult Failed(string error) =>
        new(false, null, null, null, error);
}

