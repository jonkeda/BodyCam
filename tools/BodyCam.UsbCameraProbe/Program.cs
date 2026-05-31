using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace BodyCam.UsbCameraProbe;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var command = args.Length == 0 ? "enumerate" : args[0].Trim().ToLowerInvariant();
        var options = ParseOptions(args.Skip(1));

        try
        {
            return command switch
            {
                "enumerate" => await EnumerateAsync(),
                "capture" => await CaptureAsync(options),
                _ => Usage($"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static async Task<int> EnumerateAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        Console.WriteLine($"VideoCapture devices: {devices.Count}");

        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            Console.WriteLine();
            Console.WriteLine($"[{i}] {device.Name}");
            Console.WriteLine($"    Id: {device.Id}");
            Console.WriteLine($"    Enabled: {device.IsEnabled}");
            Console.WriteLine($"    Enclosure: {DescribeEnclosure(device.EnclosureLocation)}");

            await TryPrintMediaPropertiesAsync(device);
        }

        return devices.Count == 0 ? 1 : 0;
    }

    private static async Task<int> CaptureAsync(IReadOnlyDictionary<string, string> options)
    {
        var output = GetOption(options, "output") ??
            Path.Combine(Environment.CurrentDirectory, "usb-camera-windows-still.jpg");
        var contains = GetOption(options, "device-contains");
        var deviceId = GetOption(options, "device-id");
        var vidpid = GetOption(options, "vidpid");

        var device = await SelectDeviceAsync(deviceId, contains, vidpid);
        if (device is null)
        {
            Console.Error.WriteLine("No matching video capture device found.");
            return 1;
        }

        Console.WriteLine($"Selected: {device.Name}");
        Console.WriteLine($"Id: {device.Id}");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

        using var mediaCapture = new MediaCapture();
        await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            VideoDeviceId = device.Id,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            SharingMode = MediaCaptureSharingMode.ExclusiveControl,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu
        });

        using var stream = new InMemoryRandomAccessStream();
        await mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream);

        stream.Seek(0);
        using var input = stream.GetInputStreamAt(0);
        using var reader = new DataReader(input);
        var length = checked((uint)stream.Size);
        await reader.LoadAsync(length);

        var bytes = new byte[length];
        reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(output, bytes);

        Console.WriteLine($"Saved: {Path.GetFullPath(output)}");
        Console.WriteLine($"Bytes: {bytes.Length}");
        Console.WriteLine($"JPEG: {LooksLikeJpeg(bytes)}");
        return LooksLikeJpeg(bytes) ? 0 : 1;
    }

    private static async Task<DeviceInformation?> SelectDeviceAsync(
        string? deviceId,
        string? contains,
        string? vidpid)
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            return devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(vidpid))
        {
            return devices.FirstOrDefault(d => d.Id.Contains(vidpid, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(contains))
        {
            return devices.FirstOrDefault(d =>
                d.Name.Contains(contains, StringComparison.OrdinalIgnoreCase) ||
                d.Id.Contains(contains, StringComparison.OrdinalIgnoreCase));
        }

        return devices.FirstOrDefault();
    }

    private static async Task TryPrintMediaPropertiesAsync(DeviceInformation device)
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

            PrintProperties("Preview", mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview));
            PrintProperties("Photo", mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.Photo));
            PrintProperties("Record", mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoRecord));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    MediaCapture properties unavailable: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void PrintProperties(string label, IReadOnlyList<IMediaEncodingProperties> properties)
    {
        Console.WriteLine($"    {label} formats: {properties.Count}");
        foreach (var property in properties.Take(12))
        {
            Console.WriteLine($"      - {DescribeProperty(property)}");
        }

        if (properties.Count > 12)
        {
            Console.WriteLine($"      ... {properties.Count - 12} more");
        }
    }

    private static string DescribeProperty(IMediaEncodingProperties property)
    {
        if (property is VideoEncodingProperties video)
        {
            var fps = video.FrameRate.Denominator == 0
                ? "?"
                : $"{video.FrameRate.Numerator / (double)video.FrameRate.Denominator:0.##}";
            return $"{property.Subtype} {video.Width}x{video.Height} {fps}fps";
        }

        return $"{property.Type}/{property.Subtype}";
    }

    private static string DescribeEnclosure(EnclosureLocation? location)
    {
        if (location is null)
        {
            return "unknown";
        }

        return $"{location.Panel}, rotation={location.RotationAngleInDegreesClockwise}";
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? pending = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                pending = arg[2..];
                result[pending] = "true";
                continue;
            }

            if (pending is not null)
            {
                result[pending] = arg;
                pending = null;
            }
        }

        return result;
    }

    private static string? GetOption(IReadOnlyDictionary<string, string> options, string name)
    {
        return options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static bool LooksLikeJpeg(byte[] bytes)
    {
        return bytes.Length >= 4 &&
               bytes[0] == 0xFF &&
               bytes[1] == 0xD8 &&
               bytes[^2] == 0xFF &&
               bytes[^1] == 0xD9;
    }

    private static int Usage(string? error = null)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
        }

        Console.WriteLine("""
Usage:
  BodyCam.UsbCameraProbe enumerate
  BodyCam.UsbCameraProbe capture --vidpid VID_349C&PID_0411 --output <path>
  BodyCam.UsbCameraProbe capture --device-contains "HD camera" --output <path>
""");
        return string.IsNullOrWhiteSpace(error) ? 0 : 1;
    }
}
