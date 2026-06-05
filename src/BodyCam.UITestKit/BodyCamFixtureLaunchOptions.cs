namespace BodyCam.UITestKit;

public sealed record BodyCamFixtureLaunchOptions(
    string Mode,
    string AssetsDirectory,
    string ReportsDirectory,
    IReadOnlyDictionary<string, string> Environment)
{
    public static BodyCamFixtureLaunchOptions Default { get; } = new(
        Mode: "default",
        AssetsDirectory: string.Empty,
        ReportsDirectory: string.Empty,
        Environment: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
