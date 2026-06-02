# Phase 3 / Wave 1 — `HeyCyanAudioInputProvider`

**Parent:** [../phase3-audio-providers.md](../phase3-audio-providers.md) ·
**Siblings:** [Wave 2](wave2-heycyan-audio-output-provider.md) ·
[Wave 3](wave3-auto-routing-service.md) ·
[Wave 4](wave4-a2dp-codec-verification.md) ·
[Wave 5](wave5-tests.md)

## Goal

Implement a thin `IAudioInputProvider` (M12) that wraps the generic
`BluetoothAudioInputProvider` and constrains it to the **BT Classic HFP/SCO
mic** whose MAC matches the QCSDK-paired glasses device. The QCSDK does not
carry live audio bytes — it is used **only** as a routing trigger via
`IHeyCyanGlassesSession.StateChanged`. Live mic audio flows over standard
A2DP+HFP, just like any BT headset.

The provider must:
- Report `IsAvailable` only when the QCSDK session is `Connected` **and** a
  BT capture endpoint matching `HeyCyanDeviceInfo.Address` is enumerated by
  the OS BT stack.
- Refuse to start when no MAC-matched endpoint exists (do **not** silently
  fall through to the phone mic — the auto-router in Wave 3 owns fallback).
- Stop capture immediately when `StateChanged` leaves `Connected`.
- Re-emit PCM16 24 kHz mono frames unchanged from the generic provider.

## Steps

1. Confirm the generic `BluetoothAudioInputProvider` (M12 Phase 2) exposes
   the MAC-aware surface this wave depends on. If it does not, add the
   following members to its interface (cross-platform):

    ```csharp
    // src/BodyCam/Services/Audio/IBluetoothAudioInputProvider.cs
    public interface IBluetoothAudioInputProvider : IAudioInputProvider
    {
        /// <summary>Returns true if a connected BT capture endpoint with this MAC exists.</summary>
        bool HasEndpointWithMac(string? mac);

        /// <summary>Locks subsequent StartAsync calls to the endpoint with this MAC.</summary>
        Task SelectEndpointByMacAsync(string mac, CancellationToken ct);
    }
    ```

   MAC comparison is **case-insensitive** and tolerates colon vs. dash
   separators (`AA:BB:CC:DD:EE:FF` ≡ `aa-bb-cc-dd-ee-ff`).

2. Create `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioInputProvider.cs`:

    ```csharp
    namespace BodyCam.Services.Glasses.HeyCyan;

    public sealed class HeyCyanAudioInputProvider : IAudioInputProvider, IAsyncDisposable
    {
        private readonly IHeyCyanGlassesSession _session;
        private readonly IBluetoothAudioInputProvider _bt;
        private readonly ILogger<HeyCyanAudioInputProvider> _log;

        public string ProviderId  => "heycyan-glasses";
        public string DisplayName => "HeyCyan Glasses Mic";

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
            _bt      = bt;
            _log     = log;

            _bt.AudioChunkAvailable += OnChunk;
            _bt.Disconnected        += OnBtDisconnected;
            _session.StateChanged   += OnSessionStateChanged;
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

        public Task StopAsync(CancellationToken ct = default) => _bt.StopAsync(ct);

        private void OnChunk(object? _, byte[] chunk) =>
            AudioChunkAvailable?.Invoke(this, chunk);

        private void OnBtDisconnected(object? _, EventArgs __) =>
            Disconnected?.Invoke(this, EventArgs.Empty);

        private void OnSessionStateChanged(object? _, HeyCyanState state)
        {
            if (state != HeyCyanState.Connected && _bt.IsCapturing)
            {
                _log.LogInformation("HeyCyan session left Connected ({State}); stopping mic.", state);
                _ = _bt.StopAsync(CancellationToken.None);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _bt.AudioChunkAvailable -= OnChunk;
            _bt.Disconnected        -= OnBtDisconnected;
            _session.StateChanged   -= OnSessionStateChanged;
            if (_bt is IAsyncDisposable d) await d.DisposeAsync();
        }
    }
    ```

3. Register the provider in DI (cross-platform, alongside the Phase 1
   `IHeyCyanGlassesSession`):

    ```csharp
    services.AddSingleton<HeyCyanAudioInputProvider>();
    services.AddSingleton<IAudioInputProvider>(sp =>
        sp.GetRequiredService<HeyCyanAudioInputProvider>());
    ```

   Do **not** register as `IBluetoothAudioInputProvider` — that contract is
   reserved for the generic M12 provider this wraps.

4. Document the SBC-minimum codec contract inline at the top of the file
   (xmldoc on the class): no aptX / LDAC promises, mSBC for HFP if the OS
   negotiates it, CVSD otherwise. Wave 4 surfaces actual negotiated codec.

5. Wire `Disconnected` to flow through `AudioInputManager` exactly as the
   generic provider does — the manager already knows how to fall back to
   `"platform"` when the active provider drops.

## Verify

- [ ] `IsAvailable` is `false` until `IHeyCyanGlassesSession.State == Connected`
      **and** `HasEndpointWithMac(Device.Address)` returns true.
- [ ] `StartAsync` throws `InvalidOperationException` when the session is
      disconnected or no MAC-matched endpoint exists.
- [ ] `StartAsync` calls `SelectEndpointByMacAsync(mac, ct)` **before**
      `StartAsync` on the inner BT provider.
- [ ] `AudioChunkAvailable` re-emits PCM16 24 kHz mono chunks unchanged.
- [ ] `Disconnected` fires when the inner BT provider raises it.
- [ ] On `StateChanged → Disconnecting | Disconnected`, capture is stopped.
- [ ] `DisposeAsync` unhooks every subscription it made (no leaked handlers
      across repeated connect/disconnect cycles).
- [ ] DI registration resolves a single shared instance for both
      `HeyCyanAudioInputProvider` and `IAudioInputProvider`.
- [ ] Manual smoke: pair glasses, connect via QCSDK, observe
      `AudioInputManager.ActiveProviderId == "heycyan-glasses"` after Wave 3
      router is added.
