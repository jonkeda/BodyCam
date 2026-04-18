# Remaining Roadmap

What's left to build. Completed milestones (M0–M3, M5 tools, M7, M8) are done and documented in `docs/`.

---

## Architecture Review ✅

**Status:** Complete  
**Details:** [review/steps/](../review/steps/)  
**Scope:** 15 hardening changes across threading, error handling, API boundaries, and code organization.

- [x] `SemaphoreSlim` in `AudioInputManager` + `AudioOutputManager` (prevent concurrent provider swaps)
- [x] `try/catch` in all async void event handlers (`AgentOrchestrator`, audio managers, `MainViewModel`)
- [x] Tool execution timeout (15s) in `ToolDispatcher.ExecuteAsync`
- [x] WebSocket reconnection — `ConnectionLost` event + exponential backoff in `AgentOrchestrator`
- [x] `MemoryStore` thread safety (`SemaphoreSlim` around Save/Search/GetRecent)
- [x] `ToolDispatcher` parses JSON at boundary (`string` → `JsonElement?`), error envelope via `ToolErrorResult`
- [x] `SetLayerAsync` resets `StatusText`/`ToggleButtonText` on failure
- [x] `SessionConfig` record — immutable settings snapshot captured at session start
- [x] Extract DI registrations into `ServiceExtensions` (6 grouped extension methods)
- [x] Camera `CancellationTokenRegistration` disposal in `PhoneCameraProvider`
- [x] `_isTransitioning` guard in `MainViewModel.SetLayerAsync` (prevents double-tap races)
- [x] `Lazy<IWakeWordService>` in `AgentOrchestrator` (defers Porcupine construction)
- [x] Porcupine dispose guard — local-variable ownership transfer pattern in `StartAsync`
- [x] Configurable mic release delay (`AppSettings.MicReleaseDelayMs`)
- [x] `ModelInfo` record — replaces raw `string[]` model arrays with typed Id+Label pairs

---

## M5 — Wake Word Detection

**Status:** Infrastructure complete, engine not implemented  
**Plan:** [m5-smart-features/](m5-smart-features/)

- [x] `IWakeWordService` interface + `WakeWordAction` enum + `WakeWordEntry` record
- [x] `NullWakeWordService` (stub)
- [x] `WakeWordBinding` + `WakeWordMode` on tools (7 tools declare bindings)
- [x] `ToolDispatcher.BuildWakeWordEntries()` (builds system + tool keyword list)
- [x] `AgentOrchestrator.OnWakeWordDetected()` (3-case handler)
- [x] `IMicrophoneCoordinator` + implementation (mic handoff)
- [x] Unit tests (NullService, Bindings, Orchestrator)
- **Phase 1: Porcupine Engine Integration**
  - [ ] Add Porcupine NuGet package
  - [ ] Implement `PorcupineWakeWordService`
  - [ ] Generate `.ppn` keyword files via Picovoice Console
  - [ ] Add Picovoice AccessKey to settings
  - [ ] Audio frame adapter (PCM → Porcupine format)
  - [ ] DI swap from `NullWakeWordService` to `PorcupineWakeWordService`
- **Phase 2: Quick Action & Session Flow**
  - [ ] Quick action lifecycle (connect → execute → speak → disconnect)
  - [ ] Session timeout (auto-disconnect after silence)
  - [ ] Layer transition UI feedback
  - [ ] Audio feedback tones (activation/deactivation)
- **Phase 3: Android & Cross-Platform**
  - [ ] `Porcupine.Android` native package
  - [ ] Android-specific `.ppn` files
  - [ ] Background service for always-on detection
  - [ ] Platform-specific audio routing
- **Phase 4: iOS Platform Support**
  - [ ] `Porcupine-iOS` binding
  - [ ] iOS-specific `.ppn` keyword files
  - [ ] `AVAudioSession` background audio configuration
  - [ ] Audio interruption handling (phone calls, Siri)

## M9 — Multi-Provider Architecture

**Status:** Design complete, not implemented  
**Plan:** [m9-providers/](m9-providers/)

- **Phase 1: Provider Abstraction**
  - [ ] Extract `RealtimeClient` into `OpenAiRealtimeClient`
  - [ ] `IRealtimeClientFactory` interface
  - [ ] `RealtimeClientFactory` for runtime provider switching
