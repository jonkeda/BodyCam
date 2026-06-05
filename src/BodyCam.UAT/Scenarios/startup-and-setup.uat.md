# UAT: BodyCam Startup And Setup

## Metadata

| Field | Value |
| --- | --- |
| App | BodyCam |
| Area | Startup And Setup |
| Target | MAUI |
| Tags | bodycam, startup, smoke |
| Mode | Automated |
| Requires | Deterministic |
| Priority | Smoke |
| Evidence | screenshot |

## Background

Given the app is running in deterministic UAT mode
And app settings are reset

@bodycam @startup @smoke @automated @deterministic @uat-001-1
## Scenario: UAT-001.1 Main page is visible after startup

Given I am on the Main page
Then Transcript should be visible
And Message should be enabled
And Camera Preview Panel should not be visible

@bodycam @startup @settings @automated @deterministic @uat-001-2
## Scenario: UAT-001.2 Settings can be opened from startup

Given I am on the Main page
When I tap Settings
Then I should be on the Settings page
And Connection Settings should be visible

@bodycam @startup @providers @automated @deterministic @uat-001-3
## Scenario: UAT-001.3 API provider state is visible and recoverable

Given I am on the Llm Providers Settings page
Then Add Llm Provider should be visible
And Edit Open Ai Provider should be visible

@bodycam @startup @debug @automated @deterministic @uat-001-4
## Scenario: UAT-001.4 Debug panel is hidden by default but available

Given I am on the Main page
Then Debug should not be visible
When I tap Settings
And I tap Advanced Settings
Then I should be on the Advanced Settings page
And Debug Mode should be visible
