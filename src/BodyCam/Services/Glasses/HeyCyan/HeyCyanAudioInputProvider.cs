using BodyCam.Services.Audio;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// HeyCyan glasses audio input provider.
/// Wraps the generic Bluetooth audio input provider and constrains it to the
/// BT Classic HFP/SCO mic whose MAC matches the QCSDK-paired glasses device.
/// 
/// Audio codec: SBC minimum baseline (mSBC for HFP 1.6+ if negotiated by OS,
/// CVSD otherwise). No aptX/LDAC guarantees from this hardware.
/// 
/// Live audio flows over BT Classic (A2DP+HFP), NOT through the QCSDK BLE channel.
/// The QCSDK session is used only as a routing trigger via StateChanged events.
/// </summary>
public sealed class HeyCyanAudioInputProvider : IAudioInputProvider, IAsyncDisposable
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly IBluetoothAudioInputProvider _bt;
    private readonly ILogger<HeyCyanAudioInputProvider> _log;

    public string ProviderId => "heycyan-glasses";
    public string DisplayName => "HeyCyan Glasses Mic";
    public AudioInputCapabilities InputCapabilities => AudioInputCapabilities.Default;

    public bool IsAvailable =>
        _session.State == HeyCyanState.Connected &&
        _bt.HasEndpointWithMac(_session.Device?.Address);

    public bool IsCapturing => _bt.IsCapturing;

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public HeyCyanAudioInputProvider(
        IHeyCyanGlassesSession session,
        IBluetoothAudioInputProvider bt,
        ILogger<HeyCyanAudioInputProvider> log)
    {
        _session = session;
        _bt = bt;
        _log = log;

        _bt.AudioChunkAvailable += OnChunk;
        _bt.Disconnected += OnBtDisconnected;
        _session.StateChanged += OnSessionStateChanged;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var mac = _session.Device?.Address
            ?? throw new InvalidOperationException("HeyCyan glasses not connected.");

        if (!_bt.HasEndpointWithMac(mac))
            throw new InvalidOperationException(
                $"No BT capture endpoint matching glasses MAC {mac}. " +
                "Ensure the glasses are paired as a BT headset on this device.");

        await _bt.SelectEndpointByMacAsync(mac, ct).ConfigureAwait(false);
        await _bt.StartAsync(ct).ConfigureAwait(false);
        _log.LogInformation("HeyCyan mic started (mac={Mac}).", mac);
    }

    public Task StopAsync()
    {
        return _bt.StopAsync();
    }

    private void OnChunk(object? _, byte[] chunk)
    {
        AudioChunkAvailable?.Invoke(this, chunk);
    }

    private void OnBtDisconnected(object? _, EventArgs __)
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionStateChanged(object? _, HeyCyanState state)
    {
        if (state != HeyCyanState.Connected && _bt.IsCapturing)
        {
            _log.LogInformation("HeyCyan session left Connected ({State}); stopping mic.", state);
            _ = _bt.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _bt.AudioChunkAvailable -= OnChunk;
        _bt.Disconnected -= OnBtDisconnected;
        _session.StateChanged -= OnSessionStateChanged;
        if (_bt is IAsyncDisposable d) await d.DisposeAsync();
    }
}
