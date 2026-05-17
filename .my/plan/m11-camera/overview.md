# M11 — Camera Architecture

**Status:** PLANNING  
**Goal:** Unified camera abstraction supporting multiple camera sources — smart glasses,
phone camera, USB bodycams, and WiFi cameras — with one active camera at a time.

**Absorbs:** M4 camera portions (BT/WiFi glasses camera). M4 retains BT audio routing
and button mapping only.

**Related:** [M33 — HeyCyan Glasses SDK Integration](../m33-heycyan-sdk/overview.md)
supplies the concrete implementation for Phase 4 (Chinese WiFi glasses). Important:
the HeyCyan/QCSDK hardware does **not** stream live RTSP/MJPEG video — it captures
photos to internal storage and transfers them via WiFi-Direct + HTTP. The Phase 4
protocol assumptions in [wifi-camera.md](wifi-camera.md) (RTSP/MJPEG live stream)
apply only to generic IP cameras / non-HeyCyan WiFi glasses.

---

## Why This Matters

BodyCam's core value proposition is **being a camera-powered AI assistant**. The current
implementation is tightly coupled to CommunityToolkit.Maui's `CameraView` — a UI control
that requires the preview to be visible and started. This blocks every camera scenario
except "phone screen is on and camera tab is active."

A body-worn AI camera needs to work with:
- Smart glasses on your face (Meta Ray-Ban, Chinese BT/WiFi glasses)
- A USB clip-on bodycam
- The phone camera (existing, as fallback)
- Possibly IP cameras / WiFi cameras

All of these should funnel through a single abstraction so the vision pipeline
(VisionAgent, DescribeSceneTool, etc.) doesn't care where frames come from.

---

## Camera Sources

| Source | Protocol | Latency | Resolution | Notes |
|--------|----------|---------|------------|-------|
| **Meta Ray-Ban** | Meta SDK | ~100ms | 1080p | Requires Meta developer account, SDK integration |
| **Chinese BT/WiFi glasses** | WiFi-Direct / RTSP / BT | varies | 720p-1080p | Protocol varies by model, needs reverse-engineering |
| **Phone camera** | CameraView (existing) | <50ms | up to 4K | Already works, needs headless capture |
| **USB bodycam** | UVC (Windows), USB Host (Android) | <50ms | 720p-1080p | Standard USB video device class |
| **WiFi/IP cameras** | RTSP / ONVIF / HTTP MJPEG | 100-500ms | varies | Standard protocols |

---

## Architecture

### Core Abstraction

```
ICameraProvider (per source type)
  ├── MetaGlassesCameraProvider      ← Meta SDK
  ├── WifiGlassesCameraProvider      ← RTSP/WiFi-Direct
  ├── PhoneCameraProvider            ← CameraView wrapper
  ├── UsbCameraProvider              ← UVC / platform USB
  └── IpCameraProvider               ← RTSP / MJPEG

CameraManager (single active camera)
  → Selects provider
  → Captures frames via ICameraProvider
  → Feeds into FrameCaptureFunc (existing orchestrator delegate)
```

### Data Flow

```
┌─────────────────┐
│  Camera Source   │  (glasses, phone, USB, IP)
└────────┬────────┘
         │ ICameraProvider.CaptureFrameAsync() → byte[] (JPEG)
         ▼
┌─────────────────┐
│  CameraManager   │  One active provider, selection UI
└────────┬────────┘
         │ FrameCaptureFunc delegate
         ▼
┌─────────────────┐
│  Orchestrator    │  Existing pipeline
│  → VisionAgent   │  DescribeFrameAsync(frame)
│  → Tools         │  describe_scene, read_text, find_object
└─────────────────┘
```

---

## Phases

### Phase 1: Camera Abstraction & Phone Camera
Refactor existing code into the `ICameraProvider` abstraction. Wrap the existing
CameraView into `PhoneCameraProvider`. Add headless capture (start preview
off-screen, capture, stop). All existing features continue to work.

**Deliverables:** `ICameraProvider`, `CameraManager`, `PhoneCameraProvider`,
settings UI for camera selection.

### Phase 2: USB Bodycam
Implement `UsbCameraProvider` using platform-specific UVC access. Windows:
MediaFoundation / DirectShow. Android: USB Host API + UVC driver.

**Deliverables:** `UsbCameraProvider`, device enumeration, auto-detection.

### Phase 3: WiFi / IP Cameras
Implement `IpCameraProvider` for RTSP and HTTP MJPEG streams. This covers
WiFi cameras and acts as the foundation for WiFi glasses.

**Deliverables:** `IpCameraProvider`, RTSP client, stream URL configuration.

### Phase 4: Chinese WiFi Glasses
Implement `WifiGlassesCameraProvider`. Protocol varies by model — may use
RTSP, WiFi-Direct, or proprietary protocol. Builds on Phase 3 RTSP work.

For **HeyCyan / QCSDK / TKYUAN** glasses specifically, this is delivered by
[M33 Phase 2](../m33-heycyan-sdk/overview.md) as `HeyCyanCameraProvider`,
which is a *file-based snapshot* provider (BLE photo command → WiFi-Direct
transfer mode → HTTP `GET /files/<name>.jpg`), not a live RTSP stream.

**Deliverables:** `WifiGlassesCameraProvider`, WiFi-Direct discovery,
per-model protocol adapters.

### Phase 5: Meta Ray-Ban Integration
Implement `MetaGlassesCameraProvider` using the Meta SDK. Requires developer
account and SDK integration.

**Deliverables:** `MetaGlassesCameraProvider`, Meta SDK integration, pairing flow.

### Phase 6: iOS Platform Support
Implement `PlatformCameraProvider` for iOS using AVFoundation (`AVCaptureSession`,
`AVCapturePhotoOutput`). Headless capture without requiring a visible preview.
Register in DI with `#elif IOS` alongside existing Windows/Android providers.

**Deliverables:** iOS `PlatformCameraProvider` (AVFoundation), camera permission
handling, `CameraManager` initialization on iOS, settings picker works on iOS.

---

## Exit Criteria

- [ ] `ICameraProvider` interface defined and implemented for all 5 sources
- [ ] `CameraManager` manages active provider with UI for selection
- [ ] All existing vision tools work with any camera source
- [ ] Frame capture works without CameraView being visible
- [ ] Settings page has camera source picker
- [ ] Automatic fallback to phone camera when selected source disconnects

---

## Documents

| Document | Purpose |
|----------|---------|
| [overview.md](overview.md) | This file — scope, phases, exit criteria |
| [camera-abstraction.md](camera-abstraction.md) | ICameraProvider interface, CameraManager, DI setup |
| [phone-camera.md](phone-camera.md) | PhoneCameraProvider — wrapping CameraView, headless capture |
| [usb-camera.md](usb-camera.md) | USB bodycam — UVC on Windows, USB Host on Android |
| [wifi-camera.md](wifi-camera.md) | WiFi/IP cameras and WiFi glasses — RTSP, MJPEG, WiFi-Direct |
| [meta-glasses.md](meta-glasses.md) | Meta Ray-Ban SDK integration |
