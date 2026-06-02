# Phase 1b - Logged-In Location-On Oracle Run

## Goal

Run the official HeyCyan app again with the useful Android preconditions now in
place:

- phone unlocked;
- user logged into HeyCyan;
- Android Location enabled;
- Bluetooth enabled;
- glasses powered on.

The purpose is to observe the real app's BLE discovery, device connection,
WiFi/P2P handoff, and media transfer behavior without changing BodyCam code.

## Why This Phase Exists

Phase 1a proved ADB access and identified the official app package, but the
first launch was behind the lock/AOD surface. It also showed Android Location
was disabled, which can block BLE scanning even when the app has Bluetooth and
location permissions.

Phase 1b repeats the capture after those two blockers were removed.

## Plan

1. Create a fresh timestamped capture folder.
2. Verify location and Bluetooth state.
3. Capture current foreground app/UI state.
4. Save a before snapshot:
   - logcat;
   - WiFi/P2P service state;
   - network interfaces and routes;
   - HeyCyan app/window state;
   - screenshot and UI XML.
5. Clear logcat.
6. Launch or foreground HeyCyan if needed.
7. Observe the logged-in app while the user or ADB drives the safest next
   visible action.
8. Capture after snapshots and filtered logs.
9. Record whether we saw:
   - BLE scan/connect;
   - transfer-mode command;
   - WiFi Direct/P2P group formation;
   - group owner IP;
   - HTTP media endpoint calls.

## Acceptance

- Capture folder exists under `captures/`.
- Location-on state is recorded.
- The current HeyCyan logged-in screen is captured.
- At least one clean logcat window is saved after location was enabled.
- The next phase is clear from evidence, not guesswork.

## Notes

If the logged-in screen requires a tap that cannot be safely inferred from UI
XML/screenshot, keep the app open and let the user drive that action while ADB
captures logs.

## Result

Capture folder:

- `captures/phase-1b-20260531-180628`

Findings:

- ADB detected the phone as `SM_S931B`.
- HeyCyan version was `1.0.121_20260529`.
- Android Location was enabled and the lock screen was dismissed.
- The official app reached `com.glasssutdio.wear/.MainActivity`.
- The Home screen showed connected glasses:
  - device: `M01 Pro_E6C9`;
  - status: `Connected`;
  - battery: `44%`.
- Opening Album showed `There are 64 new contents available to import to your
  smartphone.`
- Opening Album produced two BLE GATT writes with length `8`.
- Opening Album did not form a WiFi Direct group:
  - `groupFormed: false`;
  - `mGroup null`;
  - `p2p0` remained down.
- The phone stayed on normal WiFi `jobaboe`, `192.168.1.67/24`.

Conclusion:

Album browsing alone is not the transfer trigger. The visible `Import` action is
the next useful oracle step.
