# Phase 3 - First UAT Suites

## Goal

Automate the first useful BodyCam acceptance suites using Brinell's existing
`.uat.md` format, `Brinell.Uat`, the BodyCam xUnit runtime bridge, and the
shared `BodyCam.UITestKit`.

## Suite 1 - Startup And Setup

Scenario candidates:

- UAT-001.1 App launches to a stable first screen.
- UAT-001.2 Setup can be opened when required settings are missing.
- UAT-001.3 API provider state is visible and recoverable.
- UAT-001.4 Debug panel is hidden by default but available.

Default mode: Automated.

## Suite 2 - Runtime State

Scenario candidates:

- UAT-002.1 App starts in Sleep.
- UAT-002.2 Sleep -> Listen -> Active state transition is visible.
- UAT-002.3 Active -> Sleep stops active session state.
- UAT-002.4 Speak and Silent modes update predictably.
- UAT-002.5 Transcript stays readable during state changes.

Default mode: Automated.

## Suite 3 - Camera Actions

Scenario candidates:

- UAT-003.1 Startup does not show camera sub-buttons.
- UAT-003.2 Opening camera action surface shows top-level actions.
- UAT-003.3 Tapping Look shows only Overview, Summary, Detail.
- UAT-003.4 Tapping Read shows only Read variants.
- UAT-003.5 Tapping Scan shows only Scan variants.
- UAT-003.6 Tapping a sub-button hides button rows immediately.
- UAT-003.7 Camera preview closes after capture settles.
- UAT-003.8 Captured still appears in transcript before command result.
- UAT-003.9 Capture failure is user-friendly and does not leak platform errors.

Default mode: Automated with deterministic camera and scripted AI.

## Suite 4 - Settings And Providers

Scenario candidates:

- UAT-004.1 Settings hub opens from main page.
- UAT-004.2 LLM provider settings can be viewed and changed.
- UAT-004.3 Camera source selection shows available deterministic providers.
- UAT-004.4 Microphone and speaker source settings are visible.
- UAT-004.5 Save/reset behavior is clear.

Default mode: Automated.

## Suite 5 - Audio Routing

Scenario candidates:

- UAT-005.1 Silent mode prevents normal speech output.
- UAT-005.2 Speak mode allows output through the test speaker.
- UAT-005.3 Output chunks are captured by the test speaker provider.
- UAT-005.4 Echo diagnostics can be opened without crashing.

Default mode: Semi-automated first, automated after test speaker assertions are
stable.

## Suite 6 - Hardware Optional

Scenario candidates:

- UAT-006.1 HeyCyan device can connect and expose camera/mic/speaker/buttons.
- UAT-006.2 A9/Vue990 can provide a frame.
- UAT-006.3 USB camera can be selected and validated.
- UAT-006.4 Bluetooth button gesture can trigger a mapped action.

Default mode: Hardware, opt-in only.

## Work Items

1. [x] Add `.uat.md` scenario files under `BodyCam.UAT/Scenarios`.
2. [x] Implement the BodyCam xUnit bridge for UAT-001, UAT-002, and UAT-003 first.
3. [x] Map each scenario to a UAT id using scenario title and tags, for example
   `UAT-003.6` plus `@uat-003-6`.
4. [x] Capture screenshot/log evidence on failure.
5. [x] Keep hardware/live API scenarios skipped unless explicitly enabled.

## Exit Criteria

- [x] UAT-001 automated scenarios pass.
- [x] UAT-002 automated scenarios pass.
- [x] UAT-003 automated scenarios pass, including the M50 button hiding flow.
- [x] UAT-004 has at least settings navigation automation.
- [x] UAT-005 and UAT-006 are documented and tagged even if not fully automated.

## Implementation Notes

- The camera suite now distinguishes the quick action drawer from the inline
  camera action rail and uses the normalized `camera_*` automation IDs.
- `@live-api`, `@semi-automated`, and `@hardware` scenarios remain skipped by
  default through `uat.config.md` skip rules.
- The BodyCam UAT runtime captures a screenshot into the configured report
  folder when a scenario fails.