- **Phase 2: Gemini Live**
  - [ ] `GeminiLiveRealtimeClient` (Google Gemini Live API)
  - [ ] Gemini session configuration mapping
  - [ ] Provider-specific settings UI

## M11 — Camera Architecture

**Status:** Phase 1 implemented  
**Plan:** [m11-camera/](m11-camera/)

- **Phase 1: Abstraction + Phone Camera** ✅
  - [x] `ICameraProvider` interface
  - [x] `CameraManager`
  - [x] `PhoneCameraProvider` (wraps existing CameraView)
  - [x] Unit tests
- **Phase 2: USB Bodycam**
  - [ ] Windows MediaCapture USB provider
  - [ ] Android UVC provider
  - [ ] Device enumeration
- **Phase 3: WiFi/IP Cameras**
  - [ ] RTSP client
  - [ ] HTTP MJPEG parser
  - [ ] IP camera provider
- **Phase 4: Chinese WiFi Glasses**
  - [ ] WiFi-Direct discovery
  - [ ] Per-model camera profiles
- **Phase 5: Meta Ray-Ban**
  - [ ] Meta SDK integration (blocked on SDK access)
- **Phase 6: iOS Platform Support**
  - [ ] iOS `PlatformCameraProvider` (AVFoundation)
  - [ ] Camera permission handling
  - [ ] Headless capture without visible preview

## M12 — Input Audio Architecture

**Status:** Phase 2 implemented  
**Plan:** [m12-input-audio/](m12-input-audio/)

- **Phase 1: Abstraction + Platform Mic** ✅
  - [x] `IAudioInputProvider` interface
  - [x] `AudioInputManager` (implements `IAudioInputService`)
  - [x] Windows `PlatformMicProvider`
  - [x] Android `PlatformMicProvider`
  - [x] Unit tests
- **Phase 2: BT Audio Input** ✅
  - [x] `WindowsBluetoothAudioProvider` (WasapiCapture on BT HFP endpoint)
  - [x] `AndroidBluetoothAudioProvider` (SCO routing + AudioRecord)
  - [x] `WindowsBluetoothEnumerator` (MMDevice API + IMMNotificationClient)
  - [x] `AndroidBluetoothEnumerator` (BondedDevices + BroadcastReceiver)
  - [x] `ScoStateReceiver` (SCO connection state broadcast)
  - [x] `AudioResampler` (linear interpolation PCM16 mono)
  - [x] `BluetoothDeviceInfo` record
  - [x] `AudioInputManager` hot-plug (`RegisterProvider`/`UnregisterProviderAsync`/`ProvidersChanged`)
  - [x] Settings UI auto-refresh on device connect/disconnect
  - [x] Unit tests (24 tests — resampler + hot-plug)
- **Phase 3: USB Audio**
  - [ ] USB audio device enumeration
  - [ ] USB audio input provider
- **Phase 4: WiFi Glasses Audio**
  - [ ] WiFi glasses audio stream provider
- **Phase 5: iOS Platform Support**
  - [ ] iOS `PlatformMicProvider` (AVAudioEngine)
  - [ ] `AVAudioSession` configuration
  - [ ] Microphone permission handling

## M13 — Output Audio Architecture

**Status:** Phase 2 implemented  
**Plan:** [m13-output-audio/](m13-output-audio/)

- **Phase 1: Abstraction + Platform Speaker** ✅
  - [x] `IAudioOutputProvider` interface
  - [x] `AudioOutputManager` (implements `IAudioOutputService`)
  - [x] Windows `PlatformSpeakerProvider`
  - [x] Android `PlatformSpeakerProvider`
  - [x] Unit tests
- **Phase 2: BT Audio Output** ✅
  - [x] `WindowsBluetoothAudioOutputProvider` (WasapiOut + BufferedWaveProvider)
  - [x] `AndroidBluetoothAudioOutputProvider` (AudioTrack + SetPreferredDevice)
  - [x] `WindowsBluetoothOutputEnumerator` (render endpoint scanning)
  - [x] `AndroidBluetoothOutputEnumerator` (BluetoothA2dp/Sco device scanning)
  - [x] `AudioOutputManager` hot-plug + `ProvidersChanged` event
  - [x] Settings UI auto-refresh on device connect/disconnect
