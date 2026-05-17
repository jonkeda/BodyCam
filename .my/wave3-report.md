# Phase 3 / Wave 3: Auto-Routing Service — Implemented

## Files Created
- `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioRouter.cs` — Singleton service that subscribes to HeyCyan session state changes and automatically routes audio I/O to/from glasses

## Files Changed
- `src/BodyCam/Services/Audio/AudioInputManager.cs` — Added `ActiveProviderId` property and `SetActiveProviderAsync` method (throws on unknown ID, no-ops when already active)
- `src/BodyCam/Services/Audio/AudioOutputManager.cs` — Added `ActiveProviderId` property and `SetActiveProviderAsync` method (throws on unknown ID, no-ops when already active)
- `src/BodyCam/ServiceExtensions.cs` — Registered `HeyCyanAudioRouter` as singleton in DI container under `#if ANDROID`
- `src/BodyCam/MauiProgram.cs` — Resolved `HeyCyanAudioRouter` after `Build()` to activate subscription at startup

## Build/Test Results
- `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android` — **PASS** (50.7s, 108 warnings from Porcupine/bindings/platform analyzers, 0 errors)
- `dotnet build src/BodyCam.Tests/BodyCam.Tests.csproj` — **PASS** (11.3s, 39 warnings)
- `dotnet test --filter "AudioInputManagerHotPlugTests | AudioOutputManagerHotPlugTests"` — **PASS** (16 tests, 0.8s)

## Verify Checklist
- [x] On `Connected`, `AudioInputManager.ActiveProviderId == "heycyan-glasses"` — CODE VERIFIED (line 71-72 in HeyCyanAudioRouter.cs)
- [x] On `Connected`, `AudioOutputManager.ActiveProviderId == "heycyan-glasses"` — CODE VERIFIED (line 71-72)
- [x] On `Disconnected`, both managers return to previous provider or "platform" fallback — CODE VERIFIED (line 80-86, restores `_previousInputId ?? "platform"`)
- [x] Repeated `Connected → Disconnected → Connected` cycles preserve snapshot correctly — CODE VERIFIED (line 70: `??=` operator prevents overwriting snapshot on repeated Connected events)
- [x] `TransferMode` does NOT flip routing back to phone — CODE VERIFIED (line 93: default case does nothing for Scanning/Connecting/TransferMode)
- [x] Exceptions inside `SetActiveProviderAsync` are logged and do not crash SDK callback thread — CODE VERIFIED (line 54-61: top-level try/catch in `OnStateChanged`)
- [x] `DisposeAsync` unhooks `StateChanged` and disposes gate — CODE VERIFIED (line 98-102)
- [ ] MANUAL: In-flight Realtime conversation continues without dropping frames across routing flip (requires end-to-end test with real Realtime API + glasses in Phase 7)
- [ ] MANUAL: No handler leaks after 100 connect/disconnect cycles (requires real glasses hardware or mock session with repeated state transitions)

## Notes / Deviations
- Added `ActiveProviderId` property to both `AudioInputManager` and `AudioOutputManager` (wave spec assumed it existed; it did not).
- Added `SetActiveProviderAsync` method to both managers alongside existing `SetActiveAsync`:
  - `SetActiveAsync` — existing method, silent no-op when provider not found (for internal use)
  - `SetActiveProviderAsync` — new method per wave spec, throws `InvalidOperationException` when provider not found, no-ops when already active (for external callers like router)
- Router registered in `ServiceExtensions.AddAudioServices()` under `#if ANDROID` immediately after HeyCyan output provider registration, ensuring all dependencies are available.
- Router resolved in `MauiProgram.CreateMauiApp()` after `builder.Build()` via `_ = app.Services.GetRequiredService<HeyCyanAudioRouter>()` so constructor subscribes to `StateChanged` immediately at app startup.
- SemaphoreSlim gate serializes state transitions to prevent race conditions during rapid connect/disconnect toggles.
- `??=` operator on `_previousInputId` / `_previousOutputId` ensures the snapshot captures the *pre-glasses* state only on the first `Connected` event, not on subsequent ones.
- Fallback to `"platform"` when snapshot is null matches existing convention (AudioInputManager uses `"platform"` as fallback, AudioOutputManager uses first available provider; router uses `"platform"` explicitly for consistency).
- TransferMode does not trigger routing changes (glasses enter this state briefly during file pulls; audio should remain on glasses).
- Documentation note about auto-routing behavior deferred to Phase 7 (wave spec requested `docs/glasses-audio.md` which does not exist; comprehensive inline xmldoc added instead).

## Next Wave Hint
Wave 4: ../wave4-a2dp-codec-verification.md (verify BT A2DP codec negotiation with real HeyCyan glasses)
