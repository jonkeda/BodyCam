namespace BodyCam.Services.Audio;

public sealed record AudioRoutePolicy(
    AudioInputCapabilities InputCapabilities,
    AudioOutputCapabilities OutputCapabilities,
    bool HasLocalPlayback,
    bool RouteReportsHeadphones,
    bool RouteReportsBluetoothAudio,
    int EstimatedRoundTripLatencyMs,
    AecMode AecMode,
    VoiceCleanupMode VoiceCleanupMode,
    string Explanation)
{
    public static AudioRoutePolicy Default { get; } = new(
        AudioInputCapabilities.Default,
        AudioOutputCapabilities.NoLocalPlayback,
        HasLocalPlayback: false,
        RouteReportsHeadphones: false,
        RouteReportsBluetoothAudio: false,
        EstimatedRoundTripLatencyMs: 0,
        AecMode.Off,
        VoiceCleanupMode.Off,
        "No active local playback route.");
}

public interface IAudioRoutePolicyService
{
    AudioRoutePolicy Current { get; }
    event EventHandler<AudioRoutePolicy>? PolicyChanged;
    AudioRoutePolicy Recompute();
}
