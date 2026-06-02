# Phase 6 - BodyCam Integration And UX Gate

**Status:** Partial implementation started. The existing camera provider now uses
the proven capture-first transfer order; settings and diagnostics are still
pending.

## Goal

Promote the proven C# HeyCyan WiFi path into BodyCam without destabilizing the
current glasses integration.

## Work

- [ ] Add an experimental setting:
  - `HeyCyanUseCSharpWifiTransfer`
- [ ] Add diagnostics on the glasses settings page:
  - BLE connected;
  - transfer mode command status;
  - P2P peer found;
  - group owner IP;
  - media config status;
  - last JPEG download latency.
- [x] Route the existing `HeyCyanCameraProvider` through the proven capture-first
  C# transfer order on Android.
- [ ] Keep existing path available as fallback until M46 is proven.
- [ ] Add telemetry:
  - `heycyan.wifi.phase`;
  - `heycyan.wifi.latency_ms`;
  - `heycyan.wifi.error_category`;
  - selected IP and route type.
- [ ] Add user-facing failure messages that explain permission, WiFi, or glasses
  state problems.

## Implementation Notes

2026-05-31:

- The existing DI path already uses `HeyCyanMediaTransfer` on Android, so no new
  camera provider was added.
- `HeyCyanCameraProvider.CaptureFrameAsync()` now triggers `TakePhotoAsync()`
  first, waits briefly for file finalization, then lists/downloads through the
  warm transfer helper.
- The provider no longer opens transfer mode or lists `/files/media.config`
  before a fresh capture exists.
- Unit tests assert the order is `photo` then `list`.
- The settle delay defaults to `5s` in production and is overrideable in tests.

## Acceptance

- The user can enable the C# WiFi path from settings.
- Take Picture/Look can capture from HeyCyan through the C# path.
- If capture fails, diagnostics explain the failing stage.
- The default path remains unchanged unless the experimental setting is enabled.
