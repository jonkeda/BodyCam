# BodyCam — Roadmap

All milestones sorted by number. Each task corresponds to one phase from the milestone's `overview.md`.

---

### M0 — Project Scaffold

- [x] MAUI solution, `AppSettings`, DI, main page shell, Windows build

### M1 — Audio Pipeline

- [x] Audio input/output services, Realtime API WebSocket, voice agents, orchestrator wiring

### M2 — Conversation Agent

- [x] `ConversationAgent`, `SessionContext`, system prompt, orchestrator flow, transcript UI, interruption handling

### M3 — Vision Pipeline

- [x] Camera service, `VisionAgent`, trigger modes, context injection, Android camera, preview UI

### M5 — Wake Word Detection

**Plan:** [m5-smart-features/](m5-smart-features/)

- [ ] Phase 1 — Porcupine Engine Integration
- [ ] Phase 2 — Quick Action & Session Flow
- [ ] Phase 3 — Android & Cross-Platform
- [ ] Phase 4 — iOS Platform Support

### M7 — Authentication & Key Management

- [x] `IApiKeyService`, SecureStorage, fallback chain, WebSocket header auth, key masking

### M8 — Model Selection

- [x] Model picker, runtime switching, provider-aware lists

### M9 — Multi-Provider Architecture

**Plan:** [m9-providers/](m9-providers/)

- [x] OpenAI + Azure OpenAI provider support
- [ ] Gemini Live API provider
- [ ] `RealtimeClientFactory` for runtime provider switching

### M11 — Camera Architecture

**Plan:** [m11-camera/](m11-camera/)

- [x] Phase 1 — Camera Abstraction & Phone Camera
- [ ] Phase 2 — USB Bodycam
- [ ] Phase 3 — WiFi / IP Cameras
- [ ] Phase 4 — Chinese WiFi Glasses
- [ ] Phase 5 — Meta Ray-Ban Integration
- [ ] Phase 6 — iOS Platform Support

### M12 — Input Audio Architecture

**Plan:** [m12-input-audio/](m12-input-audio/)

- [x] Phase 1 — Audio Input Abstraction & Platform Mic
- [x] Phase 2 — Bluetooth Audio
- [ ] Phase 3 — USB Audio Devices
- [ ] Phase 4 — WiFi Audio Stream
- [ ] Phase 5 — iOS Platform Support

### M13 — Output Audio Architecture

**Plan:** [m13-output-audio/](m13-output-audio/)

- [x] Phase 1 — Audio Output Abstraction & Platform Providers
- [x] Phase 2 — Bluetooth Audio Output
- [ ] Phase 3 — USB Audio Output
- [ ] Phase 4 — Advanced Audio Management
- [ ] Phase 5 — iOS Platform Support

### M14 — Button & Gesture Input Architecture

**Plan:** [m14-buttons/](m14-buttons/)

- [x] Phase 1 — Abstraction & Gesture Recognition
- [ ] Phase 2 — BT Glasses Buttons & BLE Remotes
- [ ] Phase 3 — Phone Inputs
- [ ] Phase 4 — Settings & Customization
- [ ] Phase 5 — iOS Platform Support

### M15 — Brinell Test Extensions

**Plan:** [m15-brinell/](m15-brinell/)

- [x] Phase 1 — Test Provider Implementations
- [x] Phase 2 — Test DI Infrastructure
- [ ] Phase 3 — End-to-End Test Scenarios
- [ ] Phase 4 — Brinell Core Extensions (Optional)
- [ ] Phase 5 — iOS Test Support

### M16 — Voice Dictation

**Plan:** [m16-dictation/](m16-dictation/)

- [ ] Phase 1 — Core Dictation Pipeline
- [ ] Phase 2 — AI Cleanup & Formatting
- [ ] Phase 3 — Android & Command Mode

### M17 — Glasses Integration

**Plan:** [m17-glasses/](m17-glasses/)

- [ ] Phase 1 — Hardware Investigation & BT Audio
- [ ] Phase 2 — Buttons & Camera
- [ ] Phase 3 — Connection UI & Auto-Fallback

### M18 — QR Code & Barcode Scanning

**Plan:** [m18-qr-code/](m18-qr-code/)

- [x] Phase 1 — Core QR Scanning
- [x] Phase 2 — Scan UI
- [x] Phase 3 — Barcode Support + History
- [x] Phase 4 — Content-Aware Actions
- [ ] Phase 5 — Post-Scan UI & Voice Actions
- [x] Phase 6 — Vision Pipeline

### M19 — Logging, Crash Reporting & Analytics

