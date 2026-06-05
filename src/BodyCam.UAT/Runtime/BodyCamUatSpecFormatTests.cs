namespace BodyCam.UAT.Runtime;

[Trait("Category", "UAT")]
[Trait("Layer", "SpecFormat")]
public sealed class BodyCamUatSpecFormatTests
    : UatSpecFormatTestBase
{
    public static IEnumerable<object[]> ScenarioFiles =>
        GetScenarioFiles(filterEnvironmentVariable: BodyCamUatRunnerVariables.ScenarioFilterVariable);

    [Theory]
    [MemberData(nameof(ScenarioFiles))]
    public void UatFile_ParsesAndContainsRequiredMetadata(string filePath) =>
        AssertUatFileParsesAndContainsRequiredMetadata(filePath);

    [Theory]
    [MemberData(nameof(ScenarioFiles))]
    public void UatFile_BindsThroughBrinellCatalog(string filePath) =>
        AssertUatFileBindsThroughCatalog(filePath);

    [Fact]
    public void UatConfig_ParsesRuntimeDiscoveryReportingAndSkipRules() =>
        AssertUatConfigParses();

    protected override string? ExpectedApp => "BodyCam";

    protected override string? ExpectedTarget => "MAUI";

    protected override Type? RuntimeRootType => typeof(BodyCamUatFixture);

    protected override void AssertConfig(UatConfig config)
    {
        Assert.Equal("MAUI", config.Runtime["Target"]);
        Assert.Equal("Appium", config.Runtime["Fixture"]);
        Assert.False(config.Discovery.RequireExplicitUatAttributes);
        Assert.True(config.Discovery.AllowNameInference);
        Assert.EndsWith(
            Path.Combine("suites", "BodyCam.UAT", "uat"),
            config.Reporting.OutputDirectory);
        Assert.True(config.Reporting.ScreenshotOnFailure);
        Assert.Equal("TestSettings", config.Settings.Root);
        Assert.Equal("testsettings.json", config.Settings.DefaultFile);
        Assert.Equal("testsettings.local.json", config.Settings.LocalFile);
        Assert.Equal("scenarios/{ScenarioId}.json", config.Settings.ScenarioConvention);
        Assert.Contains(config.SkipRules, rule =>
            rule.Tag == UatTagConventions.Hardware &&
            rule.EnvironmentVariable == BodyCamUatLegacySkipVariables.HardwareVariable);
        Assert.Contains(config.SkipRules, rule =>
            rule.Tag == UatTagConventions.LiveApi &&
            rule.EnvironmentVariable == BodyCamUatLegacySkipVariables.LiveApiVariable);
    }
}
