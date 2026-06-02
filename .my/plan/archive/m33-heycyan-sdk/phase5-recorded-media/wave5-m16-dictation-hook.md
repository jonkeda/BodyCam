# M33 Phase 5 — Wave 5: Optional M16 Dictation Hook

Parent: [../phase5-recorded-media.md](../phase5-recorded-media.md)
Siblings: [wave1-opus-ogg-wrapper.md](wave1-opus-ogg-wrapper.md)
· [wave2-recorded-media-service.md](wave2-recorded-media-service.md)
· [wave3-mp4-sidecar-metadata.md](wave3-mp4-sidecar-metadata.md)
· [wave4-media-gallery-page.md](wave4-media-gallery-page.md)
· [wave6-tests.md](wave6-tests.md)

## Goal

Plug imported `.opus` voice notes into the M16 dictation pipeline so they
appear as a transcribable source alongside live mic input.

This is **strictly optional**. The hook is plug-in only — Phase 5 must be
fully functional even when M16 is absent or disabled. We do this with a
feature flag and a soft (reflection-free, but `null`-tolerant) M16
dependency: the assembly reference is allowed, but the registration code
path never runs unless the flag is true.

## Steps

1. **Feature flag** — extend `BodyCamOptions.HeyCyan` with
   `bool FeedVoiceNotesToDictation { get; init; } = false;`. Document in
   `docs/configuration.md` that turning this on is opt-in and irreversible
   only in the sense that already-registered sources cannot be cheaply
   un-registered (M16 is append-only by design).

2. **Hook contract** — in
   `src/BodyCam/Services/Glasses/HeyCyan/Media/HeyCyanDictationSource.cs`:

    ```csharp
    namespace BodyCam.Services.Glasses.HeyCyan.Media;

    public sealed class HeyCyanDictationSource : IDictationSource
    {
        public string SourceId => "heycyan-voicenote";
        public string MimeType => "audio/ogg";

        public Task<Stream> OpenAsync(string localUri, CancellationToken ct)
            => Task.FromResult<Stream>(File.OpenRead(LocalPath(localUri)));

        private static string LocalPath(string uri) =>
            uri.StartsWith("file://", StringComparison.Ordinal)
                ? new Uri(uri).LocalPath
                : uri;
    }
    ```

   `IDictationSource` is the M16 Phase 1 contract. If M16's interface
   evolves, regenerate this class — the type lives in the M33 assembly,
   not in M16, on purpose.

3. **Event surface in Wave 2** — add an event to
   `IHeyCyanRecordedMediaService`:

    ```csharp
    public event EventHandler<ImportedMediaItem>? AudioImported;
    ```

   Fire it from `ImportAsync` whenever `item.Kind == RecordedMediaKind.Audio`
   completes successfully. This is the *only* coupling the hook needs.

4. **Registration** — `HeyCyanDictationHook` glue class:

    ```csharp
    public sealed class HeyCyanDictationHook : IHostedService /* or IDisposable */
    {
        private readonly IHeyCyanRecordedMediaService _media;
        private readonly IDictationRegistry?         _registry;
        private readonly HeyCyanDictationSource      _source = new();
        private readonly HashSet<string>             _seenHashes = new(); // sha256

        public Task StartAsync(CancellationToken ct)
        {
            if (_registry is null) return Task.CompletedTask;
            _media.AudioImported += OnAudioImported;
            return Task.CompletedTask;
        }

        private void OnAudioImported(object? s, ImportedMediaItem e)
        {
            var hash = SidecarLookup.Sha256For(e.LocalUri);
            if (hash is null || !_seenHashes.Add(hash)) return;
            _registry!.Register(_source, e.LocalUri, hash);
        }
        ...
    }
    ```

   `SidecarLookup.Sha256For` reads the Wave 3 sidecar at
   `AppDataDirectory/RecordedMedia/Sidecars/<sha256>.bodycam.json` — but
   we already have the SHA from the import, so a faster path is to thread
   the hash through `ImportedMediaItem` (consider extending the record
   with `string Sha256` so we don't re-read the sidecar).

5. **Conditional DI** — in `MauiProgram.cs`:

    ```csharp
    if (builder.Configuration.GetValue<bool>(
            "BodyCam:HeyCyan:FeedVoiceNotesToDictation"))
    {
        builder.Services.AddSingleton<HeyCyanDictationHook>();
        builder.Services.AddHostedService(sp =>
            sp.GetRequiredService<HeyCyanDictationHook>());
    }
    ```

   When the flag is false, **the hook type is never instantiated**. The
   M16 `IDictationRegistry` resolution is also skipped, so an absent M16
   does not crash startup — `_registry` is registered as `null`-tolerant
   via `AddSingleton<IDictationRegistry>(_ => null!)` in the no-M16 build,
   or simply not registered (the optional ctor parameter handles both).

6. **Optional ctor** — make `HeyCyanDictationHook`'s ctor accept
   `IDictationRegistry? registry = null` so missing-M16 builds resolve
   cleanly via DI (`GetService<IDictationRegistry>()` returning null is
   acceptable).

7. **Dedup contract** — re-importing the same bytes (same SHA-256) must
   not re-register. The `_seenHashes` set survives the hook's lifetime;
   for cross-launch dedup, persist it as a small JSON file under
   `AppDataDirectory/RecordedMedia/dictation-registered.json` or rely on
   M16's own idempotency (preferred — keep state in M16, not here).

8. **Logging** — log at info level when a voice note is registered, with
   the SHA-256 prefix (first 12 chars) and the source's `localUri`. Log
   at debug when the flag is off (so users see why nothing is happening).

## Verify

- [ ] With `FeedVoiceNotesToDictation = false` (default), no
      `IDictationRegistry` lookup occurs; importing a `.opus` file does
      not invoke any M16 surface.
- [ ] With the flag `true` and M16 absent, startup does not throw.
- [ ] With the flag `true` and M16 present, importing a `.opus` file
      triggers exactly **one** call to `IDictationRegistry.Register` per
      unique SHA-256.
- [ ] Re-importing the same audio (same SHA-256) does not produce a
      second `Register` call.
- [ ] The `IDictationSource.OpenAsync` returns a stream that the M16
      transcription pipeline reads end-to-end without modification (the
      stream is a real Ogg/Opus file thanks to Wave 1).
- [ ] Phase 5 Waves 1–4 build and pass with M16 disabled — no compile-time
      hard dependency on M16 from the recorded-media service itself.
- [ ] Removing this wave's classes from the project leaves Waves 1–4
      fully functional (verified by deleting the namespace folder
      temporarily and re-building).
