# Phase 2 — Resampling

**Status:** Proposed
**Depends on:** Phase 1 (correctness)
**Sibling phases:** [Phase 1](../phase-1-correctness/overview.md), [Phase 3](../phase-3-threading/overview.md), [Phase 4](../phase-4-platform-coverage/overview.md), [Phase 5](../phase-5-polish/overview.md), [Phase 6](../phase-6-observability/overview.md)

---

## Summary

Today every 50 ms mic chunk is resampled twice (24 k → 48 k → AEC → 48 k →
24 k) using [AudioResampler](../../../src/BodyCam/Services/Audio/AudioResampler.cs)
which is plain *linear interpolation*. Linear upsampling leaves a strong
aliased mirror image of the 0–12 kHz speech band reflected into 12–24 kHz;
the WebRTC noise suppressor sees that as broadband noise and partially
suppresses it, dulling consonants and giving voice a "muffled" character.
Round-trip downsampling then folds whatever survives back into the speech
band as low-grade aliasing.

This phase replaces linear interpolation with proper band-limited
resampling (2.1) and, longer-term, removes one of the two resample stages
by running the AEC pipeline at 48 kHz natively (2.2).

---

## 2.1 — Replace linear resampler in the AEC hot path

### The problem with linear interpolation

Linear interpolation between two samples is mathematically equivalent to
convolution with a triangular kernel. The frequency response of that
kernel is `sinc²(f/fs)`, which has its first null at the source Nyquist
frequency and only −13 dB rejection in the first sidelobe. For 24 → 48 k
upsampling that means *anything* in the 6–12 kHz region is mirrored into
12–18 kHz at only ~−13 dB attenuation. Voice has plenty of energy in
4–8 kHz (sibilance, fricatives) — the alias image fills the upper band
with garbage that the AEC and NS treat as real signal.

### Recommended fix

**Option A — Bundle a small polyphase FIR (preferred):** Add a 32-tap
windowed-sinc kernel sized for L=2/M=1 upsampling (24 → 48) and L=1/M=2
downsampling (48 → 24). With a Kaiser β≈8 window this gives ~−80 dB stopband
rejection at a CPU cost of 32 multiply-adds per output sample — about 80 µs
per 50 ms chunk on Android arm64.

**Option B — Expose WebRTC's own resampler:** The `webrtc_audio_processing`
native lib already includes `PushResampler<float>` used internally. Add the
P/Invoke binding alongside the existing surface in
[WebRtcApmInterop](../../../src/BodyCam/Services/Audio/WebRtcApm/WebRtcApmInterop.cs):

```csharp
[DllImport(Lib)] public static extern IntPtr PushResamplerCreate(int srcRate, int dstRate, int channels);
[DllImport(Lib)] public static extern void   PushResamplerDestroy(IntPtr h);
[DllImport(Lib)] public static extern int    PushResamplerResample(IntPtr h, IntPtr src, int srcLen, IntPtr dst, int dstLen);
```

This requires verifying that the prebuilt `.dll` / `.so` / `.dylib`
artifacts shipped in `runtimes/*/native/` actually export those symbols
(some builds strip them). If they do, this is the lowest-risk choice — we
get the exact resampler the APM is tuned against.

**Decision:** start with B. If symbols aren't exported, fall back to A.

### Code sketch (Option A)

New file `src/BodyCam/Services/Audio/PolyphaseFirResampler.cs`:

```csharp
public sealed class PolyphaseFirResampler
{
    private readonly int _l, _m;          // upsample / downsample factors
    private readonly float[] _kernel;     // windowed-sinc, length L*N
    private readonly float[] _history;    // last N input samples

    public PolyphaseFirResampler(int srcRate, int dstRate, int taps = 32) { ... }

    public int Resample(ReadOnlySpan<float> input, Span<float> output) { ... }
}
```

