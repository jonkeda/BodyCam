# M33 Phase 7 Wave 5 — Manual Acceptance Checklist (iOS)

**Date:** 2026-04-30
**Platform:** iOS
**Tester:** _[name]_

## Pre-flight

- [ ] Glasses charged ≥ 60%, firmware version: _[version]_
- [ ] Phone unpaired from any prior glasses (Bluetooth cache cleared)
- [ ] App built in Release mode
- [ ] `HEYCYAN_E2E=1` env var set for automated harness
- [ ] Test results folder created: `TestResults/m33-phase7/2026-04-30/`

## Manual Test Plan

| # | Step | Expected | Pass? | Notes |
|---|------|----------|-------|-------|
| 1 | Cold-boot phone, open BodyCam | Glasses widget hidden in shell | [ ] | |
| 2 | `GlassesPage` → Scan | HeyCyan device appears with name + MAC + RSSI | [ ] | Device: |
| 3 | Tap device → Connect | Status panel populated (battery, MAC, fw, hw); shell widget shows battery | [ ] | Battery: __%<br>MAC: __<br>FW: __<br>HW: __ |
| 4 | Start a Realtime conversation | Mic = glasses HFP, speaker = glasses A2DP, camera = `HeyCyanCameraProvider` | [ ] | |
| 5 | Single-tap glasses button | Configured action fires (default: start/stop conversation) | [ ] | Action: |
| 6 | Double-tap glasses button | Photo captured; `VisionAgent` receives JPG round-trip | [ ] | |
| 7 | Long-press glasses button | Conversation ends cleanly | [ ] | |
| 8 | Power off glasses mid-call | Fallback within 2 s (Wave 4 latency table); call continues on phone | [ ] | Fallback time: __ms |
| 9 | Power glasses back on | Auto-reconnect; all four providers re-bind without user action | [ ] | Reconnect time: __s |
| 10 | Disconnect manually from `GlassesPage` | Returns to scan list; shell widget hidden | [ ] | |
| 11 | (Optional, P5 only) Open recorded media gallery | OPUS / MP4 / JPG files download via WiFi-Direct | [ ] | Files transferred: |

## Status Panel Field Check (Step 3)

- [ ] Battery %: matches SDK's `GetBatteryAsync` result within ±1%
- [ ] MAC: matches BLE address in OS Bluetooth settings
- [ ] Hardware version: non-empty, matches logged value
- [ ] Firmware version: non-empty, matches logged value
- [ ] Photos count: _[initial]_
- [ ] Videos count: _[initial]_
- [ ] Audio count: _[initial]_

## Battery Widget Freshness Check (Steps 3–9)

- [ ] Steady-state update within 1 s of `BatteryUpdated` event
- [ ] Charging bolt appears within 1 s of placing glasses on cradle
- [ ] Low-battery red tint appears at ≤ 15% when not charging

## Integration Test Harness Results

### `HeyCyanFallbackTests` (Wave 4)

```
Command: dotnet test src/BodyCam.IntegrationTests --filter "FullyQualifiedName~HeyCyanFallbackTests"
Env: HEYCYAN_E2E=1
Status: [PASS | FAIL | SKIPPED]
Output: [paste test output here]
```

### `HeyCyanEndToEndTests` (Wave 5)

```
Command: dotnet test src/BodyCam.IntegrationTests --filter "FullyQualifiedName~HeyCyanEndToEndTests"
Env: HEYCYAN_E2E=1
Status: [PASS | FAIL | SKIPPED]
```

## Artifacts

Attach the following files to this test run:
- [ ] `console-ios.log` — full Console.app output during test run
- [ ] `screenshot-status-panel.png` — status panel after step 3
- [ ] `screenshot-shell-widget.png` — shell battery widget after step 3
- [ ] `device-info.json` — firmware/hardware/MAC from SDK

## Sign-off

**Result:** [PASS | FAIL | PARTIAL]

**Failures / deviations:**
_[describe any failures, blocked steps, or deviations from expected behavior]_

**Next steps:**
_[e.g., "Android checklist complete; iOS validation blocked by macOS build environment", etc.]_

---

**M33 Exit Criteria Mapping:**

| M33 exit criterion | Covered by step(s) |
|--------------------|-------------------|
| Photo via `HeyCyanCameraProvider` round-trips through `VisionAgent` | 6 |
| BT live mic + speaker route through glasses during a conversation | 4 |
| Glasses button (tap/double/long) triggers configured actions | 5, 6, 7 |
| Auto-fallback to phone camera + mic + speaker on disconnect | 8 + Wave 4 tests |
| Battery + firmware shown in status panel | 3 + Battery Widget Check |
| M17 exit criteria pass end-to-end against HeyCyan hardware | All steps 1–11 |
| (Optional) Recorded `.opus` voice notes import into M16 dictation | 11 |
