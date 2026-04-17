# M1 — Audio Pipeline (Laptop/Phone) ✦ Core

**Status:** NOT STARTED
**Goal:** Capture mic audio, stream to OpenAI Realtime API, play back TTS — no glasses yet.

---

## Scope

| # | Task | Details |
|---|------|---------|
| 1.1 | `IAudioInputService` + Windows impl | Capture PCM 16kHz/24kHz from default mic |
| 1.2 | `IAudioOutputService` + Windows impl | Play PCM frames with low latency |
| 1.3 | `OpenAiStreamingClient` | WebSocket connection to OpenAI Realtime API |
| 1.4 | `VoiceInputAgent` (MAF) | Mic → OpenAI, emit partial transcripts |
| 1.5 | `VoiceOutputAgent` (MAF) | OpenAI TTS → speaker playback |
| 1.6 | Wire into orchestrator | Continuous mic capture → transcription → display |
| 1.7 | Android audio impl | Platform-specific mic/speaker for Android |

## Exit Criteria

- [ ] Speak into laptop mic → see transcript on screen
- [ ] Hear AI response through speakers
- [ ] Works on both Windows and Android

---

## Technical Design

### Audio Capture (Windows)

**Approach:** Use NAudio (`WaveInEvent`) for Windows mic capture.

```
NuGet: NAudio (Windows-only) or use WASAPI via NAudio
```

- Format: PCM 16-bit, mono, 24kHz (OpenAI Realtime default)
- Chunk size: 50ms = 2400 bytes (24000 × 2 bytes × 0.05s)
- `WaveInEvent` fires `DataAvailable` → emit via `AudioChunkAvailable` event

**File:** `Platforms/Windows/WindowsAudioInputService.cs`

### Audio Capture (Android)

**Approach:** Use `Android.Media.AudioRecord` via platform-specific code.

- Format: PCM 16-bit, mono, 24kHz
- Read loop on background thread
- Needs `RECORD_AUDIO` permission

**File:** `Platforms/Android/AndroidAudioInputService.cs`

### Audio Playback (Windows)

**Approach:** NAudio `WaveOutEvent` with a `BufferedWaveProvider`.

- Feed incoming PCM chunks into buffer
- WaveOut pulls from buffer automatically
- Low latency: small buffer (100-200ms)

**File:** `Platforms/Windows/WindowsAudioOutputService.cs`

### Audio Playback (Android)

**Approach:** `Android.Media.AudioTrack` in streaming mode.

**File:** `Platforms/Android/AndroidAudioOutputService.cs`

### OpenAI Realtime API Connection

**Protocol:** WebSocket to `wss://api.openai.com/v1/realtime?model=gpt-5.4-realtime`

> **NOTE:** API is now GA (not beta). Model names are `gpt-5.4-realtime` and `gpt-5.4-realtime-mini`.
> See [RESEARCH.md](RESEARCH.md) for full event catalog and session config.

**Message flow:**
```
Client                          Server
  │                               │
  ├── session.update ────────────►│  (configure session, semantic_vad, noise_reduction)
  │                               │
  ├── input_audio_buffer.append ─►│  (stream mic audio, base64 PCM)
  │   ... (continuous) ...        │
  │                               │
  │◄── input_audio_buffer         │
  │    .speech_started ───────────│  (VAD: user speaking)
  │                               │
  │◄── response.output_audio      │
  │    _transcript.delta ─────────│  (partial transcript of model speech)
  │◄── response.output_audio      │
  │    .delta ────────────────────│  (TTS audio chunks, base64 PCM)
  │◄── response.output_audio      │
  │    .done ─────────────────────│  (TTS complete, NO audio data)
  │◄── response.done ────────────│  (response complete)
  │                               │
  ├── conversation.item.truncate ►│  (interruption: discard unplayed audio)
  │                               │
```

**Key implementation details:**
- Use `System.Net.WebSockets.ClientWebSocket`
- Authentication: `Authorization: Bearer {api_key}` header (no beta header needed)
- Audio format: base64-encoded PCM 16-bit 24kHz mono
- Session config: `semantic_vad` for natural turn detection
- Noise reduction: `near_field` for headphones/glasses, `far_field` for phone mic
- Handle `response.output_audio.delta` for streaming TTS audio
- Handle `response.output_audio_transcript.delta` for streaming transcripts
- Handle `conversation.item.input_audio_transcription.completed` for user speech transcript
- **Must implement interruption handling** via `conversation.item.truncate`

### Platform-Specific Service Registration

```
// MauiProgram.cs
#if WINDOWS
builder.Services.AddSingleton<IAudioInputService, WindowsAudioInputService>();
builder.Services.AddSingleton<IAudioOutputService, WindowsAudioOutputService>();
#elif ANDROID
builder.Services.AddSingleton<IAudioInputService, AndroidAudioInputService>();
builder.Services.AddSingleton<IAudioOutputService, AndroidAudioOutputService>();
#endif
```

---

## NuGet Packages Needed

| Package | Purpose | Platform |
|---------|---------|----------|
| NAudio | 2.3.0 | Audio capture/playback | Windows |
| System.Net.WebSockets | built-in | WebSocket client | All |
| System.Text.Json | built-in | WS message serialization | All |

## Echo Cancellation Strategy

| Platform | AEC? | Approach |
|----------|------|----------|
| Windows | **NO** | Headphones mandatory |
| Android | YES | Use `AudioSource.VoiceCommunication` for OS-level AEC |
| iOS | YES | Use `.playAndRecord` + `.voiceChat` AVAudioSession mode |
| BT Glasses | N/A | Separate mic/speaker BT channels, echo unlikely |

---

## Risks

| Risk | Mitigation |
|------|-----------|
| NAudio doesn't support MAUI well | Use raw WASAPI interop as fallback |
| Realtime API format changes | Pin API version, add version check |
| Audio crackling/underrun | Tune buffer sizes, test with different chunk durations |
| Android permissions denied | Proper runtime permission request flow |

---

## Sequence Diagram

```
User speaks → Mic → AudioInputService
  → AudioChunkAvailable event
  → VoiceInputAgent.SendAudioAsync()
  → OpenAiStreamingClient (WebSocket)
  → OpenAI Realtime API

OpenAI responds:
  → transcript delta → VoiceInputAgent.TranscriptReceived
  → Orchestrator → display on UI
  → audio delta → VoiceOutputAgent
  → AudioOutputService.PlayChunkAsync()
  → Speaker
```
