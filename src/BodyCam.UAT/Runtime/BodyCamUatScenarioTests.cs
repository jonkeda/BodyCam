namespace BodyCam.UAT.Runtime;

[Collection(BodyCamUatCollection.CollectionName)]
[Trait("Category", "UAT")]
[Trait("Target", "MAUI")]
public sealed class BodyCamUatScenarioTests
    : UatScenarioTestBase<BodyCamUatFixture>
{
    public BodyCamUatScenarioTests(BodyCamUatFixture fixture)
        : base(fixture)
    {
    }

    public static IEnumerable<object[]> ScenarioFiles =>
        GetScenarioFiles(filterEnvironmentVariable: BodyCamUatRunnerVariables.ScenarioFilterVariable);

    [Theory(Timeout = 120000)]
    [MemberData(nameof(ScenarioFiles))]
    public Task UatFile_Passes(string filePath) => RunUatFileAsync(filePath);

    protected override UatRuntimeValidationOptions RuntimeValidation { get; } =
        new(Target: "MAUI", Fixture: "Appium");

    protected override void BeforeScenario(UatBoundScenario scenario) =>
        Fixture.ResetScenarioState();
}
