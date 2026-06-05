# UAT: BodyCam Runtime State

## Metadata

| Field | Value |
| --- | --- |
| App | BodyCam |
| Area | Runtime State |
| Target | MAUI |
| Tags | bodycam, runtime, smoke |
| Mode | Automated |
| Requires | Deterministic |
| Priority | Smoke |
| Evidence | screenshot |

## Background

Given the app is running in deterministic UAT mode
And app settings are reset
And I am on the Main page

@bodycam @runtime @smoke @automated @deterministic @uat-002-1
## Scenario: UAT-002.1 App starts with stable runtime controls

Then Sleep should be visible
And Listen should be visible
And Active should be visible
And Speak should be visible
And Silent should be visible
And Transcript should be visible
And Camera Preview Panel should not be visible

@bodycam @runtime @automated @deterministic @uat-002-2
## Scenario: UAT-002.2 Sleep and Listen state controls can be selected

When I tap Listen
Then Listen should be enabled
When I tap Sleep
Then Sleep should be enabled

@bodycam @runtime @live-api @uat-002-3
## Scenario: UAT-002.3 Active session can be started and stopped when realtime is enabled

When I tap Active
Then Active should be enabled
When I tap Sleep
Then Sleep should be enabled

@bodycam @runtime @automated @deterministic @uat-002-4
## Scenario: UAT-002.4 Speak and Silent modes update predictably

When I tap Speak
Then Speak should be enabled
When I tap Silent
Then Silent should be enabled

@bodycam @runtime @automated @deterministic @uat-002-5
## Scenario: UAT-002.5 Transcript stays readable during state changes

Then Transcript should be visible
When I tap Listen
Then Transcript should be visible
When I tap Sleep
And I tap Silent
Then Transcript should be visible
