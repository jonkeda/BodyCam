using BodyCam.Services.Audio;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// HeyCyan glasses audio output provider.
/// Wraps the generic Bluetooth audio output provider and constrains it to the
/// BT Classic A2DP speaker whose MAC matches the QCSDK-paired glasses device.
/// 
/// Audio codec: SBC minimum baseline. AAC/aptX/LDAC are negotiated by the OS
/// if supported by hardware; we never explicitly select or assume them.
/// 
/// Live audio flows over BT Classic (A2DP), NOT through the QCSDK BLE channel.
/// The QCSDK session is used only as a routing trigger via StateChanged events.
/// </summary>
public sealed class HeyCyanAudioOutputProvider : IAudioOutputProvider, IAsyncDisposable
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly IBluetoothAudioOutputProvider _bt;
    private readonly ILogger<HeyCyanAudioOutputProvider> _log;

    public string ProviderId => "heycyan-glasses";
    public string DisplayName => "HeyCyan Glasses Speaker";

    public bool IsAvailable =>
        _session.State == HeyCyanState.Connected &&
        _bt.HasEndpointWithMac(_session.Device?.Address);

    public bool IsPlaying => _bt.IsPlaying;
    public int EstimatedOutputLatencyMs => _bt.EstimatedOutputLatencyMs;
    public AudioOutputCapabilities OutputCapabilities => new(
        EchoPathKind.GlassesOrWearable,
        NeedsEchoCancellation: false,
        IsAcousticallyIsolated: true,
        SupportsRenderReference: false,
        EstimatedOutputLatencyMs);

    public event EventHandler? Disconnected;
    public event EventHandler? OutputRouteChanged;

    public HeyCyanAudioOutputProvider(
        IHeyCyanGlassesSession session,
        IBluetoothAudioOutputProvider bt,
        ILogger<HeyCyanAudioOutputProvider> log)
    {
        _session = session;
        _bt = bt;
        _log = log;

        _bt.Disconnected += OnBtDisconnected;
        _session.StateChanged += OnSessionStateChanged;
    }

    public async Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        var mac = _session.Device?.Address
            ?? throw new InvalidOperationException("HeyCyan glasses not connected.");

        if (!_bt.HasEndpointWithMac(mac))
            throw new InvalidOperationException(
                $"No BT render endpoint matching glasses MAC {mac}. " +
                "Ensure the glasses are paired as a BT audio device on this phone.");

        await _bt.SelectEndpointByMacAsync(mac, ct).ConfigureAwait(false);
        await _bt.StartAsync(sampleRate, ct).ConfigureAwait(false);
        _log.LogInformation("HeyCyan speaker started (mac={Mac}, sampleRate={SampleRate}).", mac, sampleRate);
    }

    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default) =>
        _bt.PlayChunkAsync(pcmData, ct);

    public Task StopAsync()
    {
        return _bt.StopAsync();
    }

    public void ClearBuffer()
    {
        _bt.ClearBuffer();
    }

    public Task FadeOutAndClearAsync(int fadeMs = 30, CancellationToken ct = default)
    {
        return _bt.FadeOutAndClearAsync(fadeMs, ct);
    }

    private void OnBtDisconnected(object? _, EventArgs __)
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionStateChanged(object? _, HeyCyanState state)
    {
        if (state != HeyCyanState.Connected && _bt.IsPlaying)
        {
            _log.LogInformation("HeyCyan session left Connected ({State}); stopping speaker.", state);
            _ = _bt.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _bt.Disconnected -= OnBtDisconnected;
        _session.StateChanged -= OnSessionStateChanged;
        if (_bt is IAsyncDisposable d) await d.DisposeAsync();
    }
}
