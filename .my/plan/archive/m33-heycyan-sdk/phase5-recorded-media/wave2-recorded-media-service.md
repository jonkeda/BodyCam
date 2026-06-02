# M33 Phase 5 — Wave 2: `HeyCyanRecordedMediaService` + `IMediaStore`

Parent: [../phase5-recorded-media.md](../phase5-recorded-media.md)
Siblings: [wave1-opus-ogg-wrapper.md](wave1-opus-ogg-wrapper.md)
· [wave3-mp4-sidecar-metadata.md](wave3-mp4-sidecar-metadata.md)
· [wave4-media-gallery-page.md](wave4-media-gallery-page.md)
· [wave5-m16-dictation-hook.md](wave5-m16-dictation-hook.md)
· [wave6-tests.md](wave6-tests.md)

## Goal

Bulk-import the recorded `.jpg` / `.mp4` / `.opus` files that accumulate on
the glasses, classify them, normalize audio via Wave 1's
`OpusOggWrapper`, and write them into the platform photo / video / audio
library. Phase 2 already shipped `HeyCyanMediaTransfer` for **single-shot**
snapshot retrieval; this wave **reuses** that exact transport and adds the
bulk-import surface on top of it.

## Steps

1. **Add classification model** in
   `src/BodyCam/Services/Glasses/HeyCyan/Media/RecordedMediaKind.cs`:

    ```csharp
    namespace BodyCam.Services.Glasses.HeyCyan.Media;

    public enum RecordedMediaKind { Photo, Video, Audio, Unknown }

    public sealed record RecordedMediaItem(
        string FileName,
        RecordedMediaKind Kind,
        long? SizeBytes,
        DateTimeOffset? GlassesTimestamp);

    public sealed record ImportedMediaItem(
        RecordedMediaItem Source,
        string LocalUri,
        long BytesWritten,
        TimeSpan TransferTime);

    public sealed record RecordedMediaImportProgress(
        int Completed, int Total, string CurrentFile, long BytesSoFar);
    ```

2. **Add static classifier** `RecordedMediaClassifier.Classify(string fileName)`
   using a case-insensitive extension switch — the table **must agree** with
   the iOS demo's `MediaGalleryViewController` filter:

    | Extension                | Kind    |
    |--------------------------|---------|
    | `.jpg`, `.jpeg`, `.png`  | Photo   |
    | `.mp4`, `.mov`           | Video   |
    | `.opus`, `.ogg`          | Audio   |
    | anything else            | Unknown |

3. **Define `IHeyCyanRecordedMediaService`** in
   `src/BodyCam/Services/Glasses/HeyCyan/Media/IHeyCyanRecordedMediaService.cs`:

    ```csharp
    public interface IHeyCyanRecordedMediaService
    {
        IAsyncEnumerable<RecordedMediaItem> EnumerateAsync(CancellationToken ct);

        IAsyncEnumerable<ImportedMediaItem> ImportAllAsync(
            IProgress<RecordedMediaImportProgress>? progress,
            CancellationToken ct);

        Task<ImportedMediaItem> ImportAsync(
            RecordedMediaItem item, CancellationToken ct);

        Task<bool> DeleteRemoteAsync(string fileName, CancellationToken ct);
    }
    ```

4. **Implement `HeyCyanRecordedMediaService`** in the same folder. Constructor
   takes `IHeyCyanGlassesSession` (Phase 1), `HeyCyanMediaTransfer` (Phase 2),
   `IMediaStore` (this wave), and `ILogger`. Do **not** create a new HTTP
   client — the transfer-mode HTTP client lives inside `HeyCyanMediaTransfer`
   and is already platform-aware (Android `WiFiP2pHttpClient`, iOS
   `HotspotHttpClient`).

5. **`EnumerateAsync` flow**:

    1. If `Session.State != HeyCyanState.TransferMode`, call
       `Session.EnterTransferModeAsync` and dispose at end of enumeration.
    2. Call `HeyCyanMediaTransfer.GetMediaConfigAsync` (already implemented
       in Phase 2) — returns the raw text body of `/files/media.config`.
    3. Split on `\r?\n`, trim whitespace, skip blanks.
    4. For each filename, yield `new RecordedMediaItem(name,
       Classify(name), SizeBytes: null, GlassesTimestamp: null)`. Size is
       only known after a HEAD or GET, and the firmware does not honor
       HEAD — leave it null until import.

6. **`ImportAsync` flow** (single item):

    1. Reject `Unknown` kind with a logged warning + return early (caller
       gets `null` `LocalUri` via a sentinel — or we throw
       `NotSupportedException`; pick throw, simpler).
    2. Skip if `await _store.ExistsAsync(item.FileName, item.Kind, ct)` —
       idempotent re-imports are required so the user can re-run a bulk
       import after a drop.
    3. Open a download stream:
       `await using var src = await _transfer.OpenAsync(item.FileName, ct);`
       (Phase 2 added `OpenAsync` to expose the response stream — if it did
       not, add it now: `GET /files/<name>` with 60 s read timeout for video,
       30 s for everything else.)
    4. Dispatch by kind:
        - `Photo` → `await _store.SaveImageAsync(item.FileName, src, ct);`
        - `Video` → `await _store.SaveVideoAsync(item.FileName, src, ct);`
        - `Audio` → buffer the bytes (.opus files are small, <2 MB
          typically), call `OpusOggWrapper.AutoWrap(buffer)`, rename the
          output to `<basename>.ogg`, then
          `await _store.SaveAudioAsync(oggName, "audio/ogg", new MemoryStream(wrapped), ct);`.
    5. Stopwatch the transfer; return `ImportedMediaItem` with the URI
       returned from the store, byte count, and elapsed time.

