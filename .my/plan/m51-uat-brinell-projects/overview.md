# M51 - BodyCam Brinell UAT Projects

**Status:** Planning
**Goal:** Create UAT-focused Brinell test projects for BodyCam using the
existing `Brinell.Uat` Markdown format and runtime bridge pattern, so
product-level acceptance scenarios can be written, reviewed, automated where
possible, and reported separately from lower-level regression tests.

**Depends on:** M15 Brinell Test Extensions, existing `BodyCam.UITests`, M44
Command Redesign, M50 Camera Buttons.

## Why This Matters

BodyCam already has unit, integration, real API, and code-first UI tests. Those
are good for engineering confidence, but they are not a clean UAT surface.

UAT needs a different shape:

- readable scenarios tied to user-visible behavior
- clear manual vs automated status
- stable Brinell page/control objects
- deterministic test-mode providers for camera, audio, buttons, and AI
- reports that can be used for sign-off

This milestone creates that UAT layer without replacing the existing test
projects.

## Target Project Layout

Start with two project-level changes and reuse the current Brinell UAT project
shape from `Brinell.Maui.Uat.Tests`:

```text
src/
  BodyCam.UITestKit/
    BodyCam.UITestKit.csproj
    Pages/
    Controls/
    Fixtures/
    Assertions/

  BodyCam.UITests/
    BodyCam.UITests.csproj
    Tests/

  BodyCam.UAT/
    BodyCam.UAT.csproj
    uat.config.md
    Scenarios/
      startup-and-setup.uat.md
      runtime-state.uat.md
      camera-actions.uat.md
      settings-and-providers.uat.md
      audio-routing.uat.md
      hardware-optional.uat.md
    Runtime/
      BodyCamUatCollection.cs
      BodyCamUatRuntime.cs
      BodyCamUatScenarioSource.cs
      BodyCamUatScenarioTests.cs
    Reports/
```

`BodyCam.UITestKit` is the shared Brinell automation layer. It should own page
objects, reusable controls, launch helpers, waits, screenshots, and common
assertions.

`BodyCam.UITests` remains the code-first regression project. It should reference
`BodyCam.UITestKit` after page objects are extracted.

`BodyCam.UAT` is the acceptance project. It uses the existing Brinell UAT
Markdown grammar:

- `# UAT: ...`
- `## Metadata`
- optional `## Background`
- optional `## Data: ...`
- scenario tags such as `@smoke @maui @uat-003`
- `## Scenario: ...`
- `## Scenario Outline: ...`
- `Given`, `When`, `Then`, `And`, `But`

Do not create a BodyCam-specific UAT parser, binder, or scenario runner. If a
real gap is identified, propose it for `Brinell.Uat` first and keep BodyCam on
the shared format.

## UAT Principles

- UAT scenarios describe product behavior, not implementation details.
- Every UAT item has an owner, status, automation level, and evidence.
- Automated UAT uses deterministic providers by default.
- Hardware and live API scenarios are explicitly tagged and skipped in normal CI.
- Brinell page objects live in one shared place.
- UAT reports should be readable by a non-developer.

## Automation Levels

| Level | Meaning | Example |
| --- | --- | --- |
| Automated | Runs in CI with deterministic providers | Camera action buttons hide after sub-button click |
| Semi-automated | Brinell drives UI, user/device confirms result | Bluetooth route selection with real glasses |
| Manual | Human-only sign-off | Real-world outdoor scene quality |
| Live API | Requires OpenAI/Azure key and network | Vision response quality with real model |
| Hardware | Requires a named device | HeyCyan, A9, USB camera |

## Initial UAT Suites

| UAT | Suite | Default mode | Purpose |
| --- | --- | --- | --- |
| UAT-001 | Startup And Setup | Automated | App launches, setup can be reached, permissions/API key states are legible. |
| UAT-002 | Runtime State | Automated | Sleep, Listen, Active, Speak, Silent, and transcript state behave predictably. |
| UAT-003 | Camera Actions | Automated | M50 camera action flow: top-level actions, sub-buttons, capture, transcript, close. |
| UAT-004 | Settings And Providers | Automated | Settings navigation, LLM provider settings, camera/mic/speaker choices. |
| UAT-005 | Audio Routing | Semi-automated | Speak/Silent, output route, mic route, echo diagnostics. |
| UAT-006 | Hardware Optional | Hardware | HeyCyan, A9/Vue990, USB camera, Bluetooth buttons. |

## Phases

1. [Project Layout And Extraction](phase-1-project-layout-and-extraction.md)
2. [UAT Spec Format And Fixtures](phase-2-uat-spec-format-and-fixtures.md)
3. [First UAT Suites](phase-3-first-uat-suites.md)
4. [Runtime Bridge, CI, And Reporting](phase-4-runtime-bridge-ci-and-reporting.md)

Template proposal: [Brinell UAT Template Additions](brinell-uat-template-additions.md)

## Exit Criteria

- [ ] `BodyCam.UITestKit` exists and contains shared Brinell page objects.
- [ ] `BodyCam.UITests` uses `BodyCam.UITestKit` instead of owning all page objects.
- [ ] `BodyCam.UAT` exists as a separate xUnit/Brinell acceptance project.
- [ ] UAT Markdown files exist for the six initial suites using Brinell's
  existing `.uat.md` format.
- [ ] BodyCam-specific UAT needs are documented as Brinell template additions,
  not as a forked BodyCam format.
- [ ] At least UAT-001, UAT-002, and UAT-003 have automated coverage.
- [ ] UAT runs can produce a readable report with pass/fail/skipped/manual status.
- [ ] Normal CI runs deterministic automated UAT only.
- [ ] Hardware/live API UAT is tagged and opt-in.

## Out Of Scope

- Replacing unit tests, integration tests, or `BodyCam.UITests`.
- Running real hardware UAT in default CI.
- Building a BodyCam-specific Markdown UAT parser or runner.
- Moving Brinell source into BodyCam.