Used by `AecProcessor` instead of `AudioResampler.Resample`. Allocate
once in `Initialize`, reuse the `_history` between calls to avoid
chunk-boundary discontinuities (this also pairs nicely with Phase 1.2's
residual buffer pattern).

Keep [AudioResampler](../../../src/BodyCam/Services/Audio/AudioResampler.cs)
for non-critical paths (Bluetooth 16 k → 24 k upmix in
[AndroidBluetoothAudioProvider](../../../src/BodyCam/Platforms/Android/Audio/AndroidBluetoothAudioProvider.cs)
and [WindowsBluetoothAudioProvider](../../../src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothAudioProvider.cs)).

### Test plan
- New test `PolyphaseFirResamplerTests`:
  - Pass 1 kHz sine at 24 k, upsample to 48 k, FFT — assert image at 47 kHz < −60 dB.
  - Pass full-band white noise, round-trip 24 → 48 → 24, FFT — assert no aliasing > −60 dB.
- Existing `AecProcessorTests` must still pass.
- Integration: 5 minute call recording, listen — voice should sound brighter / less muffled.

### Acceptance
- [ ] Polyphase FIR resampler in place (or WebRTC's PushResampler bound).
- [ ] AEC pipeline uses it instead of `AudioResampler`.
- [ ] FFT test: alias rejection ≥ 60 dB.
- [ ] Subjective listening: voice brightness improved (informal A/B).

---

## 2.2 — Native 48 kHz pipeline; single resample at the API boundary

### Idea

Most platforms can capture and render natively at 48 kHz. The only reason
we run at 24 kHz is the OpenAI Realtime API. Run the entire local pipeline
at 48 kHz and resample exactly once, just before the API send (and just
after the API receive):

```
mic (48k native) → AEC (48k) → resample 48→24 (once) → API
API → resample 24→48 (once) → AEC render ref + speaker (48k)
```

Benefits:

- Two resample stages collapse to one per chunk → halves the resampling
  CPU cost and halves the aliasing budget.
- AEC operates at its preferred native rate (no internal extra resampling
  inside APM).
- Mic capture matches native hardware rate on most devices, removing
  another implicit OS-level resample.

### Refactor

1. Split [AppSettings](../../../src/BodyCam/AppSettings.cs):
   ```csharp
   public int InternalSampleRate { get; set; } = 48000;
   public int ApiSampleRate      { get; set; } = 24000;
   public int SampleRate => InternalSampleRate;  // back-compat shim
   ```

2. Per-platform mic providers open at 48 kHz:
   - [Windows/PlatformMicProvider.cs](../../../src/BodyCam/Platforms/Windows/PlatformMicProvider.cs):
     `WaveFormat = new WaveFormat(48000, 16, 1)`.
   - [Android/PlatformMicProvider.cs](../../../src/BodyCam/Platforms/Android/PlatformMicProvider.cs):
     `AudioRecord(... sampleRate: 48000 ...)`.
   - iOS provider (when added in Phase 4.1) — open at 48 kHz native.

3. Speaker providers open at 48 kHz; nothing else changes since the
   buffered-wave-provider abstraction takes a sample rate at start time.

4. Single resample stage:
   - **Capture path** ([AudioInputManager](../../../src/BodyCam/Services/Audio/AudioInputManager.cs))
     after AEC: resample 48 → 24 once before handing to
     `IRealtimeClient.SendAudioChunkAsync`.
   - **Playback path** ([AudioOutputManager](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs))
     before play: resample 24 → 48 once when the API delivers a chunk;
     send the same 48 k buffer both to `AecProcessor.FeedRenderReference`
     and the speaker provider.

5. `AecProcessor` no longer resamples internally. Initialize APM at
   48 kHz and pass-through. Frame size stays 480 samples = 10 ms.

### Migration risks

- **`ChunkDurationMs`**: 50 ms × 48 kHz × 2 bytes = **4800 bytes/chunk** (was 2400). Audit any `BytesPerChunk` math; in particular [AudioPlaybackTracker](../../../src/BodyCam/Models/RealtimeModels.cs)'s `PlayedMs` computation and any conversation-log byte assumptions.
- **Bluetooth path**: BT SCO is 16 kHz. Existing `AndroidBluetoothAudioProvider` resamples 16 → 24; update to 16 → 48.
- **Tests**: any test fixture that hardcodes 24 k or 2400-byte chunks must be updated.
- **Realtime API**: the `pcm16` content type is fixed at 24 kHz on the wire — verify this assumption in
  [Models/RealtimeModels.cs](../../../src/BodyCam/Models/RealtimeModels.cs). If the API ever supports 48 kHz, drop the second-half resample entirely.

### Sequencing

This is a larger refactor. Do it **after Phase 1 + the relevant pieces of
Phase 3** (capture-thread offload), so the new pipeline lands on a stable
base. Treat 2.2 as its own PR; do not bundle with 2.1.

### Test plan
- Unit: `BytesPerChunk` calculations updated and asserted at 48 k.
- Integration `FullPipelineTests` updated to inject 48 k mic frames.
- End-to-end: voice round-trip latency must not regress > 5 ms vs current 24 k pipeline.
- Audio quality A/B: 1 minute recording at 24 k vs 48 k; expect cleaner highs.

### Acceptance
- [ ] `AppSettings.InternalSampleRate = 48000`, `ApiSampleRate = 24000`.
- [ ] All mic + speaker providers open at 48 kHz.
- [ ] Exactly one resample stage on each direction (at the API boundary).
- [ ] `AecProcessor` is no longer aware of 24 kHz at all.
- [ ] Full pipeline tests pass at 48 kHz.
- [ ] Round-trip latency Δ ≤ +5 ms.

---

## Files touched

### 2.1 (resampler quality)
- New: `src/BodyCam/Services/Audio/PolyphaseFirResampler.cs` (or new bindings in [WebRtcApmInterop.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/WebRtcApmInterop.cs))
- [src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs) — swap resampler.
- New: `src/BodyCam.Tests/Services/Audio/PolyphaseFirResamplerTests.cs`.

### 2.2 (48 kHz pipeline)
- [src/BodyCam/AppSettings.cs](../../../src/BodyCam/AppSettings.cs)
- [src/BodyCam/Models/RealtimeModels.cs](../../../src/BodyCam/Models/RealtimeModels.cs) — playback math.
- [src/BodyCam/Platforms/Windows/PlatformMicProvider.cs](../../../src/BodyCam/Platforms/Windows/PlatformMicProvider.cs)
- [src/BodyCam/Platforms/Windows/WindowsSpeakerProvider.cs](../../../src/BodyCam/Platforms/Windows/WindowsSpeakerProvider.cs)
- [src/BodyCam/Platforms/Android/PlatformMicProvider.cs](../../../src/BodyCam/Platforms/Android/PlatformMicProvider.cs)
- [src/BodyCam/Platforms/Android/PhoneSpeakerProvider.cs](../../../src/BodyCam/Platforms/Android/PhoneSpeakerProvider.cs)
- [src/BodyCam/Platforms/Android/Audio/AndroidBluetoothAudioProvider.cs](../../../src/BodyCam/Platforms/Android/Audio/AndroidBluetoothAudioProvider.cs)
- [src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothAudioProvider.cs](../../../src/BodyCam/Platforms/Windows/Audio/WindowsBluetoothAudioProvider.cs)
- [src/BodyCam/Services/Audio/AudioInputManager.cs](../../../src/BodyCam/Services/Audio/AudioInputManager.cs) — single 48 → 24 resample.
- [src/BodyCam/Services/Audio/AudioOutputManager.cs](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs) — single 24 → 48 resample.
- [src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs](../../../src/BodyCam/Services/Audio/WebRtcApm/AecProcessor.cs) — drop internal resampling.
- Test fixtures across `BodyCam.Tests` and `BodyCam.IntegrationTests`.