**Plan:** [m19-logging/](m19-logging/)

- [x] Phase 1 — Core ILogger Integration
- [x] Phase 2 — Remote Sink (OpenTelemetry + Azure Monitor)
- [x] Phase 3 — Crash Reporting (Sentry)
- [x] Phase 4 — Usage Analytics
- [ ] Phase 5 — iOS Platform Support

### M20 — Barcode Product Lookup

**Plan:** [m20-barcode/](m20-barcode/)

- [x] Phase 1 — API Clients & Lookup Service
- [x] Phase 2 — Lookup Tool & DI Registration
- [x] Phase 3 — Unit Tests
- [ ] Phase 4 — Real API Integration Tests

### M21 — Accessibility Improvements

**Plan:** [m21-accessibility/](m21-accessibility/)

- [ ] Phase 1 — Semantic Labels & Screen Reader Support
- [ ] Phase 2 — Keyboard & Focus Navigation
- [ ] Phase 3 — Dynamic Type & Text Scaling
- [ ] Phase 4 — Color Contrast & High Contrast
- [ ] Phase 5 — Reduced Motion & Audio Cues
- [ ] Phase 6 — iOS Platform Support

### M22 — Multilanguage Support

**Plan:** [m22-multilanguage/](m22-multilanguage/)

- [ ] Phase 1 — Conversation Language Setting
- [ ] Phase 2 — Vision & Tool Language Awareness
- [ ] Phase 3 — UI Localization
- [ ] Phase 4 — Live Translation Mode
- [ ] Phase 5 — iOS Platform Support

### M23 — First Start & Setup

**Plan:** [m23-setup/](m23-setup/)

- [x] Phase 1 — Permission Request Flow
- [x] Phase 2 — API Key Configuration
- [x] Phase 3 — Connectivity Check
- [x] Phase 4 — First-Start State Management
- [ ] Phase 5 — Welcome & Onboarding (Optional)

### M25 — MAF Realtime Migration

**Plan:** [m25-maf-realtime/](m25-maf-realtime/)

- [x] DI registration with MAF builder pipeline
- [x] Orchestrator rewrite (`IAsyncEnumerable` dispatch loop)
- [x] Delete hand-rolled `RealtimeClient` + DTOs
- [x] Update tests

### M27 — Settings UI Refactor

**Plan:** [m27-settings/](m27-settings/)

- [x] Settings hub + sub-pages (Connection, Voice, Device, Advanced)
- [x] Shell push navigation + UITest page objects

### M28 — UI Frames Refactoring

**Plan:** [m28-ui-frames/](m28-ui-frames/)

- [x] Wave 1 — Fix Navigation
- [x] Wave 2 — Split SettingsViewModel
- [x] Wave 3 — Move Pages to Folders
- [x] Wave 4 — Extract ContentViews
- [x] Wave 5 — Settings Card Template
- [x] Wave 6 — Test Coverage

### M29 — UI Navigation

**Plan:** [m29-ui-navigation/](m29-ui-navigation/)

- [x] Phase 1 — Shell Navigation Refactor (remove TabBar, push-based nav)
- [x] Phase 2 — UI Test Navigation Updates
- [x] Phase 3 — Settings Page Header Icon

### M30 — Polish & Optimization

**Plan:** [m30-polish/](m30-polish/)

- [ ] Phase 1 — Latency & Performance
- [ ] Phase 2 — Battery & Network
- [ ] Phase 3 — Error Handling & Resilience
- [ ] Phase 4 — Settings Page
- [ ] Phase 5 — Privacy Indicators
- [ ] Phase 6 — Cost Tracking
- [ ] Phase 7 — iOS Platform Polish

### M31 — State Redesign

**Plan:** [m31-state-redesign/](m31-state-redesign/)

- [x] Phase 1 — Status bar redesign (icons, rename, cleanup)

### M35 — .NET 10 Update

**Plan:** [m35-dotnet10-update/](m35-dotnet10-update/)

- [ ] Phase 1 — Pin SDK with `global.json`
- [ ] Phase 2 — Fix CS0618 Obsolete MAUI API Warnings
- [ ] Phase 3 — NuGet Package Update
- [ ] Phase 4 — Verify Build & Workload Notes

### M41 — USB Camera

**Plan:** [m41-usb-camera/](m41-usb-camera/)

- [x] Phase 1A — Windows Direct USB/UVC Probe
- [x] Phase 3 — Windows C# Client And BodyCam Provider

### M43 — Audio

**Plan:** [m43-audio/](m43-audio/)

- [ ] Phase 1 — [Provider Capabilities And Echo Policy](m43-audio/phase-1-provider-capabilities-policy.md)
- [ ] Phase 2 — [Split Echo Cancellation From Voice Cleanup](m43-audio/phase-2-echo-vs-cleanup.md)
- [ ] Phase 3 — [Windows, Android, and iOS Route Validation](m43-audio/phase-3-platform-route-validation.md)
- [ ] Phase 4 — [Diagnostics](m43-audio/phase-4-diagnostics.md)
- [ ] Phase 5 — [Brinell Audio Automation](m43-audio/phase-5-brinell-automation.md)
- [ ] Phase 6 — [Realtime Echo Canary](m43-audio/phase-6-realtime-echo-canary.md)

### M44 — Command Redesign

**Plan:** [m44-command-redesign/](m44-command-redesign/)

**Compatibility:** No backward compatibility required. Phase 1/2 connect Look
only; Read and Scan return as new registered commands in Phase 3/4.

- [ ] Phase 1 — [Command Contracts And Defaults](m44-command-redesign/phase-1-command-contracts.md)
- [ ] Phase 2 — [Look Command](m44-command-redesign/phase-2-look-command.md)
- [ ] Phase 2b — [Look Command And Command Settings](m44-command-redesign/phase-2b-look-command-and-command-settings.md)
- [ ] Phase 2c — [Capture Busy State](m44-command-redesign/phase-2c-capture-busy-state.md)
- [ ] Phase 3 — [Read Command](m44-command-redesign/phase-3-read-command.md)
- [ ] Phase 4 — [Scan Command](m44-command-redesign/phase-4-scan-command.md)
- [ ] Phase 5 — [UI And Accessibility](m44-command-redesign/phase-5-ui-accessibility.md)
- [ ] Phase 6 — [Provider Coverage And Tests](m44-command-redesign/phase-6-provider-coverage-tests.md)
- [ ] Phase 7 — [Future Helpful Commands](m44-command-redesign/phase-7-future-commands.md)

### M45 — Grok Provider

**Plan:** [m45-grok-provider/](m45-grok-provider/)

**Goal:** Add xAI/Grok beside OpenAI and Azure OpenAI, validate official OAuth
options, and create a provider capability model for future providers.

- [ ] Phase 1 — [Provider Registry And Settings Foundation](m45-grok-provider/phase-1-provider-registry-and-settings.md)
- [ ] Phase 2 — [Grok Auth And OAuth Spike](m45-grok-provider/phase-2-grok-auth-and-oauth-spike.md)
- [ ] Phase 3 — [Grok Text, Tools, And Vision](m45-grok-provider/phase-3-grok-text-tools-and-vision.md)
- [ ] Phase 4 — [Grok Voice, STT, And TTS](m45-grok-provider/phase-4-grok-voice-stt-and-tts.md)
- [ ] Phase 5 — [Grok Images And Command Capabilities](m45-grok-provider/phase-5-grok-images-and-command-capabilities.md)
- [ ] Phase 6 — [Connection Settings UX And Diagnostics](m45-grok-provider/phase-6-connection-settings-ux-and-diagnostics.md)
- [ ] Phase 6a — [LLM Provider Settings Design](m45-grok-provider/phase-6a-llm-provider-settings-design.md)
- [ ] Phase 7 — [Tests, Hardening, And Future Provider Readiness](m45-grok-provider/phase-7-tests-hardening-and-future-provider-readiness.md)

### M46 — HeyCyan C# WiFi Retry

**Plan:** [m46-heycyan-csharp-wifi-retry/](m46-heycyan-csharp-wifi-retry/)

**Goal:** Retry HeyCyan WiFi/media transfer with the M38 method: use the
official mobile app as an oracle, recover the BLE-to-WiFi sequence, and build a
C#-only Android path for BLE control, WiFi/P2P connection, and media download.

