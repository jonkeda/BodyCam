# Phase 1 - Mobile App Oracle Capture

## Goal

Use the official HeyCyan mobile app as an oracle and capture the real sequence
that gets the phone connected to the glasses WiFi/media transport.

## Work

- Prepare a clean Android device state:
  - glasses paired and charged;
  - BodyCam stopped;
  - official HeyCyan app installed;
  - old WiFi Direct groups removed if possible.
- Capture `adb logcat` with focused tags and a full fallback log.
- Capture network state before, during, and after transfer:
  - WiFi connection info;
  - P2P peers;
  - group owner IP;
  - routes;
  - open sockets.
- Capture BLE writes/notifications if possible:
  - Android Bluetooth HCI snoop log;
  - app logs;
  - external BLE sniffer only if needed.
- Trigger the app flow:
  - connect glasses;
  - open gallery/media download;
  - download one photo;
  - exit/cleanup transfer mode.
- Save all artifacts under:
  - `.my/plan/m46-heycyan-csharp-wifi-retry/captures/YYYY-MM-DD-*`
- Create a short RCA report with:
  - exact user actions;
  - timestamps;
  - BLE command candidates;
  - P2P discovery/connect timing;
  - group owner IP;
  - HTTP endpoints hit;
  - failure or success conclusion.

## Acceptance

- We have at least one timestamped successful official-app transfer capture.
- The report includes a sequence diagram from BLE command to HTTP file download.
- The report identifies the minimum unknowns to test from C#.

## Notes

This phase is observation only. Do not change BodyCam runtime code yet.
