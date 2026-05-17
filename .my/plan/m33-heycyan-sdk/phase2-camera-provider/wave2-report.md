# Wave 2: HeyCyanMediaTransfer — Implemented

## Files changed
- [IHeyCyanGlassesSession.cs](../../../src/BodyCam/Services/Glasses/HeyCyan/IHeyCyanGlassesSession.cs) — Added ExitTransferModeAsync method
- [AndroidHeyCyanGlassesSession.cs](../../../src/BodyCam/Services/Glasses/HeyCyan/AndroidHeyCyanGlassesSession.cs) — Forwarded ExitTransferModeAsync to core
- [HeyCyanGlassesSessionCore.cs](../../../src/BodyCam/Services/Glasses/HeyCyan/HeyCyanGlassesSessionCore.cs) — Implemented EnterTransferModeAsync and ExitTransferModeAsync with BLE command sending and IP notify parsing
- [ServiceExtensions.cs](../../../src/BodyCam/ServiceExtensions.cs) — Added DI registration for IHeyCyanMediaTransfer and IHeyCyanHttpClientFactory
- [BodyCam.Tests.csproj](../../../src/BodyCam.Tests/BodyCam.Tests.csproj) — Added Microsoft.Extensions.TimeProvider.Testing package reference

## Files created
- [IHeyCyanHttpClient.cs](../../../src/BodyCam/Services/Glasses/HeyCyan/IHeyCyanHttpClient.cs) — Platform-abstracted HTTP client interface for glasses media downloads
- [IHeyCyanHttpClientFactory.cs](../../../src/BodyCam/Services/Glasses/HeyCyan/IHeyCyanHttpClientFactory.cs) — Factory for creating platform-specific HTTP clients
- [IHeyCyanMediaTransfer.cs](../../../src/BodyCam/Services/Glasses/HeyCyan/IHeyCyanMediaTransfer.cs) — Cross-platform orchestrator interface with HeyCyanMediaEntry and HeyCyanMediaKind
- [MediaConfigParser.cs](../../../src/BodyCam/Services/Glasses/HeyCyan/MediaConfigParser.cs) — Parses /files/media.config plaintext response from glasses
- [AndroidHeyCyanHttpClientFactory.cs](../../../src/BodyCam/Platforms/Android/HeyCyan/AndroidHeyCyanHttpClientFactory.cs) — Android implementation wrapping WiFiP2pHttpClient
- [HeyCyanMediaTransfer.cs](../../../src/BodyCam/Services/Glasses/HeyCyan/HeyCyanMediaTransfer.cs) — Warm transfer mode orchestrator with 8s idle timeout
- [MediaConfigParserTests.cs](../../../src/BodyCam.Tests/Services/Glasses/HeyCyan/MediaConfigParserTests.cs) — Unit tests for media.config parsing
- [HeyCyanMediaTransferTests.cs](../../../src/BodyCam.Tests/Services/Glasses/HeyCyan/HeyCyanMediaTransferTests.cs) — Unit tests for transfer orchestrator

## Build/Test results
- `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android -c Debug` — **PASS** (91 warnings, expected)
- `dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj --filter "FullyQualifiedName~HeyCyan" -c Debug` — **PASS** (57 tests, 0 failures)

## Verify checklist
- [x] `ListAsync` returns parsed `HeyCyanMediaEntry` rows with correct `Kind` for `.jpg` / `.mp4` / `.opus` — Verified by MediaConfigParserTests
- [x] Two consecutive `DownloadAsync` calls within `_warmIdle` invoke `IHeyCyanGlassesSession.EnterTransferModeAsync` exactly once — Verified by `Two_consecutive_downloads_within_warm_window_reuse_session` test
- [x] Advancing the fake clock past `_warmIdle` triggers `TeardownAsync`, which calls `ExitTransferModeAsync` (BLE `LargeDataHandler.GlassesControl(new byte[] { 0x02, 0x01, 0x09 }, cb)`) exactly once — Verified by `Idle_timeout_triggers_automatic_teardown` test
- [x] A third `DownloadAsync` after the warm window re-enters transfer mode (counter == 2) — Verified by `Download_after_idle_timeout_reenters_transfer_mode` test
- [x] `ExitAsync` is idempotent (calling twice does not double-send the BLE exit) — Verified by `ExitAsync_is_idempotent` test
- [x] `DisposeAsync` cleans up `_idleCts` and `_http` — Verified by `DisposeAsync_tears_down_warm_session` test
- [x] `MediaConfigParser` handles trailing newlines, blank lines, and mixed `\r\n` / `\n` line endings — Verified by `Parse_handles_mixed_line_endings` and `Parse_ignores_blank_lines` tests
- [x] Cancellation mid-`DownloadAsync` does NOT trigger an idle teardown (warm session stays available for the next call) — Verified by `Cancellation_during_download_does_not_schedule_idle_exit` test

## Notes / deviations
- HeyCyanGlassesSessionCore.EnterTransferModeAsync was NotImplemented from Phase 1; now implemented in this wave
- Added ExitTransferModeAsync method to IHeyCyanGlassesSession (not in original interface)
- Used HeyCyanRawNotify.LoadData (not .Frame) for parsing BLE notify frames per existing codebase conventions
- AndroidHeyCyanHttpClientFactory uses ILoggerFactory (not ILogger) to create logger for WiFiP2pHttpClient
- All 57 HeyCyan tests pass, including 13 new tests from MediaConfigParserTests and HeyCyanMediaTransferTests
- Android build succeeds with expected warnings (Porcupine content files, HeyCyan binding metadata, XML doc formatting)

## Next wave hint
**Phase 2 Wave 3:** [wave3-heycyan-camera-provider.md](wave3-heycyan-camera-provider.md) — Implement `HeyCyanCameraProvider` as an `ICameraProvider` that uses the media transfer orchestrator to capture still frames via BLE photo command + HTTP download.
