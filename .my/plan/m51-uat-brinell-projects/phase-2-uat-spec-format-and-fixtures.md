# Phase 2 - Brinell UAT Format And Fixtures

## Goal

Use the existing `Brinell.Uat` Markdown format and runtime bridge pattern for
BodyCam UAT. Do not create a BodyCam-specific UAT grammar, parser, binder, or
scenario runner.

## Existing Brinell Format

BodyCam UAT files must follow the current Brinell `.uat.md` shape:

```markdown
# UAT: BodyCam Camera Actions

## Metadata

| Field | Value |
| --- | --- |
| App | BodyCam |
| Area | Camera Actions |
| Target | MAUI |
| Tags | smoke, maui, camera, uat-003 |
| Mode | Automated |
| Requires | Deterministic |

@smoke @maui @camera @uat-003 @automated @deterministic
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

Supported Brinell sections:

- `# UAT: ...`
- `## Metadata`
- `## Background`
- `## Data: ...`
- `## Scenario: ...`
- `## Scenario Outline: ...`
- `### Examples`
- scenario tags immediately before `## Scenario`
- `Given`, `When`, `Then`, `And`, `But`

## BodyCam Naming Convention

Use the existing Brinell grammar and add BodyCam semantics through metadata,
tags, and scenario names:

- Scenario id in title: `UAT-003.6 Sub-button hides action rows during capture`
- Machine tag: `@uat-003-6`
- Suite tag: `@camera-actions`
- Mode tag: `@automated`, `@manual`, `@hardware`, or `@live-api`
- Requirement tag: `@deterministic`, `@heycyan`, `@a9`, `@usb-camera`, or
  `@openai-live`

## BodyCam `uat.config.md`

Use Brinell's existing `uat.config.md` format:

```markdown
# UAT Config

## Runtime

| Field | Value |
| --- | --- |
| Target | MAUI |
| Fixture | Appium |
| AppPath | ../BodyCam/bin/Debug/net10.0-windows10.0.19041.0/win-x64/BodyCam.exe |
| WorkingDirectory | ../.. |

## Assemblies

| Kind | Assembly |
| --- | --- |
| Pages | BodyCam.UITestKit.dll |
| Controls | Brinell.Maui.dll |
| Commands | Brinell.Uat.dll |

## Discovery

| Field | Value |
| --- | --- |
| RequireExplicitUatAttributes | false |
| AllowNameInference | true |
```

Do not add BodyCam-only config parsing for the first pass.

## Deterministic Fixture Requirements

UAT should run with deterministic test mode by default:

| Provider | UAT behavior |
| --- | --- |
| Camera | returns known JPEG frames |
| Microphone | emits no audio unless a test injects PCM |
| Speaker | captures played chunks and exposes counters |
| Buttons | allows simulated gestures |
| AI provider | returns scripted text/audio unless tagged `@live-api` |
| Settings | starts from a reset profile per test run |

Use environment variables rather than production settings:

```text
BODYCAM_TEST_MODE=uat
BODYCAM_UAT_ASSETS=...
BODYCAM_UAT_REPORTS=...
BODYCAM_UAT_LIVE_API=0
BODYCAM_UAT_HARDWARE=0
```

## Proposed Brinell Template Additions

Keep BodyCam on the existing format. Proposed improvements to the shared
Brinell template are documented separately in
[Brinell UAT Template Additions](brinell-uat-template-additions.md).

## Work Items

1. [x] Create BodyCam UAT files using Brinell `.uat.md` syntax.
2. [x] Create `uat.config.md` using Brinell's current Runtime/Assemblies/Discovery
   sections.
3. [x] Use `Brinell.Uat.UatMarkdownParser`, `UatBinder`, and `UatScenarioRunner`.
4. [x] Add BodyCam-specific UAT commands via page/control objects and
   `UatPhraseAttribute` only where generic commands are not enough.
5. [x] Add deterministic provider setup to the BodyCam UAT fixture.
6. [x] Document proposed Brinell template additions before changing Brinell itself.

## Implementation Notes

- `BodyCam.UAT` uses Brinell's Markdown parser, binder, config, and scenario
  runner directly.
- `BodyCam.UAT` has spec-format tests that parse every `.uat.md`, verify
  required metadata, bind through a Brinell command catalog, and parse
  `uat.config.md`.
- `BodyCam.UITestKit` exposes UAT-friendly names and a reset hook on shared page
  objects/fixtures instead of duplicating UI test code.
- `BodyCam` supports `BODYCAM_TEST_MODE=uat` with deterministic camera, silent
  microphone, capturing speaker, simulated buttons, seeded settings, and a
  scripted chat/vision client. Live realtime is blocked in deterministic mode
  unless `BODYCAM_UAT_LIVE_API` is enabled.

## Exit Criteria

- [x] BodyCam UAT files parse with `UatMarkdownParser`.
- [x] BodyCam UAT scenarios bind through `UatBinder`.
- [x] No BodyCam-specific Markdown parser, binder, or scenario runner exists.
- [x] Automated/semi/manual/live/hardware status is represented by metadata and
  tags.
- [x] Test mode can reset app state between scenarios.
- [x] Any proposed Brinell template changes are documented separately before
  implementation.

## Verification

- `dotnet build src\BodyCam\BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore`
- `dotnet build src\BodyCam.UAT\BodyCam.UAT.csproj --no-restore`
- `dotnet test src\BodyCam.UAT\BodyCam.UAT.csproj --filter "Layer=SpecFormat" --no-build`
- `dotnet test src\BodyCam.UAT\BodyCam.UAT.csproj --list-tests --no-build`
- `dotnet test src\BodyCam.Tests\BodyCam.Tests.csproj --filter "FullyQualifiedName~UatTestModeTests" -p:SkipBuildNumberIncrement=true`
