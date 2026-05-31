namespace BodyCam.Services.Audio;

public sealed record AudioInputCapabilities(
    bool HasPlatformEchoCancellation,
    bool PlatformEchoCancellationActive,
    int EstimatedInputLatencyMs)
{
    public static AudioInputCapabilities Default { get; } = new(
        HasPlatformEchoCancellation: false,
        PlatformEchoCancellationActive: false,
        EstimatedInputLatencyMs: 0);
}

public sealed record AudioOutputCapabilities(
    EchoPathKind EchoPathKind,
    bool NeedsEchoCancellation,
    bool IsAcousticallyIsolated,
    bool SupportsRenderReference,
    int EstimatedOutputLatencyMs)
{
    public static AudioOutputCapabilities Unknown(int latencyMs = 80) => new(
        EchoPathKind.Unknown,
        NeedsEchoCancellation: true,
        IsAcousticallyIsolated: false,
        SupportsRenderReference: true,
        EstimatedOutputLatencyMs: latencyMs);

    public static AudioOutputCapabilities NoLocalPlayback { get; } = new(
        EchoPathKind.NoLocalPlayback,
        NeedsEchoCancellation: false,
        IsAcousticallyIsolated: true,
        SupportsRenderReference: false,
        EstimatedOutputLatencyMs: 0);
}

public enum EchoPathKind
{
    DirectDeviceSpeaker,
    ExternalRoomSpeaker,
    IsolatedHeadset,
    GlassesOrWearable,
    NoLocalPlayback,
    Unknown
}

public enum AecMode
{
    Off,
    PlatformNative,
    WebRtcApm,
    WindowsDmoFallback
}

public enum VoiceCleanupMode
{
    Off,
    NoiseSuppressionOnly,
    NoiseSuppressionAndAgc
}

internal static class AudioCapabilityHeuristics
{
    private static readonly string[] HeadsetIndicators =
    [
        "airpods",
        "buds",
        "earbud",
        "earbuds",
        "earphone",
        "earphones",
        "hands-free",
        "handsfree",
        "headphones",
        "headphone",
        "headset",
        "pods"
    ];

    public static AudioOutputCapabilities BluetoothOutput(
        string? displayName,
        int latencyMs,
        bool knownHeadsetRoute = false)
    {
        if (knownHeadsetRoute || IsLikelyHeadsetName(displayName))
        {
            return new AudioOutputCapabilities(
                EchoPathKind.IsolatedHeadset,
                NeedsEchoCancellation: false,
                IsAcousticallyIsolated: true,
                SupportsRenderReference: false,
                EstimatedOutputLatencyMs: latencyMs);
        }

        return new AudioOutputCapabilities(
            EchoPathKind.ExternalRoomSpeaker,
            NeedsEchoCancellation: true,
            IsAcousticallyIsolated: false,
            SupportsRenderReference: true,
            EstimatedOutputLatencyMs: latencyMs);
    }

    public static bool IsLikelyHeadsetName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        return HeadsetIndicators.Any(indicator =>
            displayName.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }
}
