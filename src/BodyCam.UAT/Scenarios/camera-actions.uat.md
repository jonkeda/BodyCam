# UAT: BodyCam Camera Actions

## Metadata

| Field | Value |
| --- | --- |
| App | BodyCam |
| Area | Camera Actions |
| Target | MAUI |
| Tags | bodycam, camera, m50 |
| Mode | Automated |
| Requires | Deterministic |
| Priority | Critical |
| Evidence | screenshot, transcript |

## Background

Given the app is running in deterministic UAT mode
And app settings are reset
And I am on the Main page

@bodycam @camera @m50 @startup @automated @deterministic @uat-003-1
## Scenario: UAT-003.1 Startup does not show camera sub-buttons

Then Camera Preview Panel should not be visible
And Camera Action Variant Rail should not be visible
And camera action variants should not be visible

@bodycam @camera @m50 @automated @deterministic @uat-003-2
## Scenario: UAT-003.2 Opening camera action surface shows top-level actions

Given the camera action surface is open
Then Actions Drawer should be visible
And Look should be visible
And Find should be visible
And Read should be visible
And Scan should be visible
And camera action variants should not be visible

@bodycam @camera @m50 @automated @deterministic @uat-003-3
## Scenario: UAT-003.3 Tapping Look shows only Overview, Summary, Detail

Given the camera action surface is open
When I tap Look
Then Actions Drawer should not be visible
And camera action top-level buttons should not be visible
Then Camera Action Variant Rail should be visible
And Look Overview should be visible
And Look Summary should be visible
And Look Detail should be visible
And Find Overview should not be visible
And Read Summary should not be visible
And Scan Default should not be visible

@bodycam @camera @m50 @automated @deterministic @uat-003-4
## Scenario: UAT-003.4 Tapping Find shows only Overview, Summary, Detail

Given the camera action surface is open
When I tap Find
Then Actions Drawer should not be visible
And camera action top-level buttons should not be visible
Then Camera Action Variant Rail should be visible
And Find Overview should be visible
And Find Summary should be visible
And Find Detail should be visible
And Look Overview should not be visible
And Read Summary should not be visible
And Scan Default should not be visible

@bodycam @camera @m50 @automated @deterministic @uat-003-5
## Scenario: UAT-003.5 Tapping Read shows only Read variants

Given the camera action surface is open
When I tap Read
Then Actions Drawer should not be visible
And camera action top-level buttons should not be visible
Then Camera Action Variant Rail should be visible
And Read Summary should be visible
And Read Overview should be visible
And Read Full should be visible
And Look Overview should not be visible
And Find Overview should not be visible
And Scan Default should not be visible

@bodycam @camera @m50 @automated @deterministic @uat-003-6
## Scenario: UAT-003.6 Tapping Scan shows only Scan variants

Given the camera action surface is open
When I tap Scan
Then Actions Drawer should not be visible
And camera action top-level buttons should not be visible
Then Camera Action Variant Rail should be visible
And Scan Default should be visible
And Look Overview should not be visible
And Find Overview should not be visible
And Read Summary should not be visible

@bodycam @camera @m50 @automated @deterministic @uat-003-7
## Scenario: UAT-003.7 Tapping a sub-button hides button rows immediately

Given the camera action surface is open
When I tap Look
And I tap Look Overview
Then Actions Drawer should not be visible
And camera action top-level buttons should not be visible
And camera action variants should not be visible

@bodycam @camera @m50 @automated @deterministic @uat-003-8
## Scenario: UAT-003.8 Camera preview closes and captured still appears after capture settles

Given the camera action surface is open
When I tap Look
And I tap Look Overview
And I wait for camera command to settle
Then Camera Preview Panel should not be visible
And the captured still should appear in the transcript
And the deterministic camera response should appear in the transcript

@bodycam @camera @m50 @automated @deterministic @uat-003-9
## Scenario: UAT-003.9 Camera command result is user-friendly and does not leak platform errors

Given the camera action surface is open
When I tap Look
And I tap Look Overview
And I wait for camera command to settle
Then the deterministic camera response should appear in the transcript
And transcript should not contain PlatformView cannot be null here
And transcript should not contain Camera capture failed.
And transcript should not contain Command error:
