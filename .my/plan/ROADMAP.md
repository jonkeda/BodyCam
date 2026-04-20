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

- [x] Phase 1 — Porcupine Engine Integration
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
- [ ] Phase 4 — iOS Platform Support

### M17 — Glasses Integration

**Plan:** [m17-glasses/](m17-glasses/)

- [ ] Phase 1 — Hardware Investigation & BT Audio
- [ ] Phase 2 — Buttons & Camera
- [ ] Phase 3 — Connection UI & Auto-Fallback
- [ ] Phase 4 — iOS Platform Support

### M18 — QR Code & Barcode Scanning

**Plan:** [m18-qr-code/](m18-qr-code/)

- [x] Phase 1 — Core QR Scanning
- [x] Phase 2 — Scan UI
- [x] Phase 3 — Barcode Support + History
- [x] Phase 4 — Content-Aware Actions
- [ ] Phase 5 — iOS Platform Support
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

### M24 — Anti-Echo (WebRTC APM)

**Plan:** [m24-anti-echo/](m24-anti-echo/)

- [ ] Windows AEC (WebRTC APM via P/Invoke)
- [x] Android AEC Verification
- [ ] iOS AEC
- [x] Reference Signal Plumbing (`AecProcessor` + resampling)

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

### M29 — UI Navigation

**Plan:** [m29-ui-navigation/](m29-ui-navigation/)

- [x] Phase 1 — Shell Navigation Refactor (remove TabBar, push-based nav)
- [x] Phase 2 — UI Test Navigation Updates
- [x] Phase 3 — Settings Page Header Icon

### M31 — State Redesign

**Plan:** [m31-state-redesign/](m31-state-redesign/)

- [x] Phase 1 — Status bar redesign (icons, rename, cleanup)

### M28 — UI Frames Refactoring

**Plan:** [m28-ui-frames/](m28-ui-frames/)

- [x] Wave 1 — Fix Navigation
- [x] Wave 2 — Split SettingsViewModel
- [x] Wave 3 — Move Pages to Folders
- [x] Wave 4 — Extract ContentViews
- [x] Wave 5 — Settings Card Template
- [x] Wave 6 — Test Coverage

### M30 — Polish & Optimization

**Plan:** [m30-polish/](m30-polish/)

- [ ] Phase 1 — Latency & Performance
- [ ] Phase 2 — Battery & Network
- [ ] Phase 3 — Error Handling & Resilience
- [ ] Phase 4 — Settings Page
- [ ] Phase 5 — Privacy Indicators
- [ ] Phase 6 — Cost Tracking
- [ ] Phase 7 — iOS Platform Polish

---

## Superseded

### ~~M4 — Bluetooth Glasses Integration~~

Split into M11 (Camera), M12 (Audio In), M13 (Audio Out), M14 (Buttons), M17 (Glasses).

### ~~M6 — Polish & Optimization~~

Renamed to M30.
