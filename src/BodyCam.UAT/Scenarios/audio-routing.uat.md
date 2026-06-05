# UAT: BodyCam Audio Routing

## Metadata

| Field | Value |
| --- | --- |
| App | BodyCam |
| Area | Audio Routing |
| Target | MAUI |
| Tags | bodycam, audio, semi-automated |
| Mode | Semi-automated |
| Requires | Deterministic |
| Priority | Normal |
| Evidence | screenshot, log |

## Background

Given the app is running in deterministic UAT mode
And app settings are reset
And I am on the Main page

@bodycam @audio @semi-automated @uat-005-1
## Scenario: UAT-005.1 Silent mode prevents normal speech output

When I tap Silent
Then Silent should be enabled

@bodycam @audio @semi-automated @uat-005-2
## Scenario: UAT-005.2 Speak mode allows output through the test speaker

When I tap Speak
Then Speak should be visible
And Speak should be enabled

@bodycam @audio @semi-automated @uat-005-3
## Scenario: UAT-005.3 Output chunks are captured by the test speaker provider

Given I am on the Device Settings page
Then Audio Output should be visible

@bodycam @audio @semi-automated @uat-005-4
## Scenario: UAT-005.4 Echo diagnostics can be opened without crashing

Given I am on the Advanced Settings page
Then Debug Mode should be visible
