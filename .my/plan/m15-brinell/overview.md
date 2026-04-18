# M15 — Brinell Test Extensions (Audio & Camera)

**Status:** PLANNING  
**Goal:** Add audio injection and camera simulation capabilities to Brinell so BodyCam
UI tests can exercise the full vision/audio pipeline without real hardware.

**Depends on:** M11 (camera abstraction), M12 (audio input abstraction),
M13 (audio output abstraction), M14 (button input abstraction).

---

## Why This Matters

BodyCam's core loop is: **capture frame → send to AI → receive audio response → play**.
Today's UI tests can only verify button existence and navigation — they can't test:

- "User presses Look → camera captures frame → AI responds → audio plays"
- "Microphone picks up speech → wake word triggers → session starts"
- "Camera provider disconnects → app falls back to phone camera"

To test these flows without real hardware or API calls, Brinell needs:

1. **Mock audio providers** that inject pre-recorded PCM or simulate mic input
2. **Mock camera providers** that return test frames (JPEG files on disk)
3. **Assertion helpers** to verify audio was played, frames were captured, etc.
4. **Test-only DI registration** that swaps real providers for mocks

---

## What Brinell Needs

| Capability | BodyCam Interface | Brinell Extension | Purpose |
|------------|-------------------|-------------------|---------|
| Inject test audio input | `IAudioInputProvider` | `TestMicProvider` | Feed pre-recorded audio into the pipeline |
| Capture audio output | `IAudioOutputProvider` | `TestSpeakerProvider` | Record what the app played for assertions |
| Inject test camera frames | `ICameraProvider` | `TestCameraProvider` | Supply JPEG frames without a real camera |
| Simulate button presses | `IButtonInputProvider` | `TestButtonProvider` | Fire button events programmatically |
| Assert pipeline results | — | Assertion helpers | Verify end-to-end flow completed |

---

## Architecture

### Test Provider Pattern

BodyCam already uses provider abstractions (M11–M14). Brinell adds **test
implementations** that plug into the same DI slots:

```
Production:                          Test:
┌──────────────────┐                ┌──────────────────────┐
│ PlatformMicProv  │                │ TestMicProvider      │
│ (real hardware)  │                │ (feeds PCM from file)│
└────────┬─────────┘                └────────┬─────────────┘
         │                                   │
         │  IAudioInputProvider              │
         ▼                                   ▼
┌──────────────────┐                ┌──────────────────────┐
│ AudioInputManager│                │ AudioInputManager    │
│ (same code)      │                │ (same code)          │
└──────────────────┘                └──────────────────────┘
```

### DI Swap Strategy

BodyCam tests use a `TestMauiApp` builder that replaces real providers:

```csharp
// In test project setup
builder.Services.RemoveAll<IAudioInputProvider>();
builder.Services.AddSingleton<IAudioInputProvider>(new TestMicProvider("wake-word.pcm"));
builder.Services.AddSingleton<IAudioInputProvider>(new TestMicProvider("question.pcm"));

builder.Services.RemoveAll<ICameraProvider>();
builder.Services.AddSingleton<ICameraProvider>(new TestCameraProvider("frames/"));

builder.Services.RemoveAll<IButtonInputProvider>();
builder.Services.AddSingleton<IButtonInputProvider>(new TestButtonProvider());
```

### Data Flow in Tests

```
Test Setup                    App Under Test               Assertions
┌──────────────┐             ┌──────────────────┐         ┌────────────────┐
│ Load test PCM│────────────▶│ AudioInputManager │         │                │
│ Load test JPG│────────────▶│ CameraManager     │────────▶│ Verify AI call │
│              │             │ Orchestrator      │         │ made           │
│              │             │ VisionAgent       │         │                │
│              │             └────────┬──────────┘         │                │
│              │                      │                    │                │
│              │             ┌────────▼──────────┐         │ Verify audio   │
│              │             │ TestSpeakerProv   │────────▶│ was played     │
│              │             │ (captures output) │         │                │
└──────────────┘             └───────────────────┘         └────────────────┘
```

---

## Phases

### Phase 1: Test Provider Implementations
Create test-only provider implementations that satisfy BodyCam's existing
interfaces. These live in the BodyCam.Tests or BodyCam.UITests project (not in
Brinell itself — Brinell doesn't know about BodyCam interfaces).

**Deliverables:** `TestMicProvider`, `TestSpeakerProvider`, `TestCameraProvider`,
`TestButtonProvider`, test asset files (PCM, JPEG).

### Phase 2: Test DI Infrastructure
Build a `TestMauiAppBuilder` or fixture extension that swaps production providers
for test providers, wires mock API clients, and manages the test lifecycle.

**Deliverables:** `TestAppBuilder`, fixture base class with provider setup,
environment variable configuration for test assets path.

### Phase 3: End-to-End Test Scenarios
Write integration-level UI tests that exercise real pipelines with fake data:
- Look command with test frame → verify vision request made
- Wake word detection with test audio → verify session activates
- Button press → verify correct action dispatched

**Deliverables:** E2E test classes, test data assets, CI pipeline integration.

### Phase 4: Brinell Core Extensions (Optional)
If patterns prove reusable, promote shared abstractions to Brinell packages:
- `Brinell.Mocking` gets audio/camera/sensor mock builders
- `Brinell.Maui` gets `IMauiSensorContext` for test-injected sensor data

**Deliverables:** Brinell.Mocking extensions, documentation, samples.

### Phase 5: iOS Test Support
Verify all test providers work in an iOS test host (simulator). Configure
iOS simulator test runner for CI. Ensure `TestMicProvider`, `TestCameraProvider`,
and `TestButtonProvider` initialize correctly on iOS. Add iOS-specific test
assets if audio/camera format differs.

**Deliverables:** iOS simulator test runner configuration, verified test
providers on iOS, CI pipeline with macOS runner for iOS headless tests.

---

## Exit Criteria

- [ ] Test providers implement all four BodyCam abstractions (audio in/out, camera, buttons)
- [ ] DI swap works — test app starts with mock providers, no real hardware needed
- [ ] At least one E2E test exercises the full Look pipeline (frame → AI → audio)
- [ ] Test assets (PCM files, JPEG frames) are checked in and documented
- [ ] CI can run these tests headless on Windows

---

## Documents

| Document | Purpose |
|----------|---------|
| [overview.md](overview.md) | This file — scope, phases, exit criteria |
| [brinell-audio-camera.md](brinell-audio-camera.md) | How to add audio & camera test support to Brinell/BodyCam |
| [test-cases.md](test-cases.md) | Complete test cases for all 13 tools × 3 invocation paths |
| [phase1-providers.md](phase1-providers.md) | Phase 1 — Test provider implementations (code, unit tests, assets) |
| [phase2-di-infrastructure.md](phase2-di-infrastructure.md) | Phase 2 — DI swap, TestServiceAccessor, BodyCamTestFixture |
| [phase3-e2e-tests.md](phase3-e2e-tests.md) | Phase 3 — Integration, button dispatch, fallback, audio, UI tests |
| [phase4-brinell-extensions.md](phase4-brinell-extensions.md) | Phase 4 — Promote generics to Brinell.Mocking & Brinell.Maui |
