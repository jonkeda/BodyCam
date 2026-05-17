# Phase 3 / Wave 4 — A2DP Codec Verification & Diagnostics

**Parent:** [../phase3-audio-providers.md](../phase3-audio-providers.md) ·
**Siblings:** [Wave 1](wave1-heycyan-audio-input-provider.md) ·
[Wave 2](wave2-heycyan-audio-output-provider.md) ·
[Wave 3](wave3-auto-routing-service.md) ·
[Wave 5](wave5-tests.md)

## Goal

We do not control codec negotiation — the OS BT stack picks an A2DP codec
(SBC/AAC/aptX/LDAC) and an HFP codec (CVSD/mSBC) based on what both ends
support. But we **do** want to surface what was actually negotiated, so:

- The diagnostics page can show the user "you're on SBC mono 16 kHz" vs.
  "AAC stereo 44.1 kHz".
- Support tickets carry the codec details automatically.
- The SBC-floor contract (no aptX/LDAC promises) is *visible*, not just a
  README footnote.

This wave is **read-only diagnostics**. It must never re-negotiate, force,
or block on codec discovery — if the platform refuses to expose it, we
report `null` and move on.

## Steps

1. Define the cross-platform contract in
   `src/BodyCam/Services/Glasses/HeyCyan/IHeyCyanAudioDiagnostics.cs`:

    ```csharp
    namespace BodyCam.Services.Glasses.HeyCyan;

    public sealed record HeyCyanAudioRouteInfo(
        string  InputProviderId,
        string  OutputProviderId,
        string? NegotiatedA2dpCodec, // "SBC" | "AAC" | "aptX" | "aptX-HD" | "LDAC" | null
        int     SampleRateHz,        // 0 if unknown
        int     Channels,            // 0 if unknown
        string? HfpCodec);           // "CVSD" | "mSBC" | null

    public interface IHeyCyanAudioDiagnostics
    {
        HeyCyanAudioRouteInfo? Current { get; }
        event EventHandler<HeyCyanAudioRouteInfo>? Updated;

        Task RefreshAsync(CancellationToken ct = default);
    }
    ```

2. Implement the cross-platform shell in
   `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioDiagnostics.cs`. It
   listens to `IHeyCyanGlassesSession.StateChanged` and asks the
   platform-specific helper to refresh on `Connected`:

    ```csharp
    public sealed class HeyCyanAudioDiagnostics : IHeyCyanAudioDiagnostics, IAsyncDisposable
    {
        private readonly IHeyCyanGlassesSession _session;
        private readonly IHeyCyanCodecProbe _probe; // platform-specific, see step 3
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
            _session.StateChanged += async (_, s) =>
            {
                if (s == HeyCyanState.Connected)
                    await RefreshAsync().ConfigureAwait(false);
            };
        }

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            try
            {
                var mac = _session.Device?.Address;
                if (string.IsNullOrEmpty(mac)) { Current = null; return; }
                var info = await _probe.ProbeAsync(mac, ct).ConfigureAwait(false);
                Current = info;
                if (info != null) Updated?.Invoke(this, info);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Codec probe failed; diagnostics will report null.");
                Current = null;
            }
        }

        public ValueTask DisposeAsync() => default;
    }

    internal interface IHeyCyanCodecProbe
    {
        Task<HeyCyanAudioRouteInfo?> ProbeAsync(string mac, CancellationToken ct);
    }
    ```

3. Implement platform-specific `IHeyCyanCodecProbe`:

   **Android** — `src/BodyCam/Platforms/Android/HeyCyan/HeyCyanCodecProbe.Android.cs`:
   - Acquire `BluetoothA2dp` profile via `BluetoothAdapter.GetProfileProxy(...)`.
   - Read `BluetoothA2dp.GetCodecStatus(BluetoothDevice)` (API 28+).
     Map `BluetoothCodecConfig.SourceCodecType` → `"SBC" | "AAC" | "aptX" | "aptX-HD" | "LDAC"`.
   - Sample rate / channel count come from the same `BluetoothCodecConfig`.
   - HFP: `BluetoothHeadset.IsAudioConnected` + reflection on
     `getAudioState()` / `getActiveDevice()`; mSBC vs CVSD is exposed via
     hidden `BluetoothHeadset` APIs — best-effort, return `null` if
     reflection fails.
   - Wrap `RequiresApi(28)` and degrade gracefully on older devices.

   **iOS** — `src/BodyCam/Platforms/iOS/HeyCyan/HeyCyanCodecProbe.iOS.cs`:
   - iOS deliberately hides codec details from third-party apps.
   - Return `new HeyCyanAudioRouteInfo("heycyan-glasses", "heycyan-glasses",
     NegotiatedA2dpCodec: null, SampleRateHz: 0, Channels: 0, HfpCodec: null)`.
   - Do **not** use private API. Document this limitation in the
     diagnostics view.

4. Register both implementations:

    ```csharp
    services.AddSingleton<HeyCyanAudioDiagnostics>();
    services.AddSingleton<IHeyCyanAudioDiagnostics>(sp =>
        sp.GetRequiredService<HeyCyanAudioDiagnostics>());
    #if ANDROID
    services.AddSingleton<IHeyCyanCodecProbe, HeyCyanCodecProbe>();
    #elif IOS
    services.AddSingleton<IHeyCyanCodecProbe, HeyCyanCodecProbe>();
    #else
    services.AddSingleton<IHeyCyanCodecProbe, NullCodecProbe>();
    #endif
    ```

5. Add a small read-only panel to the existing glasses settings page that
   binds to `IHeyCyanAudioDiagnostics.Current`. Show:
   - "A2DP: SBC · 44.1 kHz · stereo" or "A2DP: unknown".
   - "HFP: mSBC" / "HFP: CVSD" / "HFP: unknown".
   - A small ⓘ tooltip linking to the SBC-minimum policy.

6. Update `docs/glasses-audio.md`:
   - "**Codec policy.** HeyCyan glasses are guaranteed to work over **SBC**
     A2DP and **CVSD** HFP. AAC, aptX, aptX-HD, LDAC, and mSBC may be
     negotiated but are not promised. iOS does not expose negotiated
     codecs to third-party apps; the diagnostics panel will show 'unknown'
     on iOS."

## Verify

- [ ] `IHeyCyanAudioDiagnostics.Current` is populated within ~1 s of
      `Connected` on Android (when `BluetoothA2dp.GetCodecStatus` is
      available).
- [ ] iOS path returns a `HeyCyanAudioRouteInfo` with all codec fields
      `null`/0 without throwing.
- [ ] Pre-API-28 Android returns `null` codec without crashing.
- [ ] `Updated` fires whenever `RefreshAsync` produces a non-null result.
- [ ] Settings page binds live to `Current` and reflects reconnects.
- [ ] `docs/glasses-audio.md` explicitly states the SBC/CVSD floor and the
      iOS limitation.
- [ ] No use of iOS private API (App Store compatible).
- [ ] Probe failures log a warning and set `Current = null` — they do not
      block routing or crash the app.