- **Phase 3: USB Audio Output**
  - [ ] USB audio output provider
- **Phase 4: Volume Management**
  - [ ] Audio ducking
  - [ ] Per-provider volume control
- **Phase 5: iOS Platform Support**
  - [ ] iOS `PhoneSpeakerProvider` (AVAudioEngine)
  - [ ] `AVAudioSession` output routing
  - [ ] Audio route change handling

## M14 — Button & Gesture Input

**Status:** Phase 1 implemented  
**Plan:** [m14-buttons/](m14-buttons/)

- **Phase 1: Abstraction + Gesture Recognition** ✅
  - [x] `IButtonInputProvider` interface
  - [x] `ButtonInputManager`
  - [x] `GestureRecognizer` (tap/double-tap/long-press)
  - [x] `ActionMap` (gesture → action mapping)
  - [x] `KeyboardShortcutProvider` (Windows dev)
  - [x] Unit tests
- **Phase 2: BT Glasses Buttons & BLE Remotes**
  - [ ] `GattButtonProvider` (custom GATT characteristic)
  - [ ] `AvrcpButtonProvider` (media key interception)
  - [ ] Glasses-specific gesture mapping
  - [ ] `BtHomeButtonProvider` (BTHome v2 passive BLE scanning)
  - [ ] `BtHomeParser` + `BtHomeDeviceProfile` (protocol + device profiles)
  - [ ] `PreRecognizedGesture` event on `IButtonInputProvider`
  - [ ] Shelly BLU Remote default action mapping
- **Phase 3: Phone Buttons**
  - [ ] Volume key interception
  - [ ] Shake gesture provider
- **Phase 4: Keyboard Shortcuts (Windows)**
  - [ ] Global hotkey registration
  - [ ] Configurable key bindings
- **Phase 5: iOS Platform Support**
  - [ ] iOS `VolumeButtonProvider` (AVAudioSession volume observation)
  - [ ] Verify `ShakeGestureProvider` on iOS

## M15 — Brinell Test Extensions (Audio & Camera)

**Status:** Phase 3 implemented  
**Plan:** [m15-brinell/](m15-brinell/)

- **Phase 1: Test Providers** ✅
  - [x] `TestMicProvider` (injectable audio, chunk emission, disconnect simulation)
  - [x] `TestSpeakerProvider` (capture output, clear/reset)
  - [x] `TestCameraProvider` (injectable frames, cycling, disconnect)
  - [x] `TestButtonProvider` (simulated clicks, gestures, pre-recognized events)
  - [x] `TestAssets` (MinimalJpeg, SilencePcm)
  - [x] Unit tests (TestProviderTests — mic, speaker, camera, button)
- **Phase 2: Test DI Infrastructure** ✅
  - [x] `BodyCamTestHost` (builds full service graph without MAUI host)
  - [x] Test provider auto-registration via DI
  - [x] `configure` callback for test-specific registrations
  - [x] `InitializeAsync` wires managers + button input
  - [x] Unit tests (BodyCamTestHostTests)
- **Phase 3: E2E Test Scenarios** ✅
  - [x] Camera pipeline (capture → stream → disconnect → reset)
  - [x] Audio flow (mic → manager → speaker round-trip)
  - [x] Button dispatch (tap/double-tap/long-press → action mapping)
  - [x] Provider fallback (disconnect → fallback scenarios)
  - [x] Memory tool pipeline (save/recall via ToolDispatcher)
  - [x] Cross-cutting integration tests
- **Phase 4: Brinell.Mocking Extensions**
  - [ ] Generic reusable mocking types (optional)
- **Phase 5: iOS Test Support**
  - [ ] Verify test providers on iOS simulator
  - [ ] iOS simulator test runner configuration
  - [ ] CI pipeline with macOS runner

## M16 — Voice Dictation (Wispr Flow-style)

**Status:** Not started  
**Plan:** [m16-dictation/](m16-dictation/)

- **Phase 1: Core Dictation Pipeline**
  - [ ] `ITextInjectionProvider` + `ITextInjectionService` + `TextInjectionManager`
  - [ ] Windows `ClipboardTextInjectionProvider` (clipboard + Ctrl+V)
  - [ ] `IDictationService` + `DictationService` (state machine)
  - [ ] `DictationAgent` (routes STT → text injection)
  - [ ] `StartDictationTool` + `StopDictationTool`
  - [ ] DI registration + orchestrator integration
