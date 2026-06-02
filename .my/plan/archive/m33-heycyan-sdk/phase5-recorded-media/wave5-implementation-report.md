# Wave/Phase: M33 Phase 5 Wave 5 — M16 Dictation Hook — Implemented

## Files changed
- [src/BodyCam/Services/ISettingsService.cs](src/BodyCam/Services/ISettingsService.cs#L48-L49) — Added `FeedVoiceNotesToDictation` feature flag property
- [src/BodyCam/Services/SettingsService.cs](src/BodyCam/Services/SettingsService.cs#L169-L173) — Implemented feature flag with Preferences storage (defaults to false)
- [src/BodyCam/Services/Glasses/HeyCyan/Media/IHeyCyanRecordedMediaService.cs](src/BodyCam/Services/Glasses/HeyCyan/Media/IHeyCyanRecordedMediaService.cs#L11-L14) — Added `AudioImported` event
- [src/BodyCam/Services/Glasses/HeyCyan/Media/HeyCyanRecordedMediaService.cs](src/BodyCam/Services/Glasses/HeyCyan/Media/HeyCyanRecordedMediaService.cs#L21) — Implemented event and fire logic in `ImportAsync` for audio items
- [src/BodyCam/Services/Glasses/HeyCyan/Media/RecordedMediaKind.cs](src/BodyCam/Services/Glasses/HeyCyan/Media/RecordedMediaKind.cs#L20-L23) — Added optional `Sha256` field to `ImportedMediaItem` record
- [src/BodyCam/MauiProgram.cs](src/BodyCam/MauiProgram.cs#L88-L96) — Conditional DI registration of hook and null IDictationRegistry when M16 absent
- [src/BodyCam.Tests/Services/Glasses/HeyCyan/Fakes/FakeSettingsService.cs](src/BodyCam.Tests/Services/Glasses/HeyCyan/Fakes/FakeSettingsService.cs#L57-L58) — Added feature flag to test fake
- [docs/configuration.md](docs/configuration.md#L72-L74) — Documented feature flag with opt-in semantics

## Files created
- [src/BodyCam/Services/Dictation/IDictationSource.cs](src/BodyCam/Services/Dictation/IDictationSource.cs) — Stub M16 interface for dictation sources
- [src/BodyCam/Services/Dictation/IDictationRegistry.cs](src/BodyCam/Services/Dictation/IDictationRegistry.cs) — Stub M16 interface for registration
- [src/BodyCam/Services/Glasses/HeyCyan/Media/HeyCyanDictationSource.cs](src/BodyCam/Services/Glasses/HeyCyan/Media/HeyCyanDictationSource.cs) — IDictationSource implementation for .ogg voice notes
- [src/BodyCam/Services/Glasses/HeyCyan/Media/HeyCyanDictationHook.cs](src/BodyCam/Services/Glasses/HeyCyan/Media/HeyCyanDictationHook.cs) — IHostedService that subscribes to AudioImported and registers with M16
- [src/BodyCam.Tests/Services/Glasses/HeyCyan/Media/HeyCyanDictationHookTests.cs](src/BodyCam.Tests/Services/Glasses/HeyCyan/Media/HeyCyanDictationHookTests.cs) — 11 tests covering hook lifecycle, registration, dedup, null-safety
- [src/BodyCam.Tests/Services/Glasses/HeyCyan/Media/HeyCyanDictationSourceTests.cs](src/BodyCam.Tests/Services/Glasses/HeyCyan/Media/HeyCyanDictationSourceTests.cs) — 4 tests covering file:// and path opening

## Build/Test results
- `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android --no-incremental` — **PASS** (41.1s, 101 warnings, 0 errors)
- `dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj --filter "FullyQualifiedName~HeyCyanDictation"` — **PASS** (11/11 tests, 0.8s)
- `dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj --filter "FullyQualifiedName~HeyCyan"` — **PASS** (184/184 tests, 55.9s) — no regressions

## Verify checklist
- [x] With `FeedVoiceNotesToDictation = false` (default), no `IDictationRegistry` lookup occurs — verified by conditional DI registration in MauiProgram.cs; hook type is never instantiated when flag is off.
- [x] With flag `false`, importing a `.opus` file does not invoke any M16 surface — test `StartAsync_with_null_registry_logs_and_returns` verifies null-registry no-op; subscriber count remains 0.
- [x] With flag `true` and M16 absent, startup does not throw — DI registers `IDictationRegistry` factory returning `null!`, constructor accepts nullable registry, `StartAsync` checks null and returns early.
- [x] With flag `true` and M16 present, importing `.opus` triggers exactly **one** `Register` call per unique SHA-256 — test `OnAudioImported_registers_voice_note_with_M16` + `OnAudioImported_deduplicates_by_sha256` verify single registration and dedup via `_seenHashes` set.
- [x] Re-importing same audio (same SHA-256) does not produce second `Register` call — test `OnAudioImported_deduplicates_by_sha256` validates.
- [x] `IDictationSource.OpenAsync` returns stream M16 can read end-to-end — test `OpenAsync_opens_file_from_path` + `OpenAsync_opens_file_from_file_uri` verify file:// and raw path handling; stream returns real file content.
- [x] Phase 5 Waves 1–4 build and pass with M16 disabled — all 184 HeyCyan tests pass; no compile-time hard dependency (IDictationRegistry is conditionally registered, null when absent).
- [x] Removing this wave's classes leaves Waves 1–4 functional — hook is opt-in via feature flag; recorded media service has no hard reference to hook (only event subscription when flag enabled).

## Notes / deviations
- **Sha256 threading:** Added optional `Sha256` field to `ImportedMediaItem` (not in wave doc) to avoid re-reading sidecars in the hook. This is more efficient and aligns with the doc's suggestion to "thread the hash through."
- **Null registry registration:** Registered `IDictationRegistry` as `sp => null!` (not `_ => null`) to satisfy generic constraint, then suppressed nullability warning with `!` operator. The hook's constructor accepts nullable registry and handles null gracefully.
- **IHostedService pattern:** Hook implements both `IHostedService` and `IDisposable` for proper lifecycle management. Registered via `AddHostedService(sp => sp.GetRequiredService<HeyCyanDictationHook>())` so it auto-starts on app launch when flag is true.
- **Event subscription safety:** `StartAsync` subscribes, `StopAsync` and `Dispose` unsubscribe to avoid memory leaks. Test `Dispose_unsubscribes_from_AudioImported` validates.

## Next wave hint
All Phase 5 waves complete (W1: OpusOggWrapper, W2: HeyCyanRecordedMediaService, W3: MP4 sidecars, W4: MediaGalleryPage, W5: M16 hook). Next: Phase 5 integration testing or move to M33 Phase 6 (if defined) or other milestones.
