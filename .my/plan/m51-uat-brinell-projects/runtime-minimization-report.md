# Runtime Minimization Report

## Goal

Make each Brinell-format UAT project keep only app-specific runtime code:

- fixture and xUnit collection
- app environment constants
- app-specific `[UatPhrase]` methods on the runtime fixture
- tiny scenario/spec test classes that delegate orchestration to Brinell

The repeated parse, bind, runtime, execute, diagnostics, and scenario discovery
flow belongs in Brinell, not in every UAT project.

## Implemented State

This pass moved the shared UAT test orchestration into `Brinell.Uat` and removed
the per-project scenario source wrappers.

Added to `Brinell.Uat`:

| File | Responsibility |
| --- | --- |
| `UatScenarioTestBase<TFixture>.cs` | Parse, bind, create runtime/catalog, execute scenarios, capture failure evidence through `IScreenshotService`, format failures, expected-failure diagnostics, and expose shared scenario file discovery. |
| `UatSpecFormatTestBase.cs` | Parse `.uat.md` files, enforce required metadata, bind specs through a catalog, parse `uat.config.md`, discover `[UatPhrase]` methods from an optional runtime root type, and allow project-specific config assertions. |
| `UatReflectionRuntime.RegisterRootPhrases(...)` | Adds attributed root phrases to a spec catalog from a runtime root type without constructing the UI fixture. |

Added direct coverage in `Brinell.Uat.Tests`:

| File | Coverage |
| --- | --- |
| `UatTestBaseTests.cs` | Verifies scenario base execution hooks, expected-failure diagnostics/evidence capture, and spec-format base metadata/bind/config behavior. |

Removed from project runtimes:

| Removed file | Replacement |
| --- | --- |
| `src/BodyCam.UAT/Runtime/BodyCamUatScenarioSource.cs` | `UatScenarioTestBase.GetScenarioFiles(...)` / `UatSpecFormatTestBase.GetScenarioFiles(...)` |
| `src/BodyCam.UAT/Runtime/BodyCamUatPhrases.cs` | `[UatPhrase]` attributes on `BodyCamUatFixture`, discovered through `RuntimeRootType`. |
| `Brinell/testsnew/Brinell.Maui.Uat.Tests/Runtime/MauiUatScenarioSource.cs` | `UatScenarioTestBase.GetScenarioFiles(...)` |

BodyCam now keeps only project-specific UAT runtime pieces:

| File | Why it remains |
| --- | --- |
| `BodyCamUatCollection.cs` | xUnit shared fixture collection. |
| `BodyCamUatEnvironment.cs` | BodyCam deterministic, hardware, live-api, and scenario filter environment variables. |
| `BodyCamUatFixture.cs` | BodyCam app launch/setup, scenario reset, screenshot directory selection, and custom `[UatPhrase]` command implementations. |
| `BodyCamUatScenarioTests.cs` | Thin wrapper over `UatScenarioTestBase<BodyCamUatFixture>`. |
| `BodyCamUatSpecFormatTests.cs` | Thin wrapper over `UatSpecFormatTestBase` with `RuntimeRootType = typeof(BodyCamUatFixture)` and BodyCam config checks. |

## New BodyCam Shape

`BodyCamUatScenarioTests` no longer parses or binds UAT markdown itself. It only
declares the data source, calls the base runner, and supplies BodyCam hooks:

```csharp
public sealed class BodyCamUatScenarioTests
    : UatScenarioTestBase<BodyCamUatFixture>
{
    public static IEnumerable<object[]> ScenarioFiles =>
        GetScenarioFiles(filterEnvironmentVariable: BodyCamUatEnvironment.ScenarioFilterVariable);

    [Theory(Timeout = 120000)]
    [MemberData(nameof(ScenarioFiles))]
    public Task UatFile_Passes(string filePath) => RunUatFileAsync(filePath);

    protected override void BeforeScenario(UatBoundScenario scenario) =>
        Fixture.ResetScenarioState();
}
```

