# Phase 1a - First Android Oracle Shot

## Goal

Take the first live shot with the phone on USB and the glasses powered on:
verify ADB access, identify the installed HeyCyan app package, capture baseline
Android state, then attempt one observation run around the official app.

## Plan

1. Verify ADB sees exactly one usable Android device.
2. Identify the installed HeyCyan package and launchable activity.
3. Create a timestamped capture folder.
4. Save baseline device state:
   - packages matching HeyCyan/Cyan;
   - WiFi/P2P service state;
   - network interfaces and routes;
   - Bluetooth state where accessible;
   - current foreground activity.
5. Clear logcat, launch the HeyCyan app, and capture focused logs.
6. If the app can be driven from ADB safely, open likely media/gallery flows.
   Otherwise, capture logs while the user drives the app manually.
7. Save a short high-level result and decide the next phase.

## Acceptance

- Capture folder exists under `captures/`.
- ADB/device/package state is recorded.
- At least one logcat capture is saved.
- The log identifies one of:
  - a package/activity to automate next;
  - P2P/media tags to filter next;
  - a blocker that needs a new phase.

## Notes

This is still observation. No BodyCam runtime code changes belong in this phase.

## Result

- ADB detected one Android device: `SM_S931B`.
- Official HeyCyan package identified as `com.glasssutdio.wear`.
- Launchable activity identified as
  `com.glasssutdio.wear/.home.activity.SplashQcActivity`.
- App version observed: `1.0.121_20260529`.
- Important runtime permissions were already granted for WiFi/Bluetooth/location.
- `CAMERA` and `RECORD_AUDIO` were not granted; those do not block media WiFi
  transfer observation.
- WiFi P2P was idle before and after the locked launch attempt.
- The first launch reached `SplashQcActivity` and then `LoginActivity`, but the
  lock/AOD surface kept focus.
- Android Location was off, and Bluetooth logs showed `Location is off`, so
  BLE discovery could be blocked.
- Next phase: repeat with the phone unlocked/logged in and Android Location on.
