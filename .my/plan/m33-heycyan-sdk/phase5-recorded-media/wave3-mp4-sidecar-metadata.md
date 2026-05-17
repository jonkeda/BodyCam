# M33 Phase 5 — Wave 3: MP4 Sidecar Metadata

Parent: [../phase5-recorded-media.md](../phase5-recorded-media.md)
Siblings: [wave1-opus-ogg-wrapper.md](wave1-opus-ogg-wrapper.md)
· [wave2-recorded-media-service.md](wave2-recorded-media-service.md)
· [wave4-media-gallery-page.md](wave4-media-gallery-page.md)
· [wave5-m16-dictation-hook.md](wave5-m16-dictation-hook.md)
· [wave6-tests.md](wave6-tests.md)

## Goal

The glasses' MP4 and OPUS files carry no useful metadata (no GPS, no MAC,
no recording timestamp readable by the OS). Write a `.bodycam.json`
sidecar next to every imported video and audio file so later analytics —
including the optional Wave 5 M16 dictation hook — can dedup, re-attribute,
and relate files back to a specific glasses device.

This is **provenance metadata only**. We do not modify the MP4 muxer or
re-encode the OPUS stream.

## Steps

1. **Add the record** in
   `src/BodyCam/Services/Glasses/HeyCyan/Media/RecordedMediaSidecar.cs`:

    ```csharp
    namespace BodyCam.Services.Glasses.HeyCyan.Media;

    public sealed record RecordedMediaSidecar(
        int Schema,                       // always 1 for now
        string SourceFileName,            // glasses-side name, e.g. "VID_0042.mp4"
        string GlassesMacAddress,         // from IHeyCyanGlassesSession.Device.Address
        DateTimeOffset ImportedAt,
        DateTimeOffset? GlassesTimestamp, // from RecordedMediaItem if known
        TimeSpan? Duration,               // probed; null on unsupported platforms
        long SizeBytes,
        string Sha256);                   // hex, lower-case, of the imported bytes
    ```

2. **Add `ISidecarWriter`** abstraction (allows test substitution):

    ```csharp
    public interface ISidecarWriter
    {
        Task<string> WriteAsync(
            string mediaLocalUri,
            RecordedMediaSidecar sidecar,
            CancellationToken ct);
    }
    ```

3. **Implement `JsonSidecarWriter`** with `System.Text.Json` and the
   `BodyCam.Json.BodyCamJsonContext` source-gen context (or extend it).
   Output indented JSON with `WriteIndented = true` for hand-editability.

4. **Atomic write** — write to `<final>.tmp`, `Flush + Dispose`, then
   `File.Move(tmp, final, overwrite: true)`. On Android the `final` path
   resolves the `content://` URI to the matching app-private file under
   `FileSystem.Current.AppDataDirectory/RecordedMedia/Sidecars/`. We do
   not write sidecars *into* `MediaStore` — they are app-private.

5. **Sidecar location**:

    - Path is always
      `FileSystem.Current.AppDataDirectory/RecordedMedia/Sidecars/<sha256>.bodycam.json`.
    - Keying by SHA-256 (not by filename) gives instant dedup: re-importing
      the same bytes overwrites the same sidecar with refreshed metadata.

6. **SHA-256 capture**. Modify `HeyCyanRecordedMediaService.ImportAsync`
   (Wave 2) to wrap the download stream in a `CryptoStream` with
   `IncrementalHash.CreateHash(HashAlgorithmName.SHA256)`. Read the final
   hash after the media-store save completes. Do **not** re-read the file
   from the platform store — on Android scoped storage that is expensive
   and sometimes denied.

7. **Duration probe** — platform-specific, called from the sidecar writer
   right before serialization. Failures are non-fatal; the field stays
   `null`:

    - **Android** — `Android.Media.MediaMetadataRetriever`.
      `SetDataSource(context, uri)` then
      `ExtractMetadata(MetadataKeyDuration)` → milliseconds → `TimeSpan`.
    - **iOS** — `AVFoundation.AVAsset.FromUrl(NSUrl.FromString(localUri))`
      → `Duration.Seconds` → `TimeSpan`.
    - **Windows / unsupported** — return `null`. Document this; the
      gallery page degrades gracefully (Wave 4).

8. **Wire-up in Wave 2** — at the end of `ImportAsync`, if the kind is
   `Video` or `Audio`, build the sidecar and call
   `await _sidecarWriter.WriteAsync(...)`. Photos do not get sidecars by
   default (the EXIF in the JPG already carries the relevant fields, and
   the M16 hook is audio-only).

9. **MAC address source** — `_session.Device?.Address` (Phase 1 model
   exposes this). If `null` (only happens during disconnect races), use
   `"unknown"` literal — never throw.

10. **Schema versioning** — keep `Schema = 1` constant. If we add fields
    later, bump to `2` and ensure `JsonSerializerOptions` uses
    `IgnoreUnknownProperties = true` on read so old sidecars round-trip.

## Verify

- [ ] Sidecar JSON contains exactly the fields in the record, in the
      order declared, with `"schema": 1`.
- [ ] Re-importing the same bytes overwrites the same `<sha256>.bodycam.json`
      file (no proliferation).
- [ ] Sidecar write is atomic — a kill mid-write leaves either the old
      file intact or the new file fully written; never a partial.
- [ ] `GlassesMacAddress` matches `IHeyCyanGlassesSession.Device.Address`
      verbatim (with colons), or `"unknown"` if the session is null.
- [ ] On Android, `Duration` is populated for a known-good MP4.
- [ ] On iOS, `Duration` is populated for the same MP4.
- [ ] On Windows, `Duration` is `null` and no exception is thrown.
- [ ] No sidecar is written for `RecordedMediaKind.Photo` imports.
- [ ] The SHA-256 captured during import equals an out-of-band SHA-256 of
      the imported bytes (test computes it both ways).
