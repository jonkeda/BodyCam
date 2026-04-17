# M11 Phase 5 — Meta Ray-Ban Glasses Integration

## Goal

Support Meta Ray-Ban smart glasses as a camera source using the
**Meta Wearables Device Access Toolkit (DAT)** — available for both iOS and Android.

| | Android | iOS |
|--|---------|-----|
| **SDK** | [meta-wearables-dat-android](https://github.com/facebook/meta-wearables-dat-android) | [meta-wearables-dat-ios](https://github.com/facebook/meta-wearables-dat-ios) |
| **Language** | Kotlin | Swift |
| **Distribution** | GitHub Packages (Maven) | Swift Package Manager |
| **Modules** | `mwdat-core`, `mwdat-camera`, `mwdat-mockdevice` | `MWDATCore`, `MWDATCamera` |
| **Min OS** | Android 10+ (SDK 31+ for BT) | iOS 15.2+ (Xcode 14.0+) |

**Docs:** [wearables.developer.meta.com](https://wearables.developer.meta.com/docs/develop/)  
**API Reference:** [Android](https://wearables.developer.meta.com/docs/reference/android/dat/0.6) · [iOS](https://wearables.developer.meta.com/docs/reference/ios_swift/dat/0.6)  
**Version:** 0.6.0 (April 2026)  
**Status:** Public developer preview — **no longer blocked**

---

## SDK Overview

The Meta Wearables DAT is a public SDK (Android: Kotlin, iOS: Swift) that provides:

- **Video streaming** from glasses camera (`StreamSession`, `VideoFrame`)
- **Photo capture** during a stream (`capturePhoto()` → `PhotoData`)
- **Device discovery** via `AutoDeviceSelector` or `SpecificDeviceSelector`
- **Session lifecycle** (start/stop/pause/resume, device-driven state)
- **Mock device testing** without physical glasses (`MockDeviceKit`)
- **Compressed HEVC streaming** (v0.6.0) via `StreamConfiguration.compressVideo`

### Supported Hardware

| Device | Min Firmware |
|--------|-------------|
| Ray-Ban Meta glasses (Gen 1 & 2) | V22 |
| Meta Ray-Ban Display glasses | V21 |
| Oakley Meta HSTN glasses | V22 |
| Oakley Meta Vanguard glasses | V22 |

### Resolution & Frame Rate

| Quality | Resolution | Notes |
|---------|-----------|-------|
| `HIGH` | 720x1280 | Best detail, more BT bandwidth |
| `MEDIUM` | 504x896 | Balanced |
| `LOW` | 360x640 | Lowest bandwidth, best for continuous streaming |

Frame rates: 2, 7, 15, 24, 30 FPS. Adaptive bitrate automatically lowers
quality/framerate when BT bandwidth is constrained.

### Audio

Mic + speakers accessible via standard **HFP** (Hands-Free Profile), NOT
through the DAT SDK. HFP streams **8kHz mono** PCM.

| Platform | Audio Routing |
|----------|---------------|
| Android | `AudioManager.setCommunicationDevice()` with `TYPE_BLUETOOTH_SCO` |
| iOS | `AVAudioSession.setCategory(.playAndRecord, options: [.allowBluetooth])` |

---

## Integration Architecture

### .NET MAUI — Platform-Specific Integration

BodyCam targets both Android and iOS via .NET MAUI. Each platform uses
its native DAT SDK through platform-specific code in `Platforms/`.

#### Android: .NET Android Binding Library (recommended)

1. Create a .NET Android binding project for `mwdat-core` and `mwdat-camera` AARs
2. Generate C# bindings via `@(AndroidLibrary)` items
3. Call SDK directly from platform-specific code in `Platforms/Android/`

Alternative: Thin Kotlin wrapper service (AIDL/Messenger) — more isolation
but more complexity. Not recommended given the small SDK surface.

#### iOS: .NET iOS Native Reference

1. Add `MWDATCore` + `MWDATCamera` Swift packages to an Xcode framework project
2. Create a thin Objective-C bridging layer (Swift interop requires ObjC bridge)
3. Reference the compiled `.xcframework` via `@(NativeReference)` in the .NET iOS project
4. Call from platform-specific code in `Platforms/iOS/`

Alternative: Use [Swift Bindings for .NET](https://github.com/AathifMahir/Swift.Bindings)
if Swift-direct binding is mature enough by implementation time.

### MetaGlassesCameraProvider

Single cross-platform `ICameraProvider` with `#if ANDROID` / `#if IOS`
conditional compilation for platform-specific SDK calls.

```csharp
namespace BodyCam.Services.Camera;

/// <summary>
/// Camera provider for Meta Ray-Ban glasses via Meta Wearables DAT SDK.
/// Platform-specific code via #if ANDROID / #if IOS.
/// </summary>
public class MetaGlassesCameraProvider : ICameraProvider
{
    // DAT SDK types (from binding library)
    // private StreamSession? _streamSession;
    // private DeviceIdentifier? _deviceId;

    private byte[]? _latestFrame;
    private bool _connected;

    public string DisplayName => "Meta Ray-Ban";
    public string ProviderId => "meta";
    public bool IsAvailable => _connected;

    public event EventHandler? Disconnected;

    public async Task StartAsync(CancellationToken ct = default)
    {
        // 1. Initialize SDK (platform-specific, done at app startup):
        //    Android: Wearables.initialize(context)
        //    iOS:     try Wearables.configure()
        //
        // 2. Check registration state
        // 3. Check/request camera permission
        // 4. Create StreamSession:
        //
        //    Android:
        //      var streamSession = Wearables.StartStreamSession(
        //          context, new AutoDeviceSelector(),
        //          new StreamConfiguration(VideoQuality.Medium, frameRate: 15)
        //      );
        //      streamSession.videoStream.collect { frame -> ... }
        //
        //    iOS:
        //      let config = StreamSessionConfig(
        //          videoCodec: .raw, resolution: .medium, frameRate: 15)
        //      let session = StreamSession(
        //          streamSessionConfig: config,
        //          deviceSelector: AutoDeviceSelector(wearables: .shared))
        //      session.videoFramePublisher.listen { frame in
        //          guard let image = frame.makeUIImage() else { return }
        //      }
        //      await session.start()
        //
        // 5. Store latest frame, update _connected on state changes
        _connected = true;
    }

    public Task StopAsync()
    {
        // streamSession?.stop()
        _connected = false;
        return Task.CompletedTask;
    }

    public Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        // Option 1: Return latest frame from continuous stream
        // Option 2: Call streamSession.capturePhoto() for full-res JPEG
        return Task.FromResult(_latestFrame);
    }

    public async IAsyncEnumerable<byte[]> StreamFramesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Yield frames from the videoStream flow
        while (!ct.IsCancellationRequested && _connected)
        {
            if (_latestFrame is not null)
                yield return _latestFrame;
            await Task.Delay(33, ct); // ~30fps polling
        }
    }

    public ValueTask DisposeAsync()
    {
        // streamSession?.stop()
        _connected = false;
        return ValueTask.CompletedTask;
    }
}
```

### Frame Capture Strategy

Two approaches available from the DAT SDK:

| Method | API | Resolution | Latency | Format |
|--------|-----|-----------|---------|--------|
| **Stream frame** | `videoStream.collect { frame -> }` | Up to 720x1280 | Real-time | Raw bitmap (needs JPEG encoding) |
| **Photo capture** | `streamSession.capturePhoto()` | Full sensor res | ~200ms | HEIC/JPEG |

**For vision AI:** Use stream frames (lower res but faster, no shutter delay).
**For "Photo" button:** Use `capturePhoto()` for full quality.

### VideoFrame → JPEG Conversion

The DAT `VideoFrame` contains raw video buffer data. Convert to JPEG for
the VisionAgent:

**Android:**
```csharp
private byte[]? ConvertFrameToJpeg(VideoFrame frame)
{
    var bitmap = frame.ToBitmap();
    using var stream = new MemoryStream();
    bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 85, stream);
    return stream.ToArray();
}
```

**iOS:**
```csharp
private byte[]? ConvertFrameToJpeg(VideoFrame frame)
{
    // iOS DAT provides frame.makeUIImage() directly
    var uiImage = frame.MakeUIImage();
    var jpegData = uiImage.AsJPEG(0.85f);
    return jpegData?.ToArray();
}
```

With v0.6.0's compressed video option (`compressVideo` on Android,
`VideoCodec.h265` on iOS), frames arrive as HEVC — these need decoding
first or can be used directly if VisionAgent supports it.

---

## Setup Requirements

### 1. App Registration with Meta AI

One-time flow — user registers BodyCam with their glasses through the Meta AI app:

```
User opens BodyCam → taps "Connect Meta Glasses"
  → Platform-specific registration call
  → Deep-links to Meta AI app for confirmation
  → Returns to BodyCam with registration complete
```

**Android:**
```kotlin
Wearables.startRegistration(activity)
// Observe: Wearables.registrationState.collect { state -> ... }
```

**iOS:**
```swift
try Wearables.shared.startRegistration()
// Observe: for await state in wearables.registrationStateStream() { ... }
```

### 2. Camera Permission

First-time camera access requires permission grant through Meta AI app:

**Android:**
```kotlin
val status = Wearables.checkPermissionStatus(Permission.CAMERA)
if (status != PermissionStatus.Granted) {
    requestWearablesPermission(Permission.CAMERA) // deep-links to Meta AI
}
```

**iOS:**
```swift
var cameraStatus = try await wearables.checkPermissionStatus(.camera)
if cameraStatus != .granted {
    cameraStatus = try await wearables.requestPermission(.camera)
}
```

### 3. Platform Configuration

#### Android: AndroidManifest.xml

```xml
<uses-permission android:name="android.permission.BLUETOOTH" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
<uses-permission android:name="android.permission.INTERNET" />

<application ...>
    <!-- Use 0 for dev mode; production gets unique ID from Wearables Dev Center -->
    <meta-data
        android:name="com.meta.wearable.mwdat.APPLICATION_ID"
        android:value="0" />

    <!-- Opt out of Meta analytics -->
    <meta-data
        android:name="com.meta.wearable.mwdat.ANALYTICS_OPT_OUT"
        android:value="true" />

    <!-- Callback scheme for Meta AI app -->
    <activity android:name=".MainActivity" ...>
        <intent-filter>
            <action android:name="android.intent.action.VIEW" />
            <category android:name="android.intent.category.DEFAULT" />
            <category android:name="android.intent.category.BROWSABLE" />
            <data android:scheme="bodycam" />
        </intent-filter>
    </activity>
</application>
```

#### iOS: Info.plist

```xml
<!-- Custom URL scheme for Meta AI callbacks -->
<key>CFBundleURLTypes</key>
<array>
  <dict>
    <key>CFBundleTypeRole</key><string>Editor</string>
    <key>CFBundleURLName</key><string>$(PRODUCT_BUNDLE_IDENTIFIER)</string>
    <key>CFBundleURLSchemes</key>
    <array><string>bodycam</string></array>
  </dict>
</array>

<!-- External Accessory protocol for Meta Wearables -->
<key>UISupportedExternalAccessoryProtocols</key>
<array><string>com.meta.ar.wearable</string></array>

<!-- Background modes for Bluetooth -->
<key>UIBackgroundModes</key>
<array>
  <string>bluetooth-peripheral</string>
  <string>external-accessory</string>
</array>
<key>NSBluetoothAlwaysUsageDescription</key>
<string>Needed to connect to Meta Wearables</string>

<!-- MWDAT configuration -->
<key>MWDAT</key>
<dict>
  <key>AppLinkURLScheme</key><string>bodycam://</string>
  <key>MetaAppID</key><string></string>  <!-- empty for dev mode -->
  <key>Analytics</key>
  <dict>
    <key>OptOut</key><true/>
  </dict>
</dict>
```

**Note:** iOS SDK uses `ExternalAccessory` framework — **App Store submission
is not currently supported** by Meta. Distribution via release channels only.

### 4. SDK Dependencies

#### Android: Gradle (for binding library)

```toml
[versions]
mwdat = "0.6.0"

[libraries]
mwdat-core = { group = "com.meta.wearable", name = "mwdat-core", version.ref = "mwdat" }
mwdat-camera = { group = "com.meta.wearable", name = "mwdat-camera", version.ref = "mwdat" }
mwdat-mockdevice = { group = "com.meta.wearable", name = "mwdat-mockdevice", version.ref = "mwdat" }
```

Maven repository requires a GitHub PAT with `read:packages` scope.

#### iOS: Swift Package Manager

Add via Xcode: `https://github.com/facebook/meta-wearables-dat-ios`

Import in Swift bridging code:
```swift
import MWDATCore
import MWDATCamera
```

---

## Session Lifecycle

DAT sessions are device-driven. The glasses control state transitions:

**Android** `StreamSessionState`: `STARTING` → `STARTED` → `STREAMING` → `STOPPING` → `STOPPED` → `CLOSED`  
**iOS** `StreamSessionState`: `stopping` → `stopped` → `waitingForDevice` → `starting` → `streaming` → `paused`

| State | Meaning | BodyCam Action |
|-------|---------|---------------|
| Starting/WaitingForDevice | Session initializing | Show "Connecting..." |
| Streaming | Camera active, frames flowing | Capture frames for vision |
| Paused | User doffed glasses or system gesture | Hold, wait for resume |
| Stopped | Session ended | Release resources, allow restart |

**Pause triggers:** User takes off glasses, closes hinges, or another app takes over.
**Stop triggers:** BT disconnect, user removes app from Meta AI, hinge close.

BodyCam should observe `SessionState` and update `ICameraProvider.IsAvailable`
accordingly. On `STOPPED`, fire `Disconnected` event for `CameraManager` fallback.

---

## Audio via HFP (M12 Integration)

Meta glasses mic/speaker use standard BT HFP, not the DAT SDK.

**Android:**
```csharp
var audioManager = context.GetSystemService(Context.AudioService) as AudioManager;
var devices = audioManager.AvailableCommunicationDevices;
var glasses = devices.FirstOrDefault(d => d.Type == AudioDeviceType.BluetoothSco);
if (glasses != null)
{
    audioManager.Mode = AudioMode.Normal;
    audioManager.SetCommunicationDevice(glasses);
}
```

**iOS:**
```csharp
// Via AVAudioSession in platform-specific code
var audioSession = AVAudioSession.SharedInstance();
audioSession.SetCategory(AVAudioSessionCategory.PlayAndRecord,
    AVAudioSessionCategoryOptions.AllowBluetooth);
audioSession.SetActive(true);
```

**Important:** HFP must be configured BEFORE starting a DAT stream session.
8kHz mono — glasses use beamforming to isolate wearer's voice.

---

## Testing with MockDeviceKit

The SDK includes `MockDeviceKit` for testing without glasses on both platforms:

**Android:**
```kotlin
val mockDeviceKit = MockDeviceKit.getInstance(context)
val device = mockDeviceKit.pairRaybanMeta()
val cameraKit = device.services.getCameraKit()
cameraKit.setCameraFeed(CameraFacing.BACK) // Use phone camera as mock
// OR
cameraKit.setCapturedImage(testImageUri) // Static test image

val config = MockDeviceKitConfig(
    initiallyRegistered = true,
    initialPermissionsGranted = true
)
mockDeviceKit.enable(config)
```

**iOS:**
```swift
try? Wearables.configure()
let device = MockDeviceKit.shared.pairRaybanMeta()
let camera = device?.getCameraKit()
await camera?.setCameraFeed(fileURL: videoURL)      // Mock video stream
await camera?.setCapturedImage(fileURL: imageURL)   // Mock photo capture
```

Both enable CI testing of the MetaGlassesCameraProvider without hardware.

---

## Implementation Phases

| Step | Work | Priority |
|------|------|----------|
| 5.1 | Register on [Wearables Developer Center](https://wearables.developer.meta.com/) | Must |
| 5.2 | Create .NET Android binding library for mwdat-core + mwdat-camera | Must |
| 5.3 | Create .NET iOS native reference (xcframework + ObjC bridge) | Must |
| 5.4 | Implement `MetaGlassesCameraProvider` (shared + platform code) | Must |
| 5.5 | Registration + permission UI flow in BodyCam | Must |
| 5.6 | AndroidManifest.xml + Info.plist updates | Must |
| 5.7 | Frame-to-JPEG conversion pipeline (both platforms) | Must |
| 5.8 | Session lifecycle handling (pause/resume/disconnect) | Must |
| 5.9 | MockDeviceKit integration for tests (both platforms) | Should |
| 5.10 | HFP audio coordination with M12 | Should |
| 5.11 | HEVC compressed streaming (v0.6.0) | Could |

---

## Dependencies

- Meta Wearables Developer Center account (free)
- GitHub PAT with `read:packages` scope (for Maven — Android only)
- Android device with Meta AI app v254+ and/or iOS device with Meta AI app v254+
- Meta glasses with firmware v22+ (or MockDeviceKit for testing)
- .NET Android binding project for DAT SDK AARs
- .NET iOS native reference (.xcframework) for MWDATCore + MWDATCamera

## Known Limitations

- **iOS App Store blocked**: Meta uses `ExternalAccessory` framework which triggers
  Apple MFi rejection. Distribution only via Meta's release channels for now.
- **HFP audio quality**: 8kHz mono — significantly lower than phone mic. Glasses
  beamforming isolates wearer voice but ambient sound is reduced.
- **BT bandwidth**: Adaptive bitrate can drop quality. Lower framerate/resolution
  yields better per-frame quality.
- **One 3rd-party app at a time** in Developer Mode — registering a new app
  unregisters the previous one.
- **Glasses hinge close during streaming** can get the camera service stuck
  until glasses reboot.
