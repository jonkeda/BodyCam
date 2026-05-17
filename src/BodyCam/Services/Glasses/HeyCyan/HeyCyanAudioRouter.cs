using BodyCam.Services.Audio;
using Microsoft.Extensions.Logging;

namespace BodyCam.Services.Glasses.HeyCyan;

/// <summary>
/// Automatically routes audio input/output to HeyCyan glasses when they connect
/// and restores the previous provider when they disconnect.
/// Subscribes to <see cref="IHeyCyanGlassesSession.StateChanged"/> and flips
/// <see cref="AudioInputManager"/> / <see cref="AudioOutputManager"/> active providers.
/// </summary>
/// <remarks>
/// <para>Routing is reactive: when the session enters Connected, providers are
/// registered and availability is checked once. If BT endpoints appear later
/// (e.g. Classic BT profile connects after BLE), the platform BT enumerators
/// raise an event that triggers auto-selection via <see cref="OnBtEndpointRegistered"/>.</para>
/// <para>Fallback policy:</para>
/// <list type="bullet">
///   <item>On first <c>Connected</c>: snapshot whatever was active (typically "platform").</item>
///   <item>On disconnect: restore the snapshot, then clear it.</item>
///   <item>Manual provider changes while glasses are connected do NOT update the snapshot —
///         disconnecting still restores the pre-glasses choice.</item>
///   <item><c>TransferMode</c> does NOT trigger disconnect routing (SDK enters transfer mode
///         for seconds during file pulls; audio should remain on glasses).</item>
/// </list>
/// </remarks>
public sealed class HeyCyanAudioRouter : IAsyncDisposable
{
    private readonly IHeyCyanGlassesSession _session;
    private readonly AudioInputManager _input;
    private readonly AudioOutputManager _output;
    private readonly HeyCyanAudioInputProvider _inputProvider;
    private readonly HeyCyanAudioOutputProvider _outputProvider;
    private readonly ILogger<HeyCyanAudioRouter> _log;

    // Captured snapshot of the providers active *before* the glasses
    // connected, so we can restore them on disconnect.
    private string? _previousInputId;
    private string? _previousOutputId;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HeyCyanAudioRouter(
        IHeyCyanGlassesSession session,
        AudioInputManager input,
        AudioOutputManager output,
        HeyCyanAudioInputProvider inputProvider,
        HeyCyanAudioOutputProvider outputProvider,
        ILogger<HeyCyanAudioRouter> log)
    {
        _session = session;
        _input   = input;
        _output  = output;
        _inputProvider  = inputProvider;
        _outputProvider = outputProvider;
        _log     = log;

        _session.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Called by platform BT enumerators when a new endpoint is registered.
    /// If we are connected to glasses and the MAC matches, auto-select.
    /// </summary>
    public void OnBtEndpointRegistered(string mac)
    {
        if (_session.State != HeyCyanState.Connected) return;
        if (!string.Equals(_session.Device?.Address, mac, StringComparison.OrdinalIgnoreCase)) return;

        _ = TryAutoSelectAsync();
    }

    private async void OnStateChanged(object? sender, HeyCyanState state)
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await ApplyAsync(state).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HeyCyan audio routing failed (state={State}).", state);
        }
    }

    private async Task ApplyAsync(HeyCyanState state)
    {
        switch (state)
        {
            case HeyCyanState.Connected:
                _previousInputId  ??= _input.ActiveProviderId;
                _previousOutputId ??= _output.ActiveProviderId;

                // Dynamically register glasses providers so they appear in the UI pickers
                _input.RegisterProvider(_inputProvider);
                _output.RegisterProvider(_outputProvider);

                // Check once — if endpoints are already available, select immediately.
                // If not, the BT enumerator's EndpointRegistered event will trigger
                // auto-selection reactively via OnBtEndpointRegistered.
                if (_inputProvider.IsAvailable)
                    await _input .SetActiveProviderAsync("heycyan-glasses").ConfigureAwait(false);
                else
                    _log.LogInformation("HeyCyan glasses mic not yet available (mac={Mac}) — will auto-select when endpoint appears.", _session.Device?.Address);

                if (_outputProvider.IsAvailable)
                    await _output.SetActiveProviderAsync("heycyan-glasses").ConfigureAwait(false);
                else
                    _log.LogInformation("HeyCyan glasses speaker not yet available (mac={Mac}) — will auto-select when endpoint appears.", _session.Device?.Address);

                _log.LogInformation(
                    "Routed live audio to HeyCyan glasses (mac={Mac}, restoreIn={In}, restoreOut={Out}).",
                    _session.Device?.Address, _previousInputId, _previousOutputId);
                break;

            case HeyCyanState.Disconnected:
            case HeyCyanState.Disconnecting:
                var inFallback  = _previousInputId  ?? "platform";
                var outFallback = _previousOutputId ?? "platform";

                // UnregisterProviderAsync falls back to platform if active, then removes from list
                await _input .UnregisterProviderAsync("heycyan-glasses").ConfigureAwait(false);
                await _output.UnregisterProviderAsync("heycyan-glasses").ConfigureAwait(false);

                _previousInputId  = null;
                _previousOutputId = null;
                _log.LogInformation(
                    "Glasses left Connected ({State}); fell back to {In}/{Out}.",
                    state, inFallback, outFallback);
                break;

            // Scanning / Connecting / TransferMode — do not touch routing.
            default:
                break;
        }
    }

    private async Task TryAutoSelectAsync()
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_input.ActiveProviderId != "heycyan-glasses" && _inputProvider.IsAvailable)
                {
                    _log.LogInformation("BT endpoint appeared — auto-selecting HeyCyan glasses mic");
                    await _input.SetActiveProviderAsync("heycyan-glasses").ConfigureAwait(false);
                }
                if (_output.ActiveProviderId != "heycyan-glasses" && _outputProvider.IsAvailable)
                {
                    _log.LogInformation("BT endpoint appeared — auto-selecting HeyCyan glasses speaker");
                    await _output.SetActiveProviderAsync("heycyan-glasses").ConfigureAwait(false);
                }
            }
            finally { _gate.Release(); }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to auto-select HeyCyan glasses audio after endpoint appeared");
        }
    }

    public ValueTask DisposeAsync()
    {
        _session.StateChanged -= OnStateChanged;
        _gate.Dispose();
        return default;
    }
}
