# M13 — Output Audio Architecture

**Status:** PLANNING  
**Goal:** Unified audio output abstraction supporting multiple audio output destinations —
phone speaker, laptop speaker, BT glasses, BT earbuds/headset, and USB audio — with one
active output at a time and automatic fallback.

**Depends on:** M12 (BT audio input) for shared BT device enumeration/pairing infrastructure.

---

## Why This Matters

BodyCam is a voice AI assistant that speaks back to the user. The current audio output
is tightly coupled to a single `IAudioOutputService` with platform-specific implementations
that always route to the default system audio output. There's no way to select a specific
output device, enumerate available devices, or handle BT audio routing.

A smart glasses AI assistant needs audio output to work with:
- BT smart glasses speaker (the primary use case — user hears AI through glasses)
- BT earbuds/headset (any paired BT audio device)
- Phone speaker (fallback when no external device)
- Laptop speaker via NAudio (dev/testing)
- USB speaker/headset (USB audio class devices)

All of these should funnel through a single abstraction so `VoiceOutputAgent` and the
orchestrator don't care where audio goes.

---

## Audio Output Destinations

| Destination | Protocol | Latency | Notes |
|-------------|----------|---------|-------|
| **Phone speaker** | Android AudioTrack (existing) | <20ms | Default fallback |
| **Laptop speaker** | NAudio WaveOutEvent (existing) | ~200ms | Dev/testing |
| **BT glasses speaker** | A2DP / HFP profile | 50-150ms | Glasses appear as BT audio device |
| **BT earbuds/headset** | A2DP profile | 30-100ms | Any paired BT audio device |
| **USB speaker/headset** | USB Audio / platform API | <20ms | Standard USB audio device class |

---

## Architecture

### Core Abstraction

```
IAudioOutputProvider (per destination type)
  ├── PhoneSpeakerProvider           ← Android AudioTrack wrapper
  ├── WindowsSpeakerProvider         ← NAudio WaveOutEvent wrapper
  ├── BluetoothAudioOutputProvider   ← A2DP/HFP routing
  └── UsbAudioOutputProvider         ← USB Audio device routing

AudioOutputManager (single active output)
  → Selects provider
  → Routes PCM data via IAudioOutputProvider
  → Handles fallback on disconnection
```

### Data Flow

```
┌─────────────────────┐
│  Realtime API       │  AudioDelta events (PCM bytes)
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  VoiceOutputAgent   │  PlayAudioDeltaAsync() + interruption tracking
└─────────┬───────────┘
          │ IAudioOutputProvider (was IAudioOutputService)
          ▼
┌─────────────────────┐
│  AudioOutputManager │  One active provider, device selection, fallback
└─────────┬───────────┘
          │ IAudioOutputProvider.PlayChunkAsync()
          ▼
┌─────────────────────┐
│  Audio Destination  │  (glasses, phone, BT earbuds, USB, laptop)
└─────────────────────┘
```

### Interruption Flow

```
User speaks → Orchestrator.OnInputSpeechStarted
  → VoiceOutputAgent.HandleInterruption()
    → AudioOutputManager.ClearBuffer()
      → active IAudioOutputProvider.ClearBuffer()
```

---

## Phases

### Phase 1: Audio Output Abstraction & Platform Providers
Refactor existing code into `IAudioOutputProvider` abstraction. Wrap existing
`WindowsAudioOutputService` → `WindowsSpeakerProvider`, `AndroidAudioOutputService`
→ `PhoneSpeakerProvider`. Create `AudioOutputManager`. Update `VoiceOutputAgent` to
use the new abstraction. All existing features continue to work.

**Deliverables:** `IAudioOutputProvider`, `AudioOutputManager`, `PhoneSpeakerProvider`,
`WindowsSpeakerProvider`, updated DI registration, settings UI for output device selection.

### Phase 2: Bluetooth Audio Output
Implement `BluetoothAudioOutputProvider` for routing audio to paired BT devices.
Uses A2DP profile for high-quality audio or HFP for lower-latency speech. Device
enumeration, selection, and auto-reconnect.

**Deliverables:** `BluetoothAudioOutputProvider`, BT device enumeration,
A2DP/HFP routing, codec selection (SBC/AAC/aptX).

### Phase 3: USB Audio Output
Implement `UsbAudioOutputProvider` for USB speakers and headsets. Windows:
NAudio device selection by DeviceId. Android: USB Audio Class via USB Host API.

**Deliverables:** `UsbAudioOutputProvider`, USB device enumeration, hot-plug detection.

### Phase 4: Advanced Audio Management
Audio ducking for notification sounds, volume management per-provider, audio focus
handling on Android, simultaneous output scenarios (e.g. notification on phone
while voice on glasses).

**Deliverables:** Volume control per-provider, audio ducking, audio focus management,
notification audio routing.

### Phase 5: iOS Platform Support
Implement `PhoneSpeakerProvider` for iOS using `AVAudioEngine` with a player node
for PCM playback. Handle `AVAudioSession` configuration for output routing.
`ClearBuffer` resets the player node for interruption handling. Register in DI with
`#elif IOS`.

**Deliverables:** iOS `PhoneSpeakerProvider` (AVAudioEngine), `ClearBuffer` via
player node reset, `AVAudioSession` output routing, audio route change handling.

---

## Exit Criteria

- [ ] `IAudioOutputProvider` interface defined and implemented for all 4 provider types
- [ ] `AudioOutputManager` manages active provider with UI for device selection
- [ ] `VoiceOutputAgent` works with any audio output destination
- [ ] Audio plays through BT glasses when connected
- [ ] Settings page has audio output device picker
- [ ] Automatic fallback to phone/laptop speaker when selected device disconnects
- [ ] Interruption handling (ClearBuffer) works across all providers
- [ ] Audio format (16-bit PCM, mono, dynamic sample rate) preserved across all providers

---

## Documents

| Document | Purpose |
|----------|---------|
| [overview.md](overview.md) | This file — scope, phases, exit criteria |
| [audio-output-abstraction.md](audio-output-abstraction.md) | IAudioOutputProvider interface, AudioOutputManager, DI setup |
| [platform-providers.md](platform-providers.md) | Phone/laptop speaker providers, USB audio routing |
| [bt-audio-output.md](bt-audio-output.md) | Bluetooth audio output — A2DP, HFP, codec selection, latency |
