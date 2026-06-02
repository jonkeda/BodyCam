# Phase 3 / Wave 3 — Auto-Routing Service

**Parent:** [../phase3-audio-providers.md](../phase3-audio-providers.md) ·
**Siblings:** [Wave 1](wave1-heycyan-audio-input-provider.md) ·
[Wave 2](wave2-heycyan-audio-output-provider.md) ·
[Wave 4](wave4-a2dp-codec-verification.md) ·
[Wave 5](wave5-tests.md)

## Goal

The Wave 1 + Wave 2 providers are *passive* — they only describe themselves.
This wave adds `HeyCyanAudioRouter`, a singleton that subscribes to
`IHeyCyanGlassesSession.StateChanged` and flips the active providers on
`AudioInputManager` (M12) and `AudioOutputManager` (M13) so that:

- On `Connected` → live conversation audio routes to `"heycyan-glasses"`.
- On `Disconnected` (or any non-`Connected` state) → audio falls back to
  whatever was active before the glasses connected. If nothing was
  remembered, fall back to the phone's `"platform"` provider.

The router must be **idempotent** across rapid connect/disconnect cycles,
must not leak handlers, and must never block the SDK's `StateChanged`
dispatcher (use `async void` only at the top-level subscriber, do all work
inside a try/catch).

## Steps

1. Create `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioRouter.cs`:

    ```csharp
    namespace BodyCam.Services.Glasses.HeyCyan;

    public sealed class HeyCyanAudioRouter : IAsyncDisposable
    {
        private readonly IHeyCyanGlassesSession _session;
        private readonly AudioInputManager _input;
        private readonly AudioOutputManager _output;
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
            ILogger<HeyCyanAudioRouter> log)
        {
            _session = session;
            _input   = input;
            _output  = output;
            _log     = log;

            _session.StateChanged += OnStateChanged;
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
                    await _input .SetActiveProviderAsync("heycyan-glasses").ConfigureAwait(false);
                    await _output.SetActiveProviderAsync("heycyan-glasses").ConfigureAwait(false);
                    _log.LogInformation(
                        "Routed live audio to HeyCyan glasses (mac={Mac}, restoreIn={In}, restoreOut={Out}).",
                        _session.Device?.Address, _previousInputId, _previousOutputId);
                    break;

                case HeyCyanState.Disconnected:
                case HeyCyanState.Disconnecting:
                    var inFallback  = _previousInputId  ?? "platform";
                    var outFallback = _previousOutputId ?? "platform";
                    await _input .SetActiveProviderAsync(inFallback ).ConfigureAwait(false);
                    await _output.SetActiveProviderAsync(outFallback).ConfigureAwait(false);
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

        public ValueTask DisposeAsync()
        {
            _session.StateChanged -= OnStateChanged;
            _gate.Dispose();
            return default;
        }
    }
    ```

   Notes on the design:
   - `??=` ensures repeated `Connected` events don't overwrite the
     pre-glasses snapshot with `"heycyan-glasses"` (which would defeat
     fallback).
   - `TransferMode` is **not** treated as a disconnect — the QCSDK enters
     transfer mode for ~seconds during file pulls (Phase 5); audio routing
     should remain on the glasses.
   - The `SemaphoreSlim` serializes connect/disconnect so a fast toggle
     can't interleave provider switches.

2. Register in DI as a singleton resolved at startup so its constructor
   subscribes to `StateChanged` immediately:

    ```csharp
    services.AddSingleton<HeyCyanAudioRouter>();
    // In MauiProgram.cs after Build():
    var app = builder.Build();
    _ = app.Services.GetRequiredService<HeyCyanAudioRouter>();
    ```

   Or use `IHostedService` if BodyCam adopts it in Phase 1.

3. Confirm `AudioInputManager` / `AudioOutputManager` already expose:
   - `string ActiveProviderId { get; }`
   - `Task SetActiveProviderAsync(string providerId)` that no-ops when the
     id is already active and throws when the id is unknown.
   If not, add them in M12/M13 alongside this wave — they are required
   contracts.

4. Define the **fallback policy** explicitly in the class xmldoc:
   - First connect: snapshot whatever was active (typically `"platform"`).
   - On disconnect: restore the snapshot, then clear it.
   - If a user manually changes the provider while glasses are connected,
     the snapshot is **not** updated — disconnect still restores the
     pre-glasses choice. Document this; it matches user expectations
     (taking the glasses off should put me back on the phone).

5. Add a one-line entry in `docs/glasses-audio.md` describing the
   auto-routing behavior so users understand why their audio device
   changes when they put the glasses on.

## Verify

- [ ] On `Connected`, `AudioInputManager.ActiveProviderId == "heycyan-glasses"`.
- [ ] On `Connected`, `AudioOutputManager.ActiveProviderId == "heycyan-glasses"`.
- [ ] On `Disconnected`, both managers return to whatever was active before
      the glasses connected (or `"platform"` if nothing was captured).
- [ ] Repeated `Connected → Disconnected → Connected` cycles do not
      collapse the snapshot to `"heycyan-glasses"`.
- [ ] `TransferMode` does **not** flip routing back to the phone.
- [ ] Exceptions inside `SetActiveProviderAsync` are logged and do not
      crash the SDK callback thread.
- [ ] `DisposeAsync` unhooks `StateChanged` and disposes the gate.
- [ ] An in-flight Realtime conversation continues without dropping frames
      across the routing flip (verified end-to-end in Phase 7).
- [ ] No handler leaks observed after 100 connect/disconnect cycles in tests.
