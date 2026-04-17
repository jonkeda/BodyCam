# M12 — Input Audio Architecture

**Status:** PLANNING  
**Goal:** Unified audio input abstraction supporting multiple microphone sources — smart
glasses (BT), phone mic, USB headsets, and WiFi audio streams — with one active source
at a time and automatic fallback.

**Depends on:** M11 (Camera Architecture) for the provider pattern. M4 retains BT
button mapping; M12 absorbs BT/WiFi audio routing from M4.

---

## Why This Matters

BodyCam is a voice-driven AI assistant. The current `IAudioInputService` is tightly
coupled to a single platform microphone — there's no way to select a different audio
input device, enumerate available devices, or automatically switch when a Bluetooth
headset connects.

A body-worn AI assistant needs audio from:
- Smart glasses on your face (BT HFP mic, or WiFi audio stream)
- A Bluetooth headset
- A USB headset or microphone
- The phone or laptop mic (existing, as fallback)

All sources should funnel through a single abstraction so `VoiceInputAgent`,
`MicrophoneCoordinator`, and the Realtime API pipeline don't care where audio comes from.

---

## Audio Input Sources

| Source | Protocol | Latency | Quality | Notes |
|--------|----------|---------|---------|-------|
| **Phone mic** | Android AudioRecord (existing) | <10ms | Good | Default fallback |
| **Laptop mic** | NAudio WaveInEvent (existing) | <10ms | Good | Dev/testing default |
| **BT glasses mic** | BT HFP/SCO profile | 20-50ms | 8-16kHz mono | Glasses appear as BT audio device |
| **BT headset mic** | BT HFP profile | 20-50ms | 8-16kHz mono | Any BT headset with mic |
| **USB mic/headset** | Platform audio API | <10ms | High | Standard USB audio class |
| **WiFi glasses mic** | WiFi audio stream | 30-100ms | Configurable | Glasses stream audio over WiFi |

---

## Architecture

### Core Abstraction

```
IAudioInputProvider (per source type)
  ├── PlatformMicProvider             ← Wraps existing IAudioInputService impls
  ├── BluetoothAudioProvider          ← BT HFP/SCO device
  ├── UsbAudioProvider                ← USB audio device (NAudio/platform API)
  └── WifiAudioProvider               ← WiFi audio stream from glasses

AudioInputManager (single active source)
  → Selects provider
  → Receives PCM chunks via IAudioInputProvider
  → Forwards via AudioChunkAvailable event
  → Implements IAudioInputService for backward compatibility
```

### Data Flow

```
┌─────────────────┐
│  Audio Source    │  (glasses, phone, USB, BT headset)
└────────┬────────┘
         │ IAudioInputProvider.AudioChunkAvailable → byte[] (PCM16)
         ▼
┌─────────────────┐
│ AudioInputManager│  One active provider, device selection
│ : IAudioInputService  (backward compatible)
└────────┬────────┘
         │ AudioChunkAvailable event
         ▼
┌─────────────────┐
│  VoiceInputAgent │  Pipes to Realtime API
│  → IRealtimeClient.SendAudioChunkAsync(chunk)
└─────────────────┘
```

### Interaction with MicrophoneCoordinator

```
┌─────────────────────┐
│ MicrophoneCoordinator│  Coordinates mic ownership
└────────┬────────────┘
         │ TransitionToActiveSessionAsync() / TransitionToWakeWordAsync()
         ▼
┌─────────────────────┐
│  AudioInputManager   │  Start/Stop the active provider
│  (IAudioInputService)│
└─────────────────────┘
```

`MicrophoneCoordinator` continues to work as-is — it calls `IAudioInputService.StartAsync`
and `StopAsync`, which route through `AudioInputManager` to the active provider.

---

## Phases

### Phase 1: Audio Input Abstraction & Platform Mic
Refactor existing code into the `IAudioInputProvider` abstraction. Wrap existing
`WindowsAudioInputService` and `AndroidAudioInputService` into `PlatformMicProvider`.
`AudioInputManager` implements `IAudioInputService` for backward compatibility.
All existing features continue to work.

**Deliverables:** `IAudioInputProvider`, `AudioInputManager`, `PlatformMicProvider`
(Windows + Android), settings for audio device selection, tests.

### Phase 2: Bluetooth Audio
Implement `BluetoothAudioProvider` for BT HFP/SCO devices. Device enumeration
on Windows (NAudio MMDevice API) and Android (BluetoothHeadset/AudioManager).
Auto-connect to paired BT glasses. SCO audio routing.

**Deliverables:** `BluetoothAudioProvider`, BT device enumeration, auto-connect,
SCO audio routing, fallback on disconnect.

### Phase 3: USB Audio Devices
Implement `UsbAudioProvider` for USB microphones and headsets. On Windows, NAudio
can enumerate and select specific USB audio devices via MMDevice. On Android,
USB audio devices appear as standard audio sources.

**Deliverables:** `UsbAudioProvider`, USB device enumeration, hot-plug detection.

### Phase 4: WiFi Audio Stream
Implement `WifiAudioProvider` for glasses that stream audio over WiFi. Protocol
depends on glasses model — could be raw PCM over TCP, WebSocket, or custom protocol.

**Deliverables:** `WifiAudioProvider`, WiFi audio stream client, protocol adapters.

---

## Exit Criteria

- [ ] `IAudioInputProvider` interface defined and implemented for platform mic, BT, USB
- [ ] `AudioInputManager` manages active provider with backward-compatible `IAudioInputService`
- [ ] VoiceInputAgent and MicrophoneCoordinator work without code changes
- [ ] BT audio devices can be enumerated and selected
- [ ] Settings page has audio input device picker
- [ ] Automatic fallback to phone/laptop mic when selected source disconnects
- [ ] Audio format remains 16-bit PCM mono at configurable sample rate

---

## Documents

| Document | Purpose |
|----------|---------|
| [overview.md](overview.md) | This file — scope, phases, exit criteria |
| [audio-input-abstraction.md](audio-input-abstraction.md) | IAudioInputProvider interface, AudioInputManager, DI setup |
| [platform-providers.md](platform-providers.md) | Phone/laptop mic providers, USB audio, wrapping existing implementations |
| [bt-audio.md](bt-audio.md) | Bluetooth audio input — HFP/SCO, device enumeration, auto-connect |
