# Phase 4 - Runtime Bridge, CI, And Reporting

## Goal

Make UAT runs repeatable and reportable using the existing Brinell UAT runtime
without forcing hardware or live API dependencies into normal CI. The BodyCam
work in this phase is an xUnit bridge around `Brinell.Uat`, not a new UAT
runner.

## Runtime Bridge

Mirror the existing `Brinell.Maui.Uat.Tests` flow:

Keep this bridge thin; it should adapt BodyCam fixtures to Brinell UAT, not
replace Brinell UAT.

1. Discover `Scenarios/**/*.uat.md`.
2. Parse each file with `UatMarkdownParser.ParseFile`.
3. Create a command catalog with Brinell's reflection runtime.
4. Bind scenarios with `UatBinder.Bind`.
5. Execute with `UatScenarioRunner.RunAsync`.
6. Add BodyCam-specific skip/report/evidence handling around the shared runner.

## Test Categories

Use xUnit traits on the BodyCam UAT bridge consistently:

```csharp
[Trait("Suite", "UAT-003")]
[Trait("UatId", "UAT-003.6")]
[Trait("Mode", "Automated")]
[Trait("Requires", "Deterministic")]
```

For opt-in runs:

```csharp
[Trait("Mode", "Hardware")]
[Trait("Requires", "HeyCyan")]
```

## Commands

Default automated UAT:

```powershell
dotnet test src\BodyCam.UAT\BodyCam.UAT.csproj --filter "Mode=Automated"
```

Hardware UAT:

```powershell
$env:BODYCAM_UAT_HARDWARE="1"
dotnet test src\BodyCam.UAT\BodyCam.UAT.csproj --filter "Mode=Hardware"
```

Live API UAT:

```powershell
$env:BODYCAM_UAT_LIVE_API="1"
dotnet test src\BodyCam.UAT\BodyCam.UAT.csproj --filter "Mode=Live API"
```

## Reporting

Each run should write:

```text
artifacts/uat/{timestamp}/
  uat-summary.md
  uat-summary.json
  screenshots/
  logs/
  trx/
```

Summary should include:

- suite id
- scenario id
- title
- mode
- status: passed, failed, skipped, manual, blocked
- evidence links
- failure message
- app version/build
- git commit

For the first pass, the xUnit bridge can produce this report by reading scenario
metadata and Brinell runner results. If this becomes generally useful, propose
it as a Brinell UAT reporting enhancement.

## CI Plan

Windows CI default:

1. Build BodyCam.
2. Build `BodyCam.UITestKit`.
3. Build `BodyCam.UAT`.
4. Run `Mode=Automated`.
5. Publish TRX and UAT summary artifacts.

Manual opt-in jobs:

- hardware UAT
- live API UAT
- full Brinell UI smoke

## Failure Policy

- Automated deterministic UAT failures block release.
- Skipped hardware/live API scenarios do not block default CI.
- Manual scenarios must be reviewed before a tagged UAT release.
- Any raw exception visible in the user transcript is a UAT failure.

## Exit Criteria

- [ ] Default UAT command runs without hardware.
- [ ] BodyCam UAT execution uses Brinell's parser, binder, and scenario runner.
- [ ] CI publishes readable UAT artifacts.
- [ ] Hardware/live API scenarios are opt-in.
- [ ] A release can point to a UAT report for sign-off.
