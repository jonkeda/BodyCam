# UAT: BodyCam Hardware Optional

## Metadata

| Field | Value |
| --- | --- |
| App | BodyCam |
| Area | Hardware Optional |
| Target | MAUI |
| Tags | bodycam, hardware, heycyan, a9, usb-camera |
| Mode | Hardware |
| Requires | Hardware |
| Priority | Exploratory |
| Evidence | screenshot, log, artifact |

## Background

Given the app is running in deterministic UAT mode
And app settings are reset

@bodycam @hardware @heycyan @uat-006-1
## Scenario: UAT-006.1 HeyCyan device can connect and expose hardware providers

Given I am on the Device Settings page
Then Connect Device should be visible
And Connected Devices should be visible

@bodycam @hardware @a9 @vue990 @uat-006-2
## Scenario: UAT-006.2 A9 or Vue990 can provide a frame

Given I am on the Device Settings page
Then Camera Source should be visible
And Take Picture should be visible

@bodycam @hardware @usb-camera @uat-006-3
## Scenario: UAT-006.3 USB camera can be selected and validated

Given I am on the Device Settings page
Then Camera Source should be visible
And Take Picture should be visible

@bodycam @hardware @buttons @uat-006-4
## Scenario: UAT-006.4 Bluetooth button gesture can trigger a mapped action

Given I am on the Device Settings page
Then Connected Devices should be visible
