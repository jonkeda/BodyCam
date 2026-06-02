# M33 Phase 5 — Wave 1: `OpusOggWrapper`

Parent: [../phase5-recorded-media.md](../phase5-recorded-media.md)
Siblings: [wave2-recorded-media-service.md](wave2-recorded-media-service.md)
· [wave3-mp4-sidecar-metadata.md](wave3-mp4-sidecar-metadata.md)
· [wave4-media-gallery-page.md](wave4-media-gallery-page.md)
· [wave5-m16-dictation-hook.md](wave5-m16-dictation-hook.md)
· [wave6-tests.md](wave6-tests.md)

## Goal

Convert raw glasses `.opus` byte streams into a playable Ogg/Opus container.
The HeyCyan firmware emits **headerless 40-byte raw OPUS packets** (the
official app uses `OpusManager hasHead=false, packetSize=40`), so the OS
audio layer rejects them as-is. This wrapper sniffs the framing, then
emits a valid Ogg stream that `MediaPlayer` (Android) and `AVPlayer` (iOS)
can decode without modification.

Reference: `Alternative-HeyCyan-App-and-SDK/android/AGENTS.md`
("OPUS recordings" section) — the source of every framing rule below.

## Steps

1. **Create the namespace folder** `src/BodyCam/Services/Glasses/HeyCyan/Media/`
   if it does not yet exist (Phase 2 only created `…/HeyCyan/`).

2. **Add `OpusFraming.cs`** — enum with five values: `OggContainer`,
   `FixedPacket40`, `LengthPrefixedU16Le`, `LengthPrefixedU16Be`,
   `LengthPrefixedU8`, `Unknown`. The first is the pass-through case
   (input already starts with the ASCII bytes `OggS`). The middle three
   are heuristics for headerless raw streams. `Unknown` means "treat as
   `FixedPacket40` (the official-app default)".

3. **Add `OpusOggWrapper.cs`** with the public API from the phase doc:

    ```csharp
    namespace BodyCam.Services.Glasses.HeyCyan.Media;

    public static class OpusOggWrapper
    {
        public const int DefaultSampleRate = 16000;
        public const int DefaultChannels   = 1;
        public const int DefaultPacketSize = 40;

        public static OpusFraming Detect(ReadOnlySpan<byte> raw);

        public static byte[] WrapToOgg(
            ReadOnlySpan<byte> raw,
            OpusFraming framing,
            int sampleRate = DefaultSampleRate,
            int channels   = DefaultChannels,
            int packetSize = DefaultPacketSize);

        public static byte[] AutoWrap(ReadOnlySpan<byte> raw)
            => WrapToOgg(raw, Detect(raw));
    }
    ```

4. **Implement `Detect`** in this exact order — the **OggS shortcut must
   come first** so we never re-wrap a real Ogg stream:

   1. If `raw.Length >= 4` and the first four bytes are `O g g S`
      (0x4F 0x67 0x67 0x53), return `OggContainer`.
   2. Try `LengthPrefixedU16Le`: walk the buffer reading a `u16` little-
      endian length, skipping that many bytes, until either EOF (success)
      or a length that overruns the buffer / exceeds 8 KiB (fail). Require
      at least 3 successful packets before accepting.
   3. Same walk for `LengthPrefixedU16Be`.
   4. Same walk for `LengthPrefixedU8` (length byte ≤ 255).
   5. If `raw.Length % 40 == 0` and `raw.Length >= 40`, return
      `FixedPacket40`.
   6. Otherwise return `Unknown` — caller treats this as `FixedPacket40`
      (matches official-app behavior).

5. **Implement an internal `OggWriter`** helper. It emits, in order:

    - **Page 1** — `OpusHead` identification header (19 bytes payload):
      magic `"OpusHead"`, version `1`, channel count, pre-skip `3840`,
      input sample rate (the original device rate, e.g. 16000), output
      gain `0`, channel-mapping family `0`. Page header flags `0x02`
      (BOS = beginning of stream). Granule position `0`.
    - **Page 2** — `OpusTags` comment header: magic `"OpusTags"`, vendor
      string `"BodyCam"`, zero user comments. Granule position `0`.
    - **Pages 3..N** — one Ogg page per Opus packet (or per small batch;
      single-packet pages are simpler and still valid). Granule
      position must be expressed at the **48 kHz Opus reference rate**,
      not the source rate: each 20 ms packet advances the granule by
      `48000 * 0.020 = 960` samples. Set `0x04` (EOS) on the final page.

    Use a stable serial number (e.g. hash of source length, or `1` — the
    spec only requires uniqueness across multiplexed streams, and we are
    single-stream). CRC-32 each page using the Ogg-specific polynomial
    (`0x04C11DB7`, no reflect, init `0`).

6. **Implement `WrapToOgg`** dispatch:

    - `OggContainer` → return `raw.ToArray()` unchanged.
    - `FixedPacket40` / `Unknown` → split `raw` into chunks of
      `packetSize` bytes (last partial chunk dropped — matches the
      official app's behavior of discarding trailing bytes when the
      glasses flush mid-packet).
    - `LengthPrefixedU16Le` / `…U16Be` / `…U8` → walk the prefix lengths
      and slice out the packet payloads.

   Then feed each packet into `OggWriter` and return the buffered bytes.

7. **Defensive caps** to avoid memory blow-ups on malformed input:

    - Max wrapped output `= raw.Length * 2 + 64 KiB`. If exceeded, fall
      back to `FixedPacket40` and re-encode (this catches a heuristic
      that started succeeding but later overran).
    - Reject `packetSize` outside `[1, 8192]`.
    - Reject `channels` outside `[1, 2]`.

8. **DI / consumers** — no DI registration needed; the class is `static`.
   Wave 2 (`HeyCyanRecordedMediaService`) calls `AutoWrap` directly when
   classifying a file as `RecordedMediaKind.Audio`.

## Verify

- [ ] `Detect` on a buffer starting with `"OggS"` returns `OggContainer`
      and `WrapToOgg` returns the input array reference-equal-content
      (byte-for-byte) without re-wrapping.
- [ ] `Detect` on `N * 40` random bytes returns `FixedPacket40`.
- [ ] `Detect` on a synthetic length-prefixed (u16 LE) stream of 5
      packets of varying sizes returns `LengthPrefixedU16Le`.
- [ ] `WrapToOgg(FixedPacket40)` output begins with `OggS` and contains
      exactly **two** header pages (`OpusHead`, `OpusTags`) followed by
      `raw.Length / 40` data pages.
- [ ] Granule position on the final data page equals
      `(packetCount) * 960` for 20 ms packets at 48 kHz reference.
- [ ] CRC-32 on every emitted page validates with an independent Ogg
      page parser (the Wave 6 test fixture parser).
- [ ] Wrapping 1 KiB of pure random garbage produces a structurally-
      valid Ogg stream (parser reads all pages, even though the audio
      is noise).
- [ ] `AutoWrap` is allocation-bounded: wrapping 1 MiB of raw packets
      allocates < 3 MiB of managed memory (verified in benchmark, not
      a hard test gate).
- [ ] No reference to platform assemblies — the class is pure managed
      code and lives in the shared `BodyCam` assembly.
