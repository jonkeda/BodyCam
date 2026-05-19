# RCA-811: End-to-End Capture + Download Test

**Date:** 2025-05-18  
**Status:** IN PROGRESS  
**Depends on:** RCA-810 (WiFi transfer channel fix)

## Goal

Validate the full flow: take photo → enter transfer mode → get glasses IP → download JPG → save to disk.

## Test Plan

1. Connect to glasses via BLE (existing fixture)
2. Trigger `TakeAiPhotoAsync()` — sends BLE command to capture
3. Wait for media count update or 6s timeout
4. Enter transfer mode (uses the new channel-based IP discovery from RCA-810)
5. List media via HTTP (`/files/media.config`)
6. Download the newest photo
7. Validate JPEG magic bytes (SOI: `FF D8`, EOI: `FF D9`)
8. Write to `TestResults/rca-811-capture.jpg`
9. Output the file path for visual verification

## Run Command

```powershell
$env:BODYCAM_REAL_HEYCYAN="1"
$env:BODYCAM_REAL_HEYCYAN_MAC="D8:79:B8:7F:E6:C9"
dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0 --filter "FullyQualifiedName~CaptureAndDownloadTests" -v normal --logger "console;verbosity=detailed"
```

## Success Criteria

- Test passes green
- `TestResults/rca-811-capture.jpg` is a valid JPEG viewable in any image viewer
- Console output shows the BLE 0x41 channel receiving multiple notifications
- IP was obtained via one of: BLE 0x08 notify, WiFi Direct, hotspot, or candidate probe
