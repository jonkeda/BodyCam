# Brinell UAT Template Additions

## Goal

Keep BodyCam on the existing Brinell `.uat.md` format while proposing small
shared-template additions that make acceptance suites easier to review, filter,
skip, and report.

These are Brinell template proposals, not requirements for a BodyCam-specific
UAT parser or runner.

**Implementation status:** Implemented in `Brinell.Uat` by adding standard
metadata/tag constants, parsed `Reporting` settings, parsed `Skip Rules`,
`UatConfig.EvaluateSkip`, and a config-aware `UatScenarioRunner.RunAsync`
overload. The Brinell docs now include the UAT template guide.

## Current Template Baseline

The existing Brinell UAT format already supports the pieces BodyCam needs:

```markdown
# UAT: MAUI Main Page Greeting

## Metadata

| Field | Value |
| --- | --- |
| App | Brinell.Samples.Maui.App |
| Area | Main Page |
| Target | MAUI |
| Tags | smoke, maui, greeting |

@smoke @maui @greeting
## Scenario: Greeting appears when a name is entered

Given I am on the Main page
When I clear Name
And I enter "Alice" into Name
And I tap Greet
Then Greeting should contain "Hello, Alice!"
And Greeting should be visible
And Name should be enabled
```

Supported building blocks:

- `# UAT: ...`
- `## Metadata`
- optional `## Background`
- optional `## Data: ...`
- `## Scenario: ...`
- `## Scenario Outline: ...`
- `### Examples`
- tag lines immediately before a scenario
- `Given`, `When`, `Then`, `And`, `But`

## Addition 1 - Standard Metadata Fields

Add these optional metadata fields to the shared template:

| Field | Purpose |
| --- | --- |
| Mode | Automated, Semi-automated, Manual, Hardware, Live API |
| Requires | Deterministic, Hardware, Live API, HeyCyan, A9, USB Camera |
| Owner | Person/team responsible for sign-off |
| Priority | Smoke, Critical, Normal, Exploratory |
| Evidence | Expected evidence type: screenshot, transcript, log, artifact |

The current parser already accepts arbitrary metadata fields, so this is a
documentation/template addition unless reporting needs stronger validation.

## Addition 2 - Standard Tag Vocabulary

Document common tags:

```text
@smoke @regression @manual @hardware @live-api
@maui @windows @android @ios
@deterministic @openai-live
@uat-003 @uat-003-6
```

The current parser already supports tags.

## Addition 3 - Scenario Id Convention

Use one of these consistently:

```markdown
## Scenario: UAT-003.6 Sub-button hides action rows during capture
```

or:

```markdown
@uat-003-6
## Scenario: Sub-button hides action rows during capture
```

For BodyCam, prefer both title id and tag until reports can display tags
cleanly.

## Addition 4 - Optional Background Reset Pattern

Document a common reset pattern for deterministic UAT:

```markdown
## Background

Given the app is running in deterministic UAT mode
And app settings are reset
And the transcript is empty
```

This stays within the existing grammar. BodyCam only needs fixture commands that
can bind these phrases.

## Addition 5 - Optional Data Tables

Use existing `## Data:` tables for assets and provider setups:

```markdown
## Data: CameraFrames

| Name | Asset | Description |
| --- | --- | --- |
| Office | assets/camera/office.jpg | Indoor person-facing frame |
```

This gives specs a readable place to name deterministic inputs without adding a
new syntax.

## Addition 6 - Optional Config Sections

If Brinell wants richer reporting later, add optional `uat.config.md` sections:

```markdown
## Reporting

| Field | Value |
| --- | --- |
| OutputDirectory | artifacts/uat |
| ScreenshotOnFailure | true |
| IncludeRuntimeTrace | true |

## Skip Rules

| Tag | EnvironmentVariable |
| --- | --- |
| hardware | BODYCAM_UAT_HARDWARE |
| live-api | BODYCAM_UAT_LIVE_API |
```

This is now supported by `UatConfigParser`, `UatConfig.EvaluateSkip`, and the
config-aware `UatScenarioRunner.RunAsync` overload. UAT bridges can use the
shared skipped scenario result directly or map it to host framework skip output.

## Example BodyCam Scenario

```markdown
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
| Evidence | screenshot, transcript |

@bodycam @camera @m50 @automated @deterministic @uat-003-6
## Scenario: UAT-003.6 Sub-button hides action rows during capture

Given I am on the Main page
When I tap Look
Then Overview should be visible
And Summary should be visible
And Detail should be visible
When I tap Overview
Then Camera Action Rail should not be visible
And Camera Action Variant Rail should not be visible
And Transcript should contain a captured frame
```
