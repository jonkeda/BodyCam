using Brinell.Core.Artifacts;

namespace BodyCam.UAT.Runtime;

internal static class BodyCamUatEnvironment
{
    public const string TestModeVariable = "BODYCAM_TEST_MODE";
    public const string AssetsVariable = "BODYCAM_UAT_ASSETS";
    public const string ReportsVariable = "BODYCAM_UAT_REPORTS";

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
            [TestModeVariable] = "uat",
            [AssetsVariable] = assetsDirectory,
            [ReportsVariable] = reportsDirectory
        };

        foreach (var value in values)
        {
            Environment.SetEnvironmentVariable(value.Key, value.Value);
        }

        return new BodyCamFixtureLaunchOptions("uat", assetsDirectory, reportsDirectory, values);
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