- [ ] Report — [Options And Chances](m46-heycyan-csharp-wifi-retry/report-options-and-chances.md)
- [ ] Phase 1 — [Mobile App Oracle Capture](m46-heycyan-csharp-wifi-retry/phase-1-mobile-app-oracle-capture.md)
- [x] Phase 1a — [First Android Oracle Shot](m46-heycyan-csharp-wifi-retry/phase-1a-first-android-oracle-shot.md)
- [x] Phase 1b — [Logged-In Location-On Oracle Run](m46-heycyan-csharp-wifi-retry/phase-1b-logged-in-location-on-oracle-run.md)
- [x] Phase 1c — [Import Transfer Observation](m46-heycyan-csharp-wifi-retry/phase-1c-import-transfer-observation.md)
- [x] Phase 1d — [Single-Photo Endpoint Probe](m46-heycyan-csharp-wifi-retry/phase-1d-single-photo-endpoint-probe.md)
- [x] Phase 1e — [Direct Media Download Proof](m46-heycyan-csharp-wifi-retry/phase-1e-direct-media-download-proof.md)
- [ ] Phase 2 — [BLE And WiFi Protocol Map](m46-heycyan-csharp-wifi-retry/phase-2-ble-and-wifi-protocol-map.md)
- [ ] Phase 3 — [Android C# WiFi Direct Connector](m46-heycyan-csharp-wifi-retry/phase-3-android-csharp-wifi-direct-connector.md)
- [ ] Phase 4 — [Media Download And Camera Provider Path](m46-heycyan-csharp-wifi-retry/phase-4-media-download-and-camera-provider-path.md)
- [x] Phase 4f — [P2P Sequence And Android Routing](m46-heycyan-csharp-wifi-retry/phase-4f-p2p-sequence-and-routing.md)
- [x] Phase 5 — [Real Hardware Test Harness](m46-heycyan-csharp-wifi-retry/phase-5-real-hardware-test-harness.md)
- [x] Phase 6 — [BodyCam Integration And UX Gate](m46-heycyan-csharp-wifi-retry/phase-6-bodycam-integration-and-ux-gate.md)
- [ ] Phase 7 — [Windows C# Wi-Fi Direct Route](m46-heycyan-csharp-wifi-retry/phase-7-windows-csharp-wifi-direct-route.md)
- [x] Phase 7a — [Windows Field Guide And First Implementation Slice](m46-heycyan-csharp-wifi-retry/phase-7a-windows-field-guide-and-first-implementation-slice.md)
- [x] Phase 7b — [Windows Route Diagnostics And Candidate Selection](m46-heycyan-csharp-wifi-retry/phase-7b-windows-route-diagnostics-and-candidate-selection.md)
- [x] Phase 7c — [Windows Artifact Probe](m46-heycyan-csharp-wifi-retry/phase-7c-windows-artifact-probe.md)
- [x] Phase 7d — [Windows Route Boundary And Pivot](m46-heycyan-csharp-wifi-retry/phase-7d-windows-route-boundary-and-pivot.md)
- [x] Phase 8 — [Remove Android Vendor AAR BLE Bridge](m46-heycyan-csharp-wifi-retry/phase-8-remove-android-vendor-aar-ble-bridge.md)

### M47 — Device Media Architecture Report

**Plan:** [m47-device-media-architecture-report/](m47-device-media-architecture-report/)

**Goal:** Explain how camera pictures/video, input audio, output audio, Device
settings, and front-page runtime source selection currently relate.

- [x] Report — [Device Media Architecture](m47-device-media-architecture-report/report.md)

### M48 — Post-PoC App Architecture Review

**Plan:** [m48-post-poc-app-architecture-review/](m48-post-poc-app-architecture-review/)

**Goal:** Review the current app architecture as BodyCam moves beyond PoC and
propose a simpler, product-phase structure that stays blind-first, plug-and-play
where useful, and deliberately not overengineered.

- [x] Report — [Architecture Review And Improvement Proposal](m48-post-poc-app-architecture-review/report.md)

---

## Superseded

### ~~M4 — Bluetooth Glasses Integration~~

Split into M11 (Camera), M12 (Audio In), M13 (Audio Out), M14 (Buttons), M17 (Glasses).

### ~~M6 — Polish & Optimization~~

Renamed to M30.

### ~~M32 — Voice Quality~~

Scope folded into M34, then superseded by M43.

### ~~M24 — Anti-Echo (WebRTC APM)~~

Archived at [archive/m24-anti-echo/](archive/m24-anti-echo/). Scope folded into
M43 so echo cancellation is provider/route dependent.

### ~~M34 — Audio Quality & Anti-Echo Improvements~~

Archived at [archive/m34-audio-quality/](archive/m34-audio-quality/). Scope
folded into M43.

### ~~M33 — HeyCyan Glasses SDK Integration~~

Archived at [archive/m33-heycyan-sdk/](archive/m33-heycyan-sdk/). Superseded by
M46 after the Android production path moved from the vendor AAR bridge to direct
C# BLE plus C# Wi-Fi/media transfer.

### ~~M36 — HeyCyan Windows Connectivity~~

Archived at [archive/m36-heycyan-windows/](archive/m36-heycyan-windows/).
Superseded by M46 Phase 7 for any future Windows HeyCyan route.
