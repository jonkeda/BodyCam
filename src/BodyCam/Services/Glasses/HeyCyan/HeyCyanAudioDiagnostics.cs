using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Cross-platform implementation of <see cref="IHeyCyanAudioDiagnostics"/>.
/// Listens to <see cref="IHeyCyanGlassesSession.StateChanged"/> and refreshes
/// codec diagnostics when the glasses connect.
/// </summary>
public sealed class HeyCyanAudioDiagnostics : IHeyCyanAudioDiagnostics, IAsyncDisposable
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly IHeyCyanCodecProbe _probe;
    private readonly ILogger<HeyCyanAudioDiagnostics> _log;

    public HeyCyanAudioRouteInfo? Current { get; private set; }

    public event EventHandler<HeyCyanAudioRouteInfo>? Updated;

    public HeyCyanAudioDiagnostics(
        IHeyCyanGlassesSession session,
        IHeyCyanCodecProbe probe,
        ILogger<HeyCyanAudioDiagnostics> log)
    {
        _session = session;
        _probe   = probe;
        _log     = log;

        _session.StateChanged += OnSessionStateChanged;
    }

    private async void OnSessionStateChanged(object? sender, HeyCyanState state)
    {
        if (state == HeyCyanState.Connected)
            await RefreshAsync().ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var mac = _session.Device?.Address;
            if (string.IsNullOrEmpty(mac))
            {
                Current = null;
                return;
            }

            var info = await _probe.ProbeAsync(mac, ct).ConfigureAwait(false);
            Current = info;

            if (info != null)
            {
                _log.LogInformation(
                    "HeyCyan audio diagnostics refreshed: A2DP={Codec} {Rate}Hz {Ch}ch, HFP={Hfp}",
                    info.NegotiatedA2dpCodec ?? "unknown",
                    info.SampleRateHz,
                    info.Channels,
                    info.HfpCodec ?? "unknown");

                Updated?.Invoke(this, info);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Codec probe failed; diagnostics will report null.");
            Current = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        _session.StateChanged -= OnSessionStateChanged;
        return default;
    }
}

/// <summary>
/// Platform-specific codec probe implementation.
/// Android: uses BluetoothA2dp.GetCodecStatus (API 28+).
/// iOS: returns null codec fields (iOS hides codec details from third-party apps).
/// </summary>
public interface IHeyCyanCodecProbe
{
    Task<HeyCyanAudioRouteInfo?> ProbeAsync(string mac, CancellationToken ct);
}
