# Wave 5 Implementation Todos

- [ ] Add `FeedVoiceNotesToDictation` feature flag to `ISettingsService` and `SettingsService`
- [ ] Create stub M16 interfaces (`IDictationSource`, `IDictationRegistry`) in `Services/Dictation/`
- [ ] Add `Sha256` field to `ImportedMediaItem` record
- [ ] Add `AudioImported` event to `IHeyCyanRecordedMediaService`
- [ ] Fire `AudioImported` event in `HeyCyanRecordedMediaService.ImportAsync` for audio items
- [ ] Create `HeyCyanDictationSource` implementing `IDictationSource`
- [ ] Create `HeyCyanDictationHook` that subscribes to `AudioImported` and registers with `IDictationRegistry`
- [ ] Conditionally register hook in `ServiceExtensions.AddGlassesServices`
- [ ] Document feature flag in `docs/configuration.md`
- [ ] Add tests in `BodyCam.Tests/Services/Glasses/HeyCyan/Media/HeyCyanDictationHookTests.cs`
- [ ] Build and verify all tests pass
