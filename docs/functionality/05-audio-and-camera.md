# 05 — Audio Pipeline and Camera

## Audio Architecture

```
┌─────────────────────────────────────────────────────┐
│                    Platform Layer                     │
│  Windows: NAudio (WASAPI)    Android: AudioRecord     │
│  IAudioInputProvider ────→ AudioInputManager          │
│  IAudioOutputProvider ───→ AudioOutputManager         │
└──────────┬──────────────────────────┬────────────────┘
           ↓                          ↓
    IAudioInputService          IAudioOutputService
           ↓                          ↑
    VoiceInputAgent            VoiceOutputAgent
     (capture PCM)            (play response PCM)
           ↓                          ↑
      AecProcessor ←─ render ref ─────┘
     (echo cancel)
           ↓
    audio sink callback
           ↓
    session.SendAsync()  ←──→  Realtime WebSocket  ──→ OutputAudioDelta
```

## Audio Input Chain

### Platform Providers
Each platform registers an `IAudioInputProvider`:
- **Windows:** NAudio WASAPI capture (16-bit PCM, 24kHz mono)
- **Android:** `AudioRecord` with `AudioSource.VoiceCommunication`
- **Bluetooth:** Additional providers registered dynamically when Bluetooth audio devices are discovered

### AudioInputManager
- Implements `IAudioInputService`
- Manages multiple input providers (platform mic, Bluetooth devices)
- `InitializeAsync()` — restores last active provider or defaults to "platform"
- `SetActiveAsync(providerId)` — switch between audio sources
- Forwards `AudioChunkAvailable` events from the active provider
- **Singleton** — same instance shared between VoiceInputAgent and DI

### VoiceInputAgent Flow
1. `StartAsync()` → subscribes to `IAudioInputService.AudioChunkAvailable`
2. On each chunk:
   - Check `_isConnected` flag (set by orchestrator after session creation)
   - Check `_audioSink` is not null
   - Apply AEC processing if enabled: `_aec.ProcessCapture(chunk)`
   - Forward processed PCM to sink: `_audioSink(processed, ct)`
3. The sink (set by orchestrator) wraps PCM in MAF message and sends to WebSocket

### Audio Format
- **Sample rate:** 24,000 Hz (configurable in AppSettings)
- **Channels:** 1 (mono)
- **Bit depth:** 16-bit signed PCM
- **Chunk duration:** 50ms (configurable)
- **Chunk size:** 24000 × 2 × 0.05 = 2,400 bytes per chunk

## Audio Output Chain

### AudioOutputManager
- Implements `IAudioOutputService`
- Similar multi-provider architecture as input
- Platform-specific playback (NAudio on Windows, AudioTrack on Android)

### VoiceOutputAgent Flow
1. `StartAsync()` → initialize output service
2. Receives decoded PCM from message loop (`OutputAudioDelta` messages)
3. `PlayChunkAsync(pcmData)` → queues to output service
4. Feeds PCM to AEC as render reference (so echo cancellation knows what's playing)
5. Tracks bytes played via `AudioPlaybackTracker`

### Speech Interruption
When user starts speaking during AI playback:
1. Realtime API sends `input_audio_buffer.speech_started`
2. Orchestrator calls `VoiceOutputAgent.HandleInterruption()` → clears audio buffer
3. Records current playback position (`PlayedMs`)
4. Sends `conversation.item.truncate` to API with item ID and playback position
5. API stops generating audio for that response

## Echo Cancellation (AEC)

**File:** `Services/Audio/WebRtcApm/AecProcessor.cs`

Prevents the AI from hearing its own voice through the speakers:

1. `Initialize(mobile)` — configure for near-field (phone) or far-field
2. Speaker output feeds render reference: `ProcessRender(speakerPcm)`
3. Mic input is cleaned: `ProcessCapture(micPcm)` → returns echo-cancelled PCM
4. Configurable via `AppSettings.AecEnabled` (default: true)

## Microphone Coordination

**`IMicrophoneCoordinator`** manages mic handoff between wake word engine and Realtime session:
- `TransitionToActiveSessionAsync()` — hand mic to Realtime API
- `TransitionToWakeWordAsync()` — hand mic back to wake word engine

On mobile, only one component can hold the microphone at a time.

## Bluetooth Audio

On both Windows and Android:
- `BluetoothEnumerator` scans for paired Bluetooth audio devices
- `StartListening()` watches for device connect/disconnect
- Discovered devices are registered as additional `IAudioInputProvider` / `IAudioOutputProvider`
- User can select them in Device Settings

---

## Camera Architecture

```
┌────────────────────────────────┐
│ CommunityToolkit.Maui          │
│ CameraView (XAML control)      │
│   ├─ StartCameraPreview()      │
│   ├─ StopCameraPreview()       │
│   └─ CaptureImage() → JPEG    │
└──────────┬─────────────────────┘
           ↓
    PhoneCameraProvider
    (ICameraProvider)
           ↓
    CameraManager
    (manages active provider)
           ↓
    FrameCaptureFunc delegate
    (set on AgentOrchestrator)
           ↓
    ToolContext.CaptureFrame
    (used by vision tools)
```

### CameraView (UI Control)
- MAUI CameraView from CommunityToolkit.Maui
- Defined in `MainPage.xaml` as `CameraPreview`
- Two consumers get a reference:
  - `PhoneCameraProvider.SetCameraView()` — called from MainPage constructor
  - `MainViewModel.SetCameraView()` — called from MainPage constructor

### PhoneCameraProvider
- Implements `ICameraProvider`
- Wraps CameraView for the provider abstraction
- Registered in `CameraManager`

### CameraManager
- Manages multiple camera providers (phone camera, potentially external cameras)
- `CaptureFrameAsync()` — captures JPEG from active provider
- Used as fallback when `FrameCaptureFunc` is not set on orchestrator

### Frame Capture for Vision
When the orchestrator needs a camera frame (for tools):
1. `ToolContext.CaptureFrame` is called
2. Resolves to `FrameCaptureFunc` (set by MainViewModel) or `_cameraManager.CaptureFrameAsync` (fallback)
3. `MainViewModel.CaptureFrameFromCameraViewAsync()`:
   - Subscribes to `CameraView.MediaCaptured` event
   - Calls `CameraView.CaptureImage()`
   - Waits for callback with JPEG bytes
   - Returns bytes to caller

### Camera Lifecycle
- Camera preview starts when switching to Camera tab (`SwitchToCameraCommand`)
- Camera preview starts when entering Active session (`SetLayerAsync → Active`)
- Camera preview stops when leaving Active session
- Camera preview stops when switching to Transcript tab (if not in Active session)
