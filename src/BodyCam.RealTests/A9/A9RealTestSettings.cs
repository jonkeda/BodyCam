using BodyCam.Services.Camera.A9.Probe;

namespace BodyCam.RealTests.A9;

internal static class A9RealTestSettings
{
    public static bool Enabled =>
        Environment.GetEnvironmentVariable("A9_E2E") == "1";

    public static bool DiscoveryEnabled =>
        Environment.GetEnvironmentVariable("A9_DISCOVERY_E2E") == "1";

    public static bool HasKnownHost =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("A9_CAMERA_IP"));

    public static A9ProbeOptions CreateProbeOptions(
        bool firstFrame = false,
        A9ProbeProtocol protocol = A9ProbeProtocol.Auto)
    {
        return new A9ProbeOptions
        {
            Host = Environment.GetEnvironmentVariable("A9_CAMERA_IP"),
            Username = Environment.GetEnvironmentVariable("A9_CAMERA_USERNAME") ?? "admin",
            Password = Environment.GetEnvironmentVariable("A9_CAMERA_PASSWORD") ?? "admin",
            Protocol = protocol,
            TimeoutMs = ReadTimeoutMs(),
            FirstFrame = firstFrame,
        };
    }

    private static int ReadTimeoutMs()
    {
        var raw = Environment.GetEnvironmentVariable("A9_PROBE_TIMEOUT_MS");
        return int.TryParse(raw, out var timeoutMs) && timeoutMs >= 100
            ? timeoutMs
            : 1200;
    }
}
