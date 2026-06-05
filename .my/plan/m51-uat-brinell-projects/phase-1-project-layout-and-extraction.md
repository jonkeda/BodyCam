# Phase 1 - Project Layout And Extraction

## Goal

Create the project structure for BodyCam UAT without breaking the existing
`BodyCam.UITests` project.

## Work Items

1. Create `src/BodyCam.UITestKit`.
2. Move reusable Brinell page objects from `BodyCam.UITests/Pages` into the kit.
3. Move reusable fixture helpers, app launch settings, waits, and shared
   assertions into the kit.
4. Keep actual code-first UI tests in `BodyCam.UITests/Tests`.
5. Update `BodyCam.UITests.csproj` to reference `BodyCam.UITestKit`.
6. Create `src/BodyCam.UAT` as a separate xUnit project modeled directly after
   `Brinell/testsnew/Brinell.Maui.Uat.Tests`.
7. Reference `Brinell.Uat`, Brinell MAUI runtime projects, and
   `BodyCam.UITestKit` from `BodyCam.UAT`.
8. Add `uat.config.md`, `Scenarios/`, `Runtime/`, and `Reports/` folders to
   `BodyCam.UAT`.
9. Add BodyCam runtime bridge classes that call Brinell's
   `UatMarkdownParser`, `UatBinder`, `UatReflectionRuntime`, and
   `UatScenarioRunner`.
10. Copy `uat.config.md` and `Scenarios/**/*.uat.md` to the test output.

## Proposed References

`BodyCam.UITestKit`:

- `Brinell.Core`
- `Brinell.Maui`
- `Brinell.Maui.FlaUI`
- xUnit only if shared fixture base types require it

`BodyCam.UAT`:

- `BodyCam.UITestKit`
- `Brinell.Uat`
- `Brinell.Core`
- `Brinell.Maui`
- `Brinell.Maui.FlaUI`
- xUnit
- FluentAssertions
- Xunit.SkippableFact

## Migration Rule

Move page objects gradually:

1. Extract one page object at a time.
2. Build `BodyCam.UITests`.
3. Run a small UI smoke filter.
4. Continue only after the moved page object is stable.

Avoid a big-bang page object move.

## Deliverables

- [x] `src/BodyCam.UITestKit/BodyCam.UITestKit.csproj`
- [x] `src/BodyCam.UAT/BodyCam.UAT.csproj`
- [x] `src/BodyCam.UAT/uat.config.md`
- [x] `src/BodyCam.UAT/Runtime/BodyCamUatRuntime.cs`
- [x] `src/BodyCam.UAT/Runtime/BodyCamUatScenarioTests.cs`
- [x] updated `BodyCam.UITests.csproj`
- [x] at least `MainPage`, `SettingsPage`, and camera-related page objects available
  from the shared kit

## Implementation Notes

- `BodyCam.UITestKit` now owns the shared `BodyCamFixture`, page objects,
  launch defaults, navigation helpers, and `TestConstants`.
- `BodyCam.UITests` keeps only code-first tests plus its xUnit collection marker.
- `BodyCam.UAT` uses Brinell's existing `UatMarkdownParser`, `UatBinder`,
  `UatReflectionRuntime`, and `UatScenarioRunner`.
- Initial UAT scenario file: `Scenarios/startup-and-setup.uat.md`.
- The documented `Category=Smoke` verification currently discovers no matching
  UI tests because the existing UI test suite uses `Category=UITest` and
  `Category=RealHardware`.

## Verification

```powershell
dotnet build src\BodyCam.UITestKit\BodyCam.UITestKit.csproj
dotnet test src\BodyCam.UITests\BodyCam.UITests.csproj --filter "Category=Smoke"
dotnet test src\BodyCam.UAT\BodyCam.UAT.csproj --list-tests
```

## Exit Criteria

- [x] Existing UI tests still compile.
- [x] New UAT project compiles.
- [x] Shared page objects can be used from both `BodyCam.UITests` and
  `BodyCam.UAT`.
- [x] `BodyCam.UAT` uses Brinell's parser, binder, reflection runtime, and
  scenario runner.
- [x] No production app code changes are required for this phase.