- **Phase 2: AI Cleanup & Formatting**
  - [ ] `TranscriptBuffer` (sentence boundary detection)
  - [ ] `IDictationCleanupService` (GPT-4o-mini post-processing)
  - [ ] Clean mode (filler removal) + Rewrite mode (polished prose)
  - [ ] `IPersonalDictionaryService` (custom names/jargon)
  - [ ] Mode switching mid-dictation
- **Phase 3: Android & Command Mode**
  - [ ] Android `ClipboardTextInjectionProvider`
  - [ ] Android `AccessibilityTextInjectionProvider`
  - [ ] `IDictationCommandService` (voice editing: "make concise", "undo")
  - [ ] Multi-language detection
- **Phase 4: iOS Platform Support**
  - [ ] iOS `ClipboardTextInjectionProvider` (UIPasteboard)
  - [ ] Optional keyboard extension (`UIInputViewController`)
  - [ ] iOS dictation status UI (Live Activity / notification)

## M17 — Glasses Integration

**Status:** Not started — waiting for hardware  
**Plan:** [m17-glasses/](m17-glasses/)

Connects smart glasses as unified peripheral devices using M11–M14 abstractions.
Replaces M4 (which predated the multi-provider architecture).

- **Phase 1: Hardware Investigation & BT Audio**
  - [ ] TKYUAN glasses investigation (GATT services, camera protocol, button events)
  - [ ] Investigation report
  - [ ] `BluetoothAudioInputProvider` (Windows)
  - [ ] `BluetoothAudioOutputProvider` (Windows)
  - [ ] Basic `GlassesDeviceManager`
  - [ ] Android BT audio providers
- **Phase 2: Buttons & Camera**
  - [ ] `GattButtonProvider` or `AvrcpButtonProvider` (based on investigation)
  - [ ] `WifiGlassesCameraProvider` (WiFi-Direct RTSP/MJPEG)
  - [ ] `WifiDirectService` (connection + stream URL discovery)
  - [ ] Android camera + button providers
- **Phase 3: Connection UI & Auto-Fallback**
  - [ ] `GlassesViewModel` + `GlassesPage.xaml`
  - [ ] Auto-fallback on disconnect (switch to phone providers)
  - [ ] Auto-reconnect (3x exponential backoff)
  - [ ] Battery monitoring (GATT 0x180F)
  - [ ] MainPage glasses status indicator
- **Phase 4: iOS Platform Support**
  - [ ] iOS `CoreBluetooth` BLE scanning + GATT connection
  - [ ] iOS WiFi camera stream connection
  - [ ] `GlassesDeviceManager` iOS integration
  - [ ] `NSBluetoothAlwaysUsageDescription` permission

## M18 — QR Code Scanning

**Status:** Not started  
**Plan:** [m18-qr-code/](m18-qr-code/)

Scan QR codes from camera feed on demand, read content aloud via AI, ask user what to do.

- **Phase 1: Core QR Scanning**
  - [ ] `IQrCodeScanner` interface + `ZXingQrScanner` implementation (ZXing.Net)
  - [ ] `QrScanResult` model (content, format, raw bytes)
  - [ ] `ScanQrCodeTool` (ITool — captures frame, decodes, returns content)
  - [ ] Wake word binding: "scan that" → QuickAction
  - [ ] Unit tests (decode from test JPEG images)
  - [ ] DI registration
- **Phase 2: Barcode Support + History**
  - [ ] Extend scanner for EAN-13, UPC-A, Code 128, Data Matrix
  - [ ] `QrCodeService` with scan history (last N results)
  - [ ] `RecallLastScanTool` — "what was that QR code again?"
  - [ ] Save-to-memory integration (auto-save scanned URLs/contacts)
- **Phase 3: Content-Aware Actions**
  - [ ] URL detection → offer to open in browser
  - [ ] WiFi QR → offer to connect to network
  - [ ] vCard → offer to save contact
  - [ ] Plain text → offer to save to memory
  - [ ] Action dispatch through AI conversation (user chooses by voice)
