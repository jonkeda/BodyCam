# M33 Phase 5 — Wave 6: Tests

Parent: [../phase5-recorded-media.md](../phase5-recorded-media.md)
Siblings: [wave1-opus-ogg-wrapper.md](wave1-opus-ogg-wrapper.md)
· [wave2-recorded-media-service.md](wave2-recorded-media-service.md)
· [wave3-mp4-sidecar-metadata.md](wave3-mp4-sidecar-metadata.md)
· [wave4-media-gallery-page.md](wave4-media-gallery-page.md)
· [wave5-m16-dictation-hook.md](wave5-m16-dictation-hook.md)

## Goal

Lock down every behavioral contract from Waves 1–5 with deterministic
unit tests, in-memory integration tests, and a real-glasses end-to-end
test gated behind the `RequiresGlasses` trait.

Stack: xUnit + FluentAssertions (project standard).

## Steps

1. **Fixtures** — drop binary fixtures under
   `src/BodyCam.Tests/Fixtures/HeyCyan/Media/`:

    - `fixed-packet-40.bin` — `100 * 40 = 4000` bytes of synthetic OPUS-
      shaped packets (any bytes; the wrapper does not validate Opus
      payload contents).
    - `already-ogg.opus` — a real, valid Ogg/Opus capture (≥ 200 ms).
      Source from `ffmpeg -f lavfi -i sine=f=440:d=0.5 -c:a libopus
      already-ogg.opus`.
    - `len-prefix-u16le.bin` — synthesized in code or pre-baked: 5
      packets of varying sizes with `u16` LE length prefixes.
    - `sample.jpg` — small (~10 KiB) JPG.
    - `sample.mp4` — small (~50 KiB) MP4.
    - `media.config` — text fixture:
      `IMG_0001.jpg\r\nVID_0001.mp4\nAUD_0001.opus\n\n` (deliberately
      mixed line endings + trailing blank).

   Mark all as `EmbeddedResource` in the test csproj so they ship with
   the assembly.

2. **`OpusOggWrapperTests`** in
   `src/BodyCam.Tests/Services/Glasses/HeyCyan/Media/OpusOggWrapperTests.cs`:

    ```csharp
    public class OpusOggWrapperTests
    {
        [Fact] public void Detect_OggMagic_ReturnsOggContainer();
        [Fact] public void Detect_FortyByteMultiple_ReturnsFixedPacket40();
        [Fact] public void Detect_LenPrefixU16Le_ReturnsLenPrefixU16Le();
        [Fact] public void WrapToOgg_OggInput_ReturnsByteForByteIdentical();
        [Fact] public void WrapToOgg_FixedPacket40_StartsWithOggS();
        [Fact] public void WrapToOgg_FixedPacket40_HasOpusHeadAndOpusTags();
        [Fact] public void WrapToOgg_FixedPacket40_PageCountMatchesPackets();
        [Fact] public void WrapToOgg_LastPage_HasEosFlag();
        [Fact] public void WrapToOgg_GranulePosition_AdvancesBy960Per20msPacket();
        [Fact] public void AutoWrap_GarbageInput_ProducesParseableOggStream();
        [Fact] public void WrapToOgg_AllPagesPassCrc32();
    }
    ```

   Include a tiny in-test Ogg page parser (~80 LOC) that walks `OggS`
   pages and validates the CRC. **Do not** depend on a third-party Ogg
   library — wrapping logic must round-trip through a parser written
   from scratch in the same PR.

3. **`RecordedMediaClassifierTests`** — table-driven `[Theory]` over the
   classification table from Wave 2 (`.jpg`, `.JPEG`, `.png`, `.mp4`,
   `.MOV`, `.opus`, `.ogg`, `.bin`, `""`).

4. **`SidecarSerializerTests`**:

    - Round-trip a sample sidecar through `JsonSerializer`.
    - Field order matches the record declaration.
    - `Schema = 1` is emitted.
    - Atomic write writes via `<file>.tmp` + rename (use a fake file
      system to assert).
    - Path resolves under `AppDataDirectory/RecordedMedia/Sidecars/`.

