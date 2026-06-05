using Brinell.Core.Artifacts;

namespace BodyCam.UAT.Runtime;

internal static class BodyCamUatEnvironment
{
    public const string TestModeVariable = "BODYCAM_TEST_MODE";
    public const string AssetsVariable = "BODYCAM_UAT_ASSETS";
    public const string ReportsVariable = "BODYCAM_UAT_REPORTS";
    public const string LiveApiVariable = "BODYCAM_UAT_LIVE_API";
    public const string HardwareVariable = "BODYCAM_UAT_HARDWARE";
    public const string ManualVariable = "BODYCAM_UAT_MANUAL";
    public const string SemiAutomatedVariable = "BODYCAM_UAT_SEMI_AUTOMATED";
    public const string ScenarioFilterVariable = "BODYCAM_UAT_SCENARIO_FILTER";

    public static BodyCamFixtureLaunchOptions Apply()
    {
        var assetsDirectory = GetOrCreateDirectory(
            AssetsVariable,
            Path.Combine(AppContext.BaseDirectory, "UatAssets"));
        var artifactPaths = DefaultTestArtifactPathProvider.Create(
            typeof(BodyCamUatEnvironment).Assembly.GetName().Name);
        var reportsDirectory = GetOrCreateDirectory(
            ReportsVariable,
            artifactPaths.UatDirectory);

        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase)
        {
            [TestModeVariable] = ReadOrDefault(TestModeVariable, "uat"),
            [AssetsVariable] = assetsDirectory,
            [ReportsVariable] = reportsDirectory,
            [LiveApiVariable] = ReadOrDefault(LiveApiVariable, "0"),
            [HardwareVariable] = ReadOrDefault(HardwareVariable, "0"),
            [ManualVariable] = ReadOrDefault(ManualVariable, "0"),
            [SemiAutomatedVariable] = ReadOrDefault(SemiAutomatedVariable, "0")
        };

        foreach (var value in values)
        {
            Environment.SetEnvironmentVariable(value.Key, value.Value);
        }

        return new BodyCamFixtureLaunchOptions("uat", assetsDirectory, reportsDirectory, values);
    }

    private static string ReadOrDefault(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string GetOrCreateDirectory(string environmentVariable, string defaultPath)
    {
        var configured = Environment.GetEnvironmentVariable(environmentVariable);
        var path = string.IsNullOrWhiteSpace(configured)
            ? defaultPath
            : configured;

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }
}
