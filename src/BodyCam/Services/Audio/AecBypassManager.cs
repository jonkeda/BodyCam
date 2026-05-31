using BodyCam.Services.Audio.WebRtcApm;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Audio;

/// <summary>
/// Applies the current audio route policy to the AEC processor.
/// </summary>
public sealed class AecBypassManager : IAsyncDisposable
{
    private readonly IAudioRoutePolicyService _policy;
    private readonly IAecProcessor _aec;
    private readonly ILogger<AecBypassManager> _logger;

    public AecBypassManager(
        IAudioRoutePolicyService policy,
        IAecProcessor aec,
        ILogger<AecBypassManager> logger)
    {
        _policy = policy;
        _aec = aec;
        _logger = logger;

        _policy.PolicyChanged += OnPolicyChanged;
        ApplyPolicy(_policy.Recompute());
    }

    private void OnPolicyChanged(object? sender, AudioRoutePolicy policy)
    {
        ApplyPolicy(policy);
    }

    private void ApplyPolicy(AudioRoutePolicy policy)
    {
        var enabled = policy.AecMode is AecMode.WebRtcApm or AecMode.WindowsDmoFallback;
        _aec.IsEnabled = enabled;

        if (enabled)
            _aec.UpdateStreamDelay(policy.EstimatedRoundTripLatencyMs);

        _logger.LogInformation(
            "Applied audio route policy; AEC IsEnabled={Enabled}, mode={Mode}, cleanup={Cleanup}, reason={Reason}",
            enabled,
            policy.AecMode,
            policy.VoiceCleanupMode,
            policy.Explanation);
    }

    public ValueTask DisposeAsync()
    {
        _policy.PolicyChanged -= OnPolicyChanged;
        return ValueTask.CompletedTask;
    }
}
