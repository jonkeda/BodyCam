using Brinell.Core.Settings;

namespace BodyCam.UAT.Runtime;

[TestSettingsRoot]
public sealed class BodyCamTestSettings
{
    public BodyCamUatSettings Uat { get; init; } = new();

    public BodyCamCapabilitySettings Capabilities { get; init; } = new();

    public BodyCamHardwareSettings Hardware { get; init; } = new();
}

public sealed class BodyCamUatSettings
{
    public string StartupMode { get; init; } = string.Empty;

    public bool ResetAppSettingsBeforeScenario { get; init; } = true;
}

public sealed class BodyCamCapabilitySettings
{
    public bool Hardware { get; init; }

    public bool LiveApi { get; init; }

    public bool Manual { get; init; }

    public bool SemiAutomated { get; init; }
}

public sealed class BodyCamHardwareSettings
{
    public A9CameraSettings A9Camera { get; init; } = new();
}

[TestSettingsSection("hardware.a9Camera")]
public sealed class A9CameraSettings
{
    public string Host { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