`BodyCamUatSpecFormatTests` now delegates the generic metadata/bind/config work
to `UatSpecFormatTestBase`. It does not register BodyCam phrases manually; it
only points the base class at `BodyCamUatFixture` so `[UatPhrase]` methods are
picked up by the same reflection path as runtime execution.

## New Brinell Maui Template Shape

The Brinell Maui UAT template now uses the same base class:

```csharp
public sealed class MauiUatScenarioTests
    : UatScenarioTestBase<AppiumFixture>
{
    public static IEnumerable<object[]> ScenarioFiles => GetScenarioFiles();

    public static IEnumerable<object[]> ExpectedFailureScenarioFiles =>
        GetScenarioFiles("ExpectedFailures");

    [Theory(Timeout = 120000)]
    [MemberData(nameof(ScenarioFiles))]
    public Task UatFile_Passes(string filePath) => RunUatFileAsync(filePath);

    [Theory(Timeout = 120000)]
    [MemberData(nameof(ExpectedFailureScenarioFiles))]
    public Task ExpectedFailureUatFile_ReturnsUsefulDiagnostics(string filePath) =>
        RunExpectedFailureUatFileAsync(
            filePath,
            "Imaginary Button",
            "Available controls",
            Path.GetFileName(filePath));
}
```

## What Was Removed From Project Runtime Code

BodyCam and the Brinell Maui template runtime tests no longer directly call:

- `UatMarkdownParser.ParseFile`
- `UatBinder.Bind`
- `UatRuntime.CreateCommandCatalog`
- `UatScenarioExecutor.RunAsync`
- `UatDiagnosticsFormatter.FormatResults`
- `UatScenarioSource.GetScenarioFileTheoryData`

Those calls now live in Brinell base classes.

BodyCam runtime code also no longer has a separate custom phrase registration
table. Custom phrases are discovered from `BodyCamUatFixture`.

## Why The Static `ScenarioFiles` Property Still Exists

xUnit `MemberData` needs a static member. The per-project test class therefore
still has a tiny `ScenarioFiles` property, but it no longer owns discovery logic
or a project-specific source wrapper. It only supplies the project filter
variable to the Brinell base helper.

If we want to remove even that property later, the next step is a Brinell-owned
custom data attribute, for example:

```csharp
[UatScenarioData(FilterEnvironmentVariable = BodyCamUatEnvironment.ScenarioFilterVariable)]
```

That would make scenario discovery fully declarative, but it is a separate xUnit
extension. The current implementation already removes the duplicated runtime
logic and deletes `BodyCamUatScenarioSource.GetScenarioFiles()`.

## Verification

Validated in this pass:

- `dotnet build Brinell\srcnew\Brinell.Uat\Brinell.Uat.csproj --no-restore`
- `dotnet build Brinell\testsnew\Brinell.Maui.Uat.Tests\Brinell.Maui.Uat.Tests.csproj --no-restore`
- `dotnet build src\BodyCam.UAT\BodyCam.UAT.csproj --no-restore`
- `dotnet test Brinell\testsnew\Brinell.Uat.Tests\Brinell.Uat.Tests.csproj --no-restore` (53 passed)
- `dotnet test src\BodyCam.UAT\BodyCam.UAT.csproj --filter "Layer=SpecFormat" --no-build` (13 passed)

The full Appium UAT scenario suite was not run here because it launches the app
and depends on the local UI runtime.

## Remaining Optional Cleanup

1. Add a Brinell xUnit data attribute to remove the last static `ScenarioFiles`
   properties.
2. Consider moving the base test classes into a future `Brinell.Uat.Xunit`
   package if Brinell should keep all xUnit-shaped helpers outside the core UAT
   package.
3. Add more edge-case tests later if the base runner gains additional knobs,
   such as custom skip handling or declarative scenario data attributes.
