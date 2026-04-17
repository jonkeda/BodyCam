# M11 Phase 2 — USB Bodycam Provider

## Goal

Support USB clip-on bodycams and USB webcams as camera sources via standard
USB Video Class (UVC) protocol.

---

## Use Case

A body-worn USB camera clipped to clothing, a lanyard, or a hat. Connected
to the phone (USB OTG) or laptop (USB-A/C). Captures forward-facing video
like a dashcam or police bodycam.

Unlike phone cameras, USB cameras are always pointed where the user is looking
without needing to hold the phone up.

---

## Platform Support

### Windows — MediaFoundation

Windows treats UVC devices as standard capture sources via MediaFoundation.

```csharp
namespace BodyCam.Platforms.Windows.Camera;

public class UsbCameraProvider : ICameraProvider
{
    public string DisplayName { get; private set; } = "USB Camera";
    public string ProviderId => "usb";
    public bool IsAvailable => _device is not null;

    public event EventHandler? Disconnected;

    // Use MediaFoundation to enumerate and capture from UVC devices.
    // Key APIs:
    //   - MFEnumDeviceSources (enumerate USB cameras)
    //   - IMFSourceReader (read frames)
    //   - MFCreateSampleGrabberSinkWriter (grab individual frames)
    //
    // Alternative: Use OpenCvSharp (NuGet) for simpler API at cost of larger binary.
    // Alternative: Use Windows.Media.Capture.MediaCapture (UWP API available in WinUI3).
}
```

**Recommended approach: `Windows.Media.Capture.MediaCapture`**

This UWP API is available in WinUI3/MAUI and provides:
- Device enumeration via `DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)`
- Frame capture via `MediaCapture.CapturePhotoToStreamAsync()`
- No preview needed (LowLatencyPhoto capture mode)
- Built-in permission handling

```csharp
// Enumerate USB cameras (distinct from phone cameras)
var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
var usbDevices = devices.Where(d => !d.Name.Contains("Front") && !d.Name.Contains("Rear"));

// Capture without preview
var capture = new MediaCapture();
await capture.InitializeAsync(new MediaCaptureInitializationSettings
{
    VideoDeviceId = selectedDevice.Id,
    StreamingCaptureMode = StreamingCaptureMode.Video,
    PhotoCaptureSource = PhotoCaptureSource.VideoPreview
});

using var stream = new InMemoryRandomAccessStream();
await capture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream);
// Convert stream to byte[]
```

### Android — USB Host API

Android requires USB Host API + a UVC driver library since Android doesn't
natively support UVC webcams.

**Options:**

| Library | License | Notes |
|---------|---------|-------|
| **UVCCamera (saki4510t)** | Apache 2.0 | Mature, Java, requires JNI bindings |
| **libusb + libuvc** | LGPL | C libraries, requires native interop |
| **AndroidX Camera USB** | N/A | Does not exist yet (as of 2026) |

**Recommended: UVCCamera via Android Java binding library**

1. Create a .NET Android binding project for UVCCamera
2. Use `USBMonitor` to detect USB camera connection
3. Use `UVCCamera` to open and capture frames
4. Convert frames to JPEG via Android `Bitmap.compress()`

```csharp
// Simplified flow
public class AndroidUsbCameraProvider : ICameraProvider
{
    // 1. Register USB device filter in AndroidManifest.xml
    // 2. Use USBManager to request permission
    // 3. Open UVCCamera on the device
    // 4. CaptureFrameAsync → grab frame from preview callback → compress to JPEG
}
```

**AndroidManifest.xml addition:**
```xml
<uses-feature android:name="android.hardware.usb.host" android:required="false" />
```

---

## Device Discovery

USB cameras can be plugged/unplugged at any time. The provider needs to
detect this:

### Windows
```csharp
// DeviceWatcher for USB video devices
var watcher = DeviceInformation.CreateWatcher(DeviceClass.VideoCapture);
watcher.Added += (s, info) => { /* new USB camera */ };
watcher.Removed += (s, update) => { Disconnected?.Invoke(this, EventArgs.Empty); };
watcher.Start();
```

### Android
```csharp
// BroadcastReceiver for USB_DEVICE_ATTACHED / USB_DEVICE_DETACHED
// Register in AndroidManifest.xml or dynamically
```

---

## Implementation Phases

| Step | Work | Priority |
|------|------|----------|
| 2.1 | `UsbCameraProvider` interface stub | Must |
| 2.2 | Windows MediaCapture implementation | Must |
| 2.3 | Device enumeration + picker UI | Must |
| 2.4 | Auto-detection (plug/unplug) | Should |
| 2.5 | Android UVC binding library | Could (complex) |
| 2.6 | Android USB Host implementation | Could |

**Note:** Android USB camera support is significantly harder than Windows. Consider
deferring Android USB to a later milestone and focusing on Windows first (which
covers the laptop + USB bodycam scenario).

---

## Settings

When a USB camera is detected, it appears in the camera picker with its
device name (e.g. "Logitech C920", "USB2.0 HD Camera").

The `ActiveCameraProvider` setting stores the provider ID with device
qualifier: `"usb:USB2.0 HD Camera"` or `"usb:{device-id}"`.
