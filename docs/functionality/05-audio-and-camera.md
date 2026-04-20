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

Prevents the AI from hearing its own voice through the speakers. Each platform uses a different AEC strategy optimized for its audio hardware.

### Architecture Overview

```
                        ┌──────────────────────────────────────┐
                        │          AgentOrchestrator            │
                        │  decides which AEC to activate:      │
                        │  • Desktop → WebRTC APM              │
                        │  • Android/iOS → Platform AEC        │
                        └──────────────────────────────────────┘

  ┌─────────────────────────────────┐    ┌────────────────────────────────────────┐
  │       Windows (Desktop)          │    │         Android / iOS (Mobile)          │
  │                                  │    │                                        │
  │  NAudio WaveInEvent (mic)        │    │  AudioRecord                           │
  │         │                        │    │  AudioSource.VoiceCommunication         │
  │         ▼                        │    │         │                              │
  │  VoiceInputAgent                 │    │  AcousticEchoCanceler (HAL)            │
  │   └─ AecProcessor.ProcessCapture│    │  NoiseSuppressor (HAL)                 │
  │         │                        │    │         │                              │
  │    24kHz→48kHz resample          │    │  VoiceInputAgent (passthrough)         │
  │    WebRTC APM (AEC3 + NS + AGC) │    │         │                              │
  │    48kHz→24kHz resample          │    │         ▼                              │
  │         │                        │    │  session.SendAsync()                   │
  │         ▼                        │    │                                        │
  │  session.SendAsync()             │    │  AudioTrack                            │
  │                                  │    │  AudioUsageKind.VoiceCommunication     │
  │  NAudio WaveOutEvent (speaker)   │    │  Shared AudioSessionId with AudioRecord│
  │         ▲                        │    │  Routed to loudspeaker                 │
  │  VoiceOutputAgent                │    │         ▲                              │
  │   └─ AecProcessor.FeedReference  │    │  VoiceOutputAgent (no AEC feed)        │
  └─────────────────────────────────┘    └────────────────────────────────────────┘
```

### Windows — WebRTC APM

**Files:**
- `Services/Audio/WebRtcApm/AecProcessor.cs` — managed wrapper
- `Services/Audio/WebRtcApm/WebRtcApmInterop.cs` — P/Invoke declarations
- `runtimes/win-x64/native/webrtc-apm.dll` — native library (from SoundFlow.Extensions.WebRtc.Apm, MIT + BSD-3)

**How it works:**

1. On session start, `AgentOrchestrator` calls `AecProcessor.Initialize(mobileMode: false)` (desktop only)
2. The native WebRTC APM is created with:
   - **AEC3** — echo cancellation (desktop mode)
   - **Noise suppression** — level High
   - **High-pass filter** — removes DC offset
   - **AGC** — adaptive digital gain, target -3 dBFS
3. **Speaker → AEC reference:** `VoiceOutputAgent.PlayAudioDeltaAsync()` calls `AecProcessor.FeedRenderReference(pcmData)` before playing each chunk. This feeds the render stream so the AEC knows what's being played.
4. **Mic → AEC processing:** `VoiceInputAgent.OnAudioChunk()` calls `AecProcessor.ProcessCapture(chunk)`. Internally:
   - Resamples 24kHz → 48kHz (APM requires 48kHz)
   - Splits into 10ms frames (480 samples at 48kHz)
   - Processes each frame through `webrtc_apm_process_stream()`
   - Resamples 48kHz → 24kHz
5. Stream delay is set to **40ms** (typical desktop speaker-to-mic latency)
6. All native calls are serialized under a lock (WebRTC APM is not thread-safe)

**Latency overhead:** ~4-6ms per 50ms chunk (resampling + native processing)

**Toggle:** `AppSettings.AecEnabled` (default: true). When disabled, mic audio passes through unprocessed.

### Android — Platform AcousticEchoCanceler

**Files:**
- `Platforms/Android/PlatformMicProvider.cs` — mic capture with hardware AEC
- `Platforms/Android/PhoneSpeakerProvider.cs` — speaker output with shared session

**How it works:**

1. `PlatformMicProvider` creates an `AudioRecord` with `AudioSource.VoiceCommunication` — this tells Android the audio is for two-way communication (not just recording)
2. After creating the `AudioRecord`, it attaches:
   - `AcousticEchoCanceler.Create(audioSessionId)` — hardware AEC integrated into the audio HAL
   - `NoiseSuppressor.Create(audioSessionId)` — hardware noise suppression
   - Both references are held alive for the duration of recording
3. `PhoneSpeakerProvider` creates an `AudioTrack` with:
   - `AudioUsageKind.VoiceCommunication` — routes through the communication audio path where AEC operates
   - **Shared `AudioSessionId`** from `PlatformMicProvider` — allows the platform AEC to correlate speaker output with mic input
4. Audio is routed to the **loudspeaker** (not the earpiece):
   - Android 12+: `AudioManager.SetCommunicationDevice(BuiltinSpeaker)`
   - Older: `AudioManager.SpeakerphoneOn = true`
5. Routing is restored on stop/dispose via `ClearCommunicationDevice()` / `SpeakerphoneOn = false`

**Why platform AEC, not WebRTC APM on Android:**
- The platform AEC runs in the audio HAL with **exact knowledge of device timing** — no need to guess stream delay
- WebRTC APM requires precise timing alignment between render reference and mic capture, which is unreliable on Android's variable-latency audio pipeline
- Running both causes double-processing artifacts and voice distortion
- Zero CPU overhead in managed code — all processing happens in native audio firmware

**Key requirement:** The `AudioRecord` must start **before** the `AudioTrack` so the session ID is available. The orchestrator's `StartAsync()` sequence ensures this: mic pipeline starts, then audio output starts.

### iOS — Platform AEC (planned)

iOS uses `AVAudioSession` with category `.playAndRecord` and mode `.voiceChat`, which enables the built-in AEC automatically. Similar to Android — no WebRTC APM needed.

### Configuring AEC

| Setting | Default | Effect |
|---------|---------|--------|
| `AppSettings.AecEnabled` | `true` | Enables/disables WebRTC APM (desktop only). Android always uses platform AEC. |

### Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| AI responds to itself (echo loop) | AEC not correlating speaker/mic | Verify shared audio session ID, check `VoiceCommunication` usage |
| Audio very quiet on Android | Routed to earpiece | Verify `SetCommunicationDevice(BuiltinSpeaker)` or `SpeakerphoneOn` |
| Voice sounds distorted | Double AEC processing | Ensure WebRTC APM is disabled on Android (`IsEnabled = false`) |
| Echo on Bluetooth | BT has different latency | Bluetooth providers use separate audio path; platform AEC handles BT natively |

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
