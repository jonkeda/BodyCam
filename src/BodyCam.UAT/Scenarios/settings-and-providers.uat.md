# UAT: BodyCam Settings And Providers

## Metadata

| Field | Value |
| --- | --- |
| App | BodyCam |
| Area | Settings And Providers |
| Target | MAUI |
| Tags | bodycam, settings, providers |
| Mode | Automated |
| Requires | Deterministic |
| Priority | Normal |
| Evidence | screenshot |

## Background

Given the app is running in deterministic UAT mode
And app settings are reset

@bodycam @settings @automated @deterministic @uat-004-1
## Scenario: UAT-004.1 Settings hub opens from main page

Given I am on the Main page
When I tap Settings
Then I should be on the Settings page
And Connection Settings should be visible
And Voice Settings should be visible
And Device Settings should be visible
And Advanced Settings should be visible

@bodycam @settings @providers @automated @deterministic @uat-004-2
## Scenario: UAT-004.2 Provider settings page can be opened

Given I am on the Settings page
When I tap Connection Settings
Then I should be on the Llm Providers Settings page
And Add Llm Provider should be visible

@bodycam @settings @devices @automated @deterministic @uat-004-3
## Scenario: UAT-004.3 Camera source selection shows deterministic providers

Given I am on the Device Settings page
Then Source Profile should be visible
And Camera Source should be visible
And Audio Input should be visible
And Audio Output should be visible

@bodycam @settings @audio @automated @deterministic @uat-004-4
## Scenario: UAT-004.4 Microphone, speaker, and voice settings are visible

Given I am on the Voice Settings page
Then Voice should be visible
And Turn Detection should be visible
And Noise Reduction should be visible

@bodycam @settings @advanced @automated @deterministic @uat-004-5
## Scenario: UAT-004.5 Save and reset related settings remain discoverable

Given I am on the Advanced Settings page
Then Debug Mode should be visible
And Send Diagnostic Data should be visible
And Send Crash Reports should be visible