7. **`ImportAllAsync` flow**:

    - Materialize the enumeration into a `List<RecordedMediaItem>` first so
      we know `Total` for the progress reporter.
    - Iterate sequentially (one HTTP transfer at a time — the glasses
      cannot serve concurrent connections reliably; matches CyanBridge).
    - Report progress *before* each download (`CurrentFile` = next file)
      and update `BytesSoFar` after each completes.
    - Skip `Unknown` items silently (logged at debug level).
    - Stay inside one transfer-mode session for the whole batch (huge perf
      win; mirrors Phase 2's "warm transfer mode" optimization).

8. **`DeleteRemoteAsync` is opt-in**. Default `BodyCamOptions.HeyCyan
   .DeleteAfterImport = false`. The method maps to a
   `LargeDataHandler.glassesControl` payload — exact opcode per
   `android/AGENTS.md` (currently undocumented; if not yet known, return
   `false` and log "delete-after-import not supported on this firmware").

9. **`IMediaStore` abstraction** in
   `src/BodyCam/Services/Glasses/HeyCyan/Media/IMediaStore.cs`:

    ```csharp
    public interface IMediaStore
    {
        Task<string> SaveImageAsync(string fileName, Stream content, CancellationToken ct);
        Task<string> SaveVideoAsync(string fileName, Stream content, CancellationToken ct);
        Task<string> SaveAudioAsync(string fileName, string mimeType, Stream content, CancellationToken ct);
        Task<bool>   ExistsAsync(string fileName, RecordedMediaKind kind, CancellationToken ct);
    }
    ```

10. **Platform implementations**:

    - **Android** — `Platforms/Android/HeyCyan/AndroidMediaStore.cs`. Uses
      `ContentResolver.Insert` against `MediaStore.Images.Media
      .ExternalContentUri`, `MediaStore.Video.Media.ExternalContentUri`,
      `MediaStore.Audio.Media.ExternalContentUri`. Set
      `MediaStore.MediaColumns.RelativePath = "DCIM/BodyCam"` for image/
      video, `"Music/BodyCam"` for audio (the `DCIM/CyanBridge` pattern
      from the official app, scoped to our brand). Returns the inserted
      `content://` URI as a string. **Standard direct save** — MP4 and JPG
      are passed through byte-for-byte; only audio is rewritten by Wave 1.
    - **iOS** — `Platforms/iOS/HeyCyan/IosMediaStore.cs`. Uses
      `PHPhotoLibrary.SharedPhotoLibrary.PerformChanges` with
      `PHAssetCreationRequest.CreationRequestForAssetFromImage` /
      `…ForVideo(atFileUrl)`. Audio is **not** a Photos asset type — write
      to `NSFileManager` Documents/`BodyCam/Audio/<name>` and surface a
      `file://` URI. Standard MP4 saves directly; the Photos app picks it
      up automatically.
    - **Windows / unsupported** — `Services/Glasses/HeyCyan/NoopMediaStore.cs`
      writes everything to `FileSystem.Current.AppDataDirectory
      /RecordedMedia/<kind>/<name>` and returns the resulting `file://`
      URI. Used in tests and on Windows-host MAUI builds.

11. **DI registration** in `MauiProgram.cs`:

    ```csharp
    #if ANDROID
        builder.Services.AddSingleton<IMediaStore, AndroidMediaStore>();
    #elif IOS
        builder.Services.AddSingleton<IMediaStore, IosMediaStore>();
    #else
        builder.Services.AddSingleton<IMediaStore, NoopMediaStore>();
    #endif
        builder.Services.AddSingleton<IHeyCyanRecordedMediaService,
            HeyCyanRecordedMediaService>();
    ```

## Verify

- [ ] `RecordedMediaClassifier` matches the iOS demo's filter for every
      extension in the table above (case-insensitive).
- [ ] `EnumerateAsync` parses a `media.config` body with mixed `\n` and
      `\r\n` line endings and trims trailing whitespace.
- [ ] `ImportAsync` for a `.jpg` round-trips bytes-for-bytes through
      `IMediaStore.SaveImageAsync` (no Wave 1 invocation).
- [ ] `ImportAsync` for a `.mp4` round-trips bytes-for-bytes through
      `SaveVideoAsync` and uses the 60 s read timeout.
- [ ] `ImportAsync` for a `.opus` invokes `OpusOggWrapper.AutoWrap` and
      writes to `SaveAudioAsync` with mime `audio/ogg` and a `.ogg` name.
- [ ] `ExistsAsync` returns `true` for a previously-imported file so a
      second `ImportAllAsync` is a no-op.
- [ ] `ImportAllAsync` reports `RecordedMediaImportProgress` with
      monotonically increasing `Completed` and `BytesSoFar`.
- [ ] The whole batch runs inside a single `EnterTransferModeAsync`
      session (one BLE switch, one P2P join).
- [ ] `DeleteRemoteAsync` returns `false` and logs when disabled; never
      runs in unit tests.
- [ ] No new HTTP client is constructed — Phase 2's `HeyCyanMediaTransfer`
      is the sole transfer surface.
