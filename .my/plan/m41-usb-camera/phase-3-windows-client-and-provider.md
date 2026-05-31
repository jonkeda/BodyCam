# Phase 3 - Windows C# Client And BodyCam Provider

**Status:** Implemented; live UI capture path passed

## Goal

Turn the proven direct Windows USB/UVC path into a reusable C# client and then a
real BodyCam camera provider.

## Scope

This phase has two provider modes:

1. **Direct Windows USB mode:** the USB camera is plugged into the laptop and
   Windows reads it as a normal USB/UVC camera.

Direct Windows USB mode comes first because Phase 1A proved it works.

## Planned Files

Names may change to fit the existing codebase:

```text
Services/Camera/Usb/
  UsbCameraProvider.cs
  WindowsUsbCameraClient.cs
  UsbCameraOptions.cs
```

## Tasks

- [x] Promote the Phase 1A MediaCapture probe into a reusable
      `WindowsUsbCameraClient`.
- [x] Capture one still image from `VID_349C&PID_0411`.
- [x] Save a proof artifact under the M41 captures folder.
- [ ] Add optional short MJPEG/video artifact download.
- [x] Add `UsbCameraProvider` implementing `ICameraProvider`.
- [x] Register the provider in DI.
- [x] Add an Add Devices entry for **USB Camera**.
- [x] Add settings for device id/name/VID-PID.
- [x] Add unit tests for success/failure provider behavior.
- [x] Add UI tests for Add Devices and the USB Camera settings page.
- [x] Add hardware-gated real UI test for **Test Capture**.
- [x] Add hardware-gated real UI test for selecting **USB Camera** and using
      **Take Picture**.

## Acceptance Criteria

- Windows C# captures one JPEG from the USB camera.
- BodyCam can select `USB Camera` as a camera source.
- `CaptureFrameAsync()` returns JPEG bytes from the USB camera path.
- Android direct USB support remains a separate platform branch.

## Outcome - 2026-05-30

Implemented the Windows USB camera app integration.

Added shared USB camera services:

- `src/BodyCam/Services/Camera/Usb/IUsbCameraClient.cs`
- `src/BodyCam/Services/Camera/Usb/UsbCameraProvider.cs`
- `src/BodyCam/Services/Camera/Usb/WindowsUsbCameraClient.cs`

Added settings and UI:

- `ISettingsService.UsbCameraDeviceMatch`
- `SettingsService.UsbCameraDeviceMatch`
- `UsbCameraSettingsViewModel`
- `UsbCameraSettingsPage`
- **Add USB Camera** card in Add Devices
- `UsbCameraSettingsPage` route in `AppShell`

Provider details:

```text
Provider id: usb-camera
Display name: USB Camera
Default device match: VID_349C&PID_0411
Capture path: Windows MediaCapture -> JPEG bytes
Platform scope: Windows-only route, DI registration, and Add Devices card
```

Verification:

```text
dotnet test src\BodyCam.Tests\BodyCam.Tests.csproj --filter "UsbCamera|AddDevices"
Result: passed 22/22

dotnet build src\BodyCam\BodyCam.csproj -f net10.0-windows10.0.19041.0
Result: passed, warnings only

dotnet build src\BodyCam\BodyCam.csproj -f net10.0-android
Result: passed, warnings only

dotnet test src\BodyCam.UITests\BodyCam.UITests.csproj --filter "FullyQualifiedName~UsbCameraSettingsTests|FullyQualifiedName~AddDevicesTests"
Result: passed 10/10

BODYCAM_REAL_USB_CAMERA_UI=1 dotnet test src\BodyCam.UITests\BodyCam.UITests.csproj --filter "FullyQualifiedName~UsbCameraHardwareUiTests"
Result: passed 2/2
```

Known remaining check:

- The provider and settings route are built, unit-tested, covered by UI
  navigation smoke tests, verified through the real **Test Capture** UI button,
  and verified through the real **Take Picture** flow after selecting **USB
  Camera** as the active camera source.

## Risks

- Direct Windows USB support may expose the camera under a friendly device name
  that differs from the vendor/product id.

## Stop Condition

Stop this phase when BodyCam can capture one USB camera frame through the
provider. Do not add broad UI polish before the provider works.