- **Phase 4: iOS Platform Support**
  - [ ] Verify ZXing.Net works on iOS (.NET AOT)
  - [ ] Test with iOS camera provider
  - [ ] Platform-specific permission handling if needed

## M19 — Logging, Crash Reporting & Analytics

**Status:** Not started  
**Plan:** [m19-logging/](m19-logging/)

Replace ad-hoc `DebugLog` string events with structured `ILogger<T>`, persist to remote sinks, add crash reporting and usage analytics.

- **Phase 1: Core ILogger Integration**
  - [ ] Inject `ILogger<T>` into AgentOrchestrator, RealtimeClient, managers
  - [ ] Replace all `DebugLog?.Invoke()` calls with leveled `ILogger` calls
  - [ ] `InAppLoggerProvider` (custom provider for debug overlay, ring buffer)
  - [ ] Wire into MAUI logging pipeline (`MauiProgram.cs`)
  - [ ] Update MainViewModel to consume `InAppLoggerProvider`
  - [ ] Remove `AgentOrchestrator.DebugLog` event
- **Phase 2: Remote Sink (OpenTelemetry + Azure Monitor)**
  - [ ] Add `OpenTelemetry` + `Azure.Monitor.OpenTelemetry.Exporter` packages
  - [ ] Configure OpenTelemetry logging exporter in MAUI pipeline
  - [ ] Configure connection string via settings (opt-in)
  - [ ] Filter: Warning+ to remote, privacy-safe properties only
  - [ ] Resource attributes: SessionId, Platform, AppVersion
- **Phase 3: Crash Reporting (Sentry)**
  - [ ] Add `Sentry.Maui` package
  - [ ] Configure `UseSentry()` in `MauiProgram.cs`
  - [ ] `BeforeSend` callback to strip API keys and transcript text
  - [ ] Breadcrumbs from ILogger entries
  - [ ] Offline envelope caching
  - [ ] Exclude PII, API keys, transcript text
- **Phase 4: Usage Analytics (OpenTelemetry)**
  - [ ] Custom events via `ActivitySource`: SessionStarted, ToolExecuted, VisionCaptured
  - [ ] Metrics via `Meter`: session duration, tool call count, error rate
  - [ ] Opt-in toggle in settings
- **Phase 5: iOS Platform Support**
  - [ ] Verify OpenTelemetry exporter on iOS (.NET AOT)
  - [ ] Verify Sentry.Maui on iOS
  - [ ] iOS crash symbolication (Sentry dSYM upload)
  - [ ] Background logging with iOS lifecycle constraints

## M20 — Barcode Product Lookup

**Status:** Not started  
**Plan:** [m20-barcode/](m20-barcode/)

Scan product barcodes (EAN-13, UPC-A, etc.) from camera feed and look up product
information via open APIs. Reads barcode aloud with product name, brand, nutritional
info, and pricing. Depends on M18 Phase 2 (barcode scanning support).

- **Phase 1: Barcode Lookup Service**
  - [ ] `IBarcodeLookupService` interface
  - [ ] `OpenFoodFactsClient` (Open Food Facts API — food/drink products)
  - [ ] `UpcItemDbClient` (UPCitemdb API — general products)
  - [ ] `BarcodeLookupService` (aggregates multiple sources, caches results)
  - [ ] `ProductInfo` model (name, brand, category, image URL, nutrition, ingredients)
  - [ ] `LookupBarcodeTool` (ITool — scans barcode → looks up → returns product info)
  - [ ] Wake word binding: "scan barcode" → QuickAction
  - [ ] Unit tests (mock HTTP responses)
  - [ ] DI registration
- **Phase 2: Smart Responses**
  - [ ] AI summarization of product info (concise spoken description)
  - [ ] Nutritional highlights (calories, allergens, dietary flags)
  - [ ] Price comparison hints (when available from API)
  - [ ] "Tell me more about this product" follow-up via conversation
- **Phase 3: History & Favorites**
  - [ ] `BarcodeHistoryService` (persist scanned products)
  - [ ] `RecallLastProductTool` — "what was that product?"
  - [ ] Favorites / shopping list integration via memory store
  - [ ] Scan history UI in settings
- **Phase 4: iOS Platform Support**
  - [ ] Verify HTTP clients work on iOS (.NET AOT)
  - [ ] Test barcode scanning + lookup end-to-end on iOS

## M21 — Accessibility Improvements

