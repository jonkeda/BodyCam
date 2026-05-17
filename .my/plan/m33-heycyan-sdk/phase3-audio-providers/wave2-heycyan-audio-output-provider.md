# Phase 3 / Wave 2 — `HeyCyanAudioOutputProvider`

**Parent:** [../phase3-audio-providers.md](../phase3-audio-providers.md) ·
**Siblings:** [Wave 1](wave1-heycyan-audio-input-provider.md) ·
[Wave 3](wave3-auto-routing-service.md) ·
[Wave 4](wave4-a2dp-codec-verification.md) ·
[Wave 5](wave5-tests.md)

## Goal

Symmetric to Wave 1: implement an `IAudioOutputProvider` (M13) that wraps
the generic `BluetoothAudioOutputProvider` and locks playback to the **BT
Classic A2DP** speaker whose MAC matches the QCSDK-paired device. Realtime
API TTS frames flow over A2DP exactly the same way as music to a regular
BT headset; the QCSDK is only the routing trigger.

The provider must:
- Report `IsAvailable` only when the QCSDK session is `Connected` **and** a
  BT render endpoint matching `HeyCyanDeviceInfo.Address` is present.
- Forward PCM16 24 kHz mono chunks via `PlayChunkAsync` unchanged.
- Stop playback immediately when `StateChanged` leaves `Connected`.
- Document SBC as the codec floor — aptX / LDAC are not guaranteed on this
  hardware, and we do not negotiate codecs ourselves.

## Steps

1. Confirm the generic `IBluetoothAudioOutputProvider` (M13 Phase 2) exposes
   the MAC-aware surface this wave depends on. If not, add the symmetric
   members:

    ```csharp
    // src/BodyCam/Services/Audio/IBluetoothAudioOutputProvider.cs
    public interface IBluetoothAudioOutputProvider : IAudioOutputProvider
    {
        bool HasEndpointWithMac(string? mac);
        Task SelectEndpointByMacAsync(string mac, CancellationToken ct);
    }
    ```

   MAC comparison rules match Wave 1 (case-insensitive, `:`/`-` tolerant).

2. Create `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioOutputProvider.cs`:

    ```csharp
    namespace BodyCam.Services.Glasses.HeyCyan;

    public sealed class HeyCyanAudioOutputProvider : IAudioOutputProvider, IAsyncDisposable
    {
        private readonly IHeyCyanGlassesSession _session;
        private readonly IBluetoothAudioOutputProvider _bt;
        private readonly ILogger<HeyCyanAudioOutputProvider> _log;

        public string ProviderId  => "heycyan-glasses";
        public string DisplayName => "HeyCyan Glasses Speaker";

        public bool IsAvailable =>
            _session.State == HeyCyanState.Connected &&
            _bt.HasEndpointWithMac(_session.Device?.Address);

        public bool IsPlaying => _bt.IsPlaying;

        public event EventHandler? Disconnected;

        public HeyCyanAudioOutputProvider(
            IHeyCyanGlassesSession session,
            IBluetoothAudioOutputProvider bt,
            ILogger<HeyCyanAudioOutputProvider> log)
        {
            _session = session;
            _bt      = bt;
            _log     = log;

            _bt.Disconnected      += OnBtDisconnected;
            _session.StateChanged += OnSessionStateChanged;
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            var mac = _session.Device?.Address
                ?? throw new InvalidOperationException("HeyCyan glasses not connected.");

            if (!_bt.HasEndpointWithMac(mac))
                throw new InvalidOperationException(
                    $"No BT render endpoint matching glasses MAC {mac}. " +
                    "Ensure the glasses are paired as a BT audio device on this phone.");

            await _bt.SelectEndpointByMacAsync(mac, ct).ConfigureAwait(false);
            await _bt.StartAsync(ct).ConfigureAwait(false);
            _log.LogInformation("HeyCyan speaker started (mac={Mac}).", mac);
        }

        public Task PlayChunkAsync(byte[] pcm16, CancellationToken ct = default) =>
            _bt.PlayChunkAsync(pcm16, ct);

        public Task StopAsync(CancellationToken ct = default) => _bt.StopAsync(ct);

        private void OnBtDisconnected(object? _, EventArgs __) =>
            Disconnected?.Invoke(this, EventArgs.Empty);

        private void OnSessionStateChanged(object? _, HeyCyanState state)
        {
            if (state != HeyCyanState.Connected && _bt.IsPlaying)
            {
                _log.LogInformation("HeyCyan session left Connected ({State}); stopping speaker.", state);
                _ = _bt.StopAsync(CancellationToken.None);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _bt.Disconnected      -= OnBtDisconnected;
            _session.StateChanged -= OnSessionStateChanged;
            if (_bt is IAsyncDisposable d) await d.DisposeAsync();
        }
    }
    ```

3. Register in DI alongside Wave 1:

    ```csharp
    services.AddSingleton<HeyCyanAudioOutputProvider>();
    services.AddSingleton<IAudioOutputProvider>(sp =>
        sp.GetRequiredService<HeyCyanAudioOutputProvider>());
    ```

4. Reuse the same MAC normalization helper across input + output providers
   to avoid drift. Recommended location:
   `src/BodyCam/Services/Audio/BluetoothMacNormalizer.cs`.

5. Document A2DP codec expectations in xmldoc on the class:
   - **SBC** is the only guaranteed codec.
   - **AAC / aptX / LDAC** are negotiated by the OS; we never assume them.
   - Wave 4 surfaces whatever was actually negotiated.

## Verify

- [ ] `IsAvailable` is `false` until QCSDK is `Connected` **and** a render
      endpoint with the matching MAC is enumerated.
- [ ] `StartAsync` throws when no MAC-matched render endpoint exists.
- [ ] `StartAsync` calls `SelectEndpointByMacAsync` **before** the inner
      `StartAsync`.
- [ ] `PlayChunkAsync` round-trips PCM16 24 kHz mono to the generic
      provider with no transformation.
- [ ] `Disconnected` fires when the inner provider raises it.
- [ ] On `StateChanged → Disconnecting | Disconnected`, playback stops.
- [ ] `DisposeAsync` unhooks every subscription.
- [ ] DI returns the same instance for `HeyCyanAudioOutputProvider` and the
      `IAudioOutputProvider` registration.
- [ ] Manual smoke: with glasses connected, `AudioOutputManager.ActiveProviderId`
      becomes `"heycyan-glasses"` after Wave 3 router runs, and TTS playback
      is audible from the glasses.