5. **`MediaGalleryViewModelTests`**:

    - `Filter = "Photo"` populates only photo items into `FilteredItems`.
    - `RefreshCommand` toggles `IsImporting` true → false and updates
      `ImportProgress` monotonically using a fake
      `IHeyCyanRecordedMediaService` that yields a scripted progress
      sequence.
    - `OpenItemCommand` for `Photo` navigates to `ImageViewerPage`,
      `Video` invokes the launcher, `Audio` navigates to
      `AudioPlayerPage`. Mock `IShellNavigation` / `ILauncher`.
    - VM uses `SetProperty` only — assert by spying on
      `PropertyChanged` event payloads.

6. **`HeyCyanRecordedMediaServiceTests`** in
   `src/BodyCam.IntegrationTests/Services/Glasses/HeyCyan/Media/`:

    - Use a `FakeHeyCyanMediaTransfer` that serves `media.config` and
      file payloads from in-memory streams.
    - Use a `FakeMediaStore` that records every `Save*` and `Exists`
      call.
    - Assert: enumeration parses the fixture `media.config` correctly
      (3 items, mixed kinds).
    - `ImportAllAsync` calls `SaveImageAsync`, `SaveVideoAsync`,
      `SaveAudioAsync` in the right order.
    - The audio call's stream content begins with `OggS` (Wave 1 ran).
    - The image and video calls' streams round-trip bytes-for-bytes.
    - Re-running `ImportAllAsync` with `ExistsAsync` returning true
      issues zero `Save*` calls.
    - Progress reporter receives one update per file with monotonic
      `Completed`.
    - `DeleteRemoteAsync` is **never** called by tests (assert call
      count == 0).

7. **`HeyCyanDictationHookTests`**:

    - With `FeedVoiceNotesToDictation = false`, no
      `IDictationRegistry.Register` call.
    - With flag true and registry present, audio import → exactly one
      `Register`.
    - Same audio re-imported → still exactly one `Register`.
    - With registry `null` (M16 absent), startup completes without
      throwing.

8. **`BodyCam.RealTests`** — gated, opt-in physical-device test:

    ```csharp
    [Trait("RequiresGlasses", "true")]
    public class HeyCyanRecordedMediaE2ETests
    {
        [Fact]
        public async Task ConnectRecordImport_VoiceNote_PlaysBack()
        {
            // 1. Connect to glasses (Phase 1 session).
            // 2. await session.StartAudioAsync(); await Task.Delay(5_000);
            //    await session.StopAudioAsync();
            // 3. await using var transfer = await session.EnterTransferModeAsync(ct);
            // 4. var imports = await media.ImportAllAsync(...).ToListAsync(ct);
            // 5. The newest .ogg import opens via MediaPlayer/AVPlayer
            //    (smoke check by calling Prepare/Play; success = no
            //    "unsupported format" exception).
        }
    }
    ```

   Skip by default in CI; run with
   `dotnet test --filter "Trait=RequiresGlasses"`.

9. **CI wiring** — add the new test classes to
   `BodyCam.Tests` / `BodyCam.IntegrationTests`. The fixture binaries
   should be tracked via Git LFS if any individual fixture exceeds 100
   KiB; otherwise plain Git.

## Verify

- [ ] `dotnet test src/BodyCam.Tests` passes locally with all Wave 6
      unit tests.
- [ ] `dotnet test src/BodyCam.IntegrationTests` passes with the in-
      memory transfer integration tests.
- [ ] `BodyCam.RealTests` E2E passes on a physical glasses device:
      record → import → playback succeeds without "unsupported format".
- [ ] No test depends on a real network or real BLE except those tagged
      `RequiresGlasses`.
- [ ] Test fixtures are byte-stable (committed binaries are
      deterministic, not regenerated each run).
- [ ] Coverage of the `OpusOggWrapper` public surface is ≥ 90 % lines
      (measured via `coverlet`, not a hard gate).
- [ ] No test references `CommunityToolkit.Mvvm`; VM tests use the
      `BodyCam.Mvvm` `RelayCommand` / `AsyncRelayCommand` directly.
- [ ] All Phase 5 overall verify items in
      [../phase5-recorded-media.md](../phase5-recorded-media.md) have a
      corresponding test in this wave (one-to-one mapping documented in
      the test class XML doc comments).
