# Stored Image Fallback For HeyCyan Glasses Testing

**Date:** 2026-05-19
**Status:** Proposed
**Goal:** Keep Windows app and test work moving while real HeyCyan WiFi transfer is blocked by returning a deterministic stored JPEG from the glasses media-download step.

## Short Answer

Add a Windows-only fallback for the HeyCyan media download. The `Capture` button on the Devices page should still run the normal HeyCyan capture flow and use the real Bluetooth command to trigger the glasses capture, so the user gets the real glasses behavior, including the click sound. After that, instead of trying to connect to the glasses WiFi and download the new photo, BodyCam returns a known local JPEG as the captured image.

This should not pretend that WiFi Direct, hotspot routing, or the real glasses media download works on Windows. It is a controlled substitute so we can continue validating the capture workflow, image handling, UI, and downstream processing. Android should continue to use the real image download path by default.

## Why This Is Needed

The current Windows investigation shows that the glasses can be put into transfer mode, but Windows cannot complete a usable peer-to-peer photo download path yet:

- Windows WiFi Direct support appears insufficient for this device flow.
- Hotspot-style association has not produced a stable route to the glasses media endpoint.
- Existing RealTests cover the iOS/hotspot flow, but not this Android-style direct P2P flow.
- We still need reliable app and test coverage for "take picture, receive image, process/display image".

Until the transport is solved, the best low-risk step is to keep the capture-command path real and isolate only the image-download problem.

## Proposed Behavior

When the Devices page `Capture` button is used on Windows:

1. The app/test requests a capture from the normal HeyCyan camera path.
2. BodyCam sends the real Bluetooth capture command to the glasses.
3. The glasses should take a photo and make the normal click sound.
4. BodyCam skips the WiFi connection/download step.
5. The fallback downloader creates a deterministic local JPEG on first use, then reads that JPEG from disk.
6. The result is returned through the same application-facing capture contract used by real glasses photos.
7. Logs and status metadata clearly say the image bytes came from a stored fallback source.

When the Windows fallback is not in play:

1. The real HeyCyan hardware path behaves exactly as it does today.
2. Missing WiFi/P2P support continues to fail visibly.
3. The fallback image is never used silently on non-Windows platforms.

When the same flow runs on Android:

1. Android uses the real HeyCyan media download implementation.
2. The photo returned to the app is the actual image captured by the glasses.
3. The stored-image fallback is not used unless a dedicated Android test configuration explicitly selects it.

## Recommended Implementation

Create a small stored-image implementation at the media-transfer/download boundary, not as a replacement for the whole HeyCyan camera provider.

Preferred shape:

- `StoredImageHeyCyanMediaTransfer` or equivalent
  - Creates a deterministic local JPEG on first use.
  - Reads bytes from that local JPEG path.
  - Optionally validates the image is JPEG.
  - Returns media/download metadata with `Source = StoredImageFallback` or similar.
  - Does not know anything about BLE, WiFi, WCL, pairing, or HeyCyan transport.

- Dependency injection
  - Keep the real HeyCyan camera/capture service registered.
  - On Windows, replace only the media-transfer/download service with the stored-image implementation.
  - On Android and iOS, register/use the real media-transfer implementation.

This keeps most of the real HeyCyan implementation in use while carving out only the part Windows cannot currently perform: connecting to the glasses WiFi and downloading the image bytes.

## Fixture Location

For Windows app development, the fallback downloader should create the JPEG automatically under app data:

`%LOCALAPPDATA%\BodyCam\HeyCyanFallback\stored-heycyan-fallback.jpg`

The seed image should be small and deterministic: a normal JPEG that exercises the image pipeline without bloating the repository. If the application needs EXIF or orientation behavior, add a second explicit fixture for that case instead of making the default image complicated.

## Test Plan

Add focused tests for the fallback provider:

- Creates the fallback JPEG when it is missing.
- Reads the created JPEG and returns image bytes.
- Marks the capture result as fallback data.
- Does not call WiFi, WCL, or peer-to-peer networking services.
- Allows the real Bluetooth capture command path to run when hardware is present.
- Preserves expected content type or format metadata.

Add app-flow tests using the fallback provider:

- Trigger the same Devices page `Capture` button workflow used for glasses.
- Verify the capture command path can remain the real HeyCyan Bluetooth path.
- Verify the UI receives and displays an image.
- Verify downstream image-processing code can consume the capture result.
- Verify status/log text distinguishes fallback from real glasses download.
- Verify Android configuration continues to select the real media download implementation by default.

Keep RealTests separate:

- iOS/hotspot RealTests remain hardware-dependent.
- Android/P2P RealTests should stay skipped unless the required glasses, adapter, and environment variables are present.
- Fallback tests should be CI-friendly and not require hardware.

## Acceptance Criteria

- A developer can run the Windows app or tests without glasses and receive a deterministic JPEG through the glasses capture workflow.
- A developer with glasses can still trigger the real Bluetooth photo command and hear/observe the capture action.
- The fallback is always active for Windows builds until the real Windows media download path is solved.
- The UI/log/test metadata clearly identifies the download bytes as stored fallback data.
- The real HeyCyan capture command path is preserved.
- The real HeyCyan WiFi download path is unchanged when fallback is disabled.
- Android uses the real captured image by default.
- CI can validate the capture/display/image-processing flow without requiring WiFi Direct or a physical device.
- The same fake-download boundary can later be reused by Android and iOS app tests when those tests explicitly opt into fake download mode.

## Suggested Phases

1. Add this plan and choose the fixture strategy.
2. Add a stored-image implementation behind the existing HeyCyan media-transfer/download abstraction.
3. Wire Windows dependency injection to use the stored-image downloader.
4. Add a small deterministic JPEG seed.
5. Add unit tests for fallback downloader behavior.
6. Add one app-flow or UI test proving the capture workflow can continue.
7. Verify Android still selects the real media download path in normal runtime configuration.
8. Reuse the same fake-download behavior for Android and iOS app tests only where those tests explicitly opt in.
9. Keep the WiFi/P2P research separate and resume it when there is a new Windows transport lead.

## Open Questions

- Does the current capture result contract already have a place for source metadata, or do we need to add one?
- Do we only need a single captured-photo result, or should the fallback also simulate a glasses media listing/download flow?
- Should hardware-present tests require the Bluetooth command to succeed before returning the fallback image, or should CI tests bypass Bluetooth entirely?
- Should Windows expose a developer setting later to switch back to the real media downloader once a promising transport path exists?

## Recommendation

Start with a Windows-only stored-image downloader. Keep the real HeyCyan Bluetooth capture command in place, then substitute only the media download result. This lets the team continue validating the application behavior and gives users the real glasses capture feedback without blurring the line between a real WiFi download and a controlled test substitute.