**Status:** Phases 1–5 implemented, Phase 6 deferred  
**Plan:** [m21-accessibility/](m21-accessibility/)

Screen reader support, keyboard navigation, dynamic text scaling, and color contrast
fixes. Makes the app usable with Narrator (Windows), TalkBack (Android), and
VoiceOver (iOS).

- **Phase 1: Semantic Labels & Screen Reader Support**
  - [x] `SemanticProperties.Description` on all MainPage interactive controls
  - [x] `SemanticProperties.Description` on all SettingsPage controls
  - [x] `SemanticProperties.HeadingLevel` on section headers
  - [x] `AccessibleText` property on `TranscriptEntry`
  - [x] `StateDescription` property on `MainViewModel`
  - [ ] Narrator (Windows) + TalkBack (Android) testing
- **Phase 2: Keyboard & Focus Navigation**
  - [x] `TabIndex` on all interactive controls (logical order)
  - [x] Focus ring visual states (`VisualStateManager`)
  - [x] Modal focus trap in snapshot overlay
  - [ ] Keyboard activation verification (Enter/Space)
- **Phase 3: Dynamic Type & Text Scaling**
  - [x] Replace fixed `FontSize` with named sizes (`Body`, `Caption`, `Title`)
  - [ ] Verify `FontAutoScalingEnabled` not disabled
  - [ ] Test at 200% text scaling (Windows) and largest font (Android)
  - [x] Remove hardcoded `HeightRequest` on buttons
- **Phase 4: Color Contrast & High Contrast**
  - [x] Fix `TextColor="Gray"` usages (fails WCAG AA for small text)
  - [x] Audit status / role colors for contrast ratios
  - [ ] High contrast resource dictionary
  - [ ] Test with Windows High Contrast mode + Android high contrast text
- **Phase 5: Reduced Motion & Audio Cues**
  - [x] Check `PreferReducedMotion` before transcript item animations
  - [ ] `IAudioCueService` — short earcons for state changes, tool activity, errors
  - [x] Audio cue files: activate, deactivate, listen, tool_start, tool_done, error, connected
  - [ ] Settings toggle for audio cues
- **Phase 6: iOS Platform Support**
  - [ ] VoiceOver navigation testing
  - [ ] Dynamic Type scaling verification
  - [ ] Switch Control navigation

## M30 — Polish & Optimization

**Status:** Not started  
**Plan:** [m30-polish/](m30-polish/)

Production-ready quality pass. Intentionally last — no point polishing features
that will change.

- **Phase 1: Latency & Performance**
  - [ ] Latency instrumentation (T0-T4 timing points)
  - [ ] Pre-connect WebSocket on app start
  - [ ] Stream TTS playback immediately
  - [ ] Minimize audio chunk sizes
  - [ ] Performance dashboard (debug mode)
- **Phase 2: Battery & Network**
  - [ ] BLE control channel optimization
  - [ ] Reduce BT scan frequency when connected
  - [ ] WebSocket keepalive pings
  - [ ] Vision capture frequency based on battery level
- **Phase 3: Error Handling & Resilience**
  - [x] WebSocket auto-reconnect (exponential backoff) — done in Architecture Review
  - [ ] API rate limit (429) backoff + model fallback
  - [ ] API error (500) retry 3x + user notification
  - [ ] OOM handling (trim history, reduce resolution)
  - [ ] Offline detection + queued commands
- **Phase 4: Settings Page**
  - [ ] Full settings UI (API keys, models, voice, wake word, dictation, privacy)
- **Phase 5: Privacy Indicators**
  - [ ] Red dot + "REC" when mic active
  - [ ] Camera icon when capturing
  - [ ] Audio tones on recording start/stop
  - [ ] Glasses LED control (if supported)
- **Phase 6: Cost Tracking**
  - [ ] `UsageTracker` service
  - [ ] Cost indicator on main page
  - [ ] Daily/weekly/monthly usage history
  - [ ] Optional daily budget alert
- **Phase 7: iOS Platform Polish**
  - [ ] iOS battery optimization (`BGTaskScheduler`)
  - [ ] iOS App Store privacy labels
  - [ ] iOS performance profiling (Instruments)
  - [ ] `AVAudioSession` interruption handling
  - [ ] TestFlight distribution setup
