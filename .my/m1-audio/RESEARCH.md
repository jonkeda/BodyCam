# M1 Audio Pipeline — Research

**Date:** 2026-04-16
**Status:** COMPLETE

---

## 1. NAudio (Windows Audio)

### Overview
- **Package:** `NAudio` v2.3.0 (released 12 Mar 2026)
- **License:** MIT
- **Platform:** Windows only (.NET 6+, .NET Core 3.1+, .NET Framework 4.7.2+)
- **NuGet:** 10.8M total downloads
- **Split packages:** `NAudio` (meta), `NAudio.Core`, `NAudio.WinMM`, `NAudio.Wasapi`, `NAudio.Asio`

### Recording APIs

| API | Class | Best For | Notes |
|-----|-------|----------|-------|
| WaveIn (Event) | `WaveInEvent` | **General purpose (RECOMMENDED)** | Background thread, event-driven, most reliable |
| WASAPI | `WasapiCapture` | Low-latency, sample-rate control | Supports sample rate conversion since v2.1; exclusive mode fixed in v2.3.0 |
| ASIO | `AsioOut` | Professional audio interfaces | Lowest latency but requires ASIO driver |

**Decision: Use `WaveInEvent`** for M1. It's the most broadly compatible, works on all Windows versions, and uses a simple event-based model that fits our streaming needs perfectly.

#### WaveInEvent Recording Pattern
```csharp
var waveIn = new WaveInEvent
{
    WaveFormat = new WaveFormat(24000, 16, 1), // 24kHz, 16-bit, mono
    BufferMilliseconds = 50  // 50ms chunks = ~2400 bytes
};

waveIn.DataAvailable += (s, e) =>
{
    // e.Buffer contains PCM data, e.BytesRecorded is actual count
    // IMPORTANT: always use e.BytesRecorded, NOT e.Buffer.Length
    var chunk = new byte[e.BytesRecorded];
    Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
    AudioChunkAvailable?.Invoke(this, chunk);
};

waveIn.StartRecording();
// ... later ...
waveIn.StopRecording();
waveIn.Dispose();
```

### Playback APIs

| API | Class | Best For | Notes |
|-----|-------|----------|-------|
| WaveOut (Event) | `WaveOutEvent` | **General purpose (RECOMMENDED)** | Default choice, universal, 300ms default latency |
| WASAPI | `WasapiOut` | Low-latency monitoring | Shared mode auto-resamples since v2.1; Vista+; fussy about formats |
| DirectSound | `DirectSoundOut` | Alternative to WaveOut | Simple, widely supported |
| ASIO | `AsioOut` | Pro audio | Lowest latency, requires ASIO driver |

**Decision: Use `WaveOutEvent` + `BufferedWaveProvider`** for M1. Provides reliable streaming playback with minimal complexity.

#### Streaming Playback Pattern
```csharp
var waveFormat = new WaveFormat(24000, 16, 1); // must match OpenAI output
var buffer = new BufferedWaveProvider(waveFormat)
{
    BufferDuration = TimeSpan.FromSeconds(5),
    DiscardOnBufferOverflow = true  // prevent memory growth
};

var waveOut = new WaveOutEvent
{
    DesiredLatency = 200  // ms, can lower for less delay (risk of stutter)
};
waveOut.Init(buffer);
waveOut.Play(); // starts pulling from buffer

// Feed incoming audio chunks:
buffer.AddSamples(pcmChunk, 0, pcmChunk.Length);

// Stop:
waveOut.Stop();
waveOut.Dispose();
```

### Echo Cancellation: NOT AVAILABLE
NAudio provides **zero echo cancellation**. There is no AEC (Acoustic Echo Cancellation) in any NAudio API.

**Mitigation: Headphones are MANDATORY on Windows.** This is a hardware constraint, not a software one. The user has already confirmed this decision.

### Key Gotchas
1. `DataAvailable` buffer: always copy `e.BytesRecorded` bytes, not `e.Buffer.Length`
2. Only call `Init()` once per `IWavePlayer` instance — create new for different formats
3. `WaveOutEvent` `DesiredLatency` is total buffer duration, not per-buffer
4. `BufferedWaveProvider` defaults to 5s buffer; set `DiscardOnBufferOverflow = true` for streaming
5. `RecordingStopped` event fires after `StopRecording()` — dispose WaveFileWriter there
6. WaveFormat must match between capture and the API's expected input (24kHz, 16-bit, mono)

---

## 2. OpenAI Realtime API (GA — NOT Beta)

### Connection
- **Protocol:** WebSocket
- **URL:** `wss://api.openai.com/v1/realtime?model=gpt-5.4-realtime`
- **Auth:** `Authorization: Bearer {api_key}` header
- **Also:** No more `OpenAI-Beta: realtime=v1` header — API is GA now
- **Max session duration:** 60 minutes
- **Models:** `gpt-5.4-realtime` (full), `gpt-5.4-realtime-mini` (cheaper/faster)

### Audio Format
- **Input:** PCM 16-bit, 24kHz sample rate, mono
- **Output:** PCM 16-bit, 24kHz sample rate, mono (configurable)
- **Encoding:** Base64 in JSON messages over WebSocket
- **Max chunk size:** 15 MB per `input_audio_buffer.append` event

### Session Configuration (GA format)
```json
{
  "type": "session.update",
  "session": {
    "type": "realtime",
    "model": "gpt-5.4-realtime",
    "output_modalities": ["audio"],
    "audio": {
      "input": {
        "format": { "type": "audio/pcm", "rate": 24000 },
        "turn_detection": { "type": "semantic_vad" },
        "noise_reduction": { "type": "near_field" }
      },
      "output": {
        "format": { "type": "audio/pcm" },
        "voice": "marin"
      }
    },
    "instructions": "You are a helpful assistant..."
  }
}
```

### Voice Activity Detection (VAD)

| Mode | Description | Best For |
|------|-------------|----------|
| `server_vad` | Volume-based speech detection | Simple, reliable |
| `semantic_vad` | ML-based turn detection + VAD | **Natural conversations (RECOMMENDED)** — handles "uhhm" pauses better |
| `null` (disabled) | Manual commit + response.create | Push-to-talk |

**Decision: Use `semantic_vad`** — it provides the most natural conversational flow. The model understands when the user has actually finished speaking vs. just pausing.

### Noise Reduction (Built-in!)

| Type | Description |
|------|-------------|
| `near_field` | Close-talking mic (headphones, earbuds) |
| `far_field` | Laptop mic, conference room mic |

**Decision:** 
- **Windows (headphones):** `near_field`
- **Android/iOS (phone mic):** `far_field`
- **Glasses (BT mic):** `near_field` (close to face)

### Key Client Events

| Event | Purpose |
|-------|---------|
| `session.update` | Configure session (instructions, tools, audio format, VAD) |
| `input_audio_buffer.append` | Stream mic audio chunks (base64 PCM) |
| `input_audio_buffer.commit` | Manual commit (only needed when VAD disabled) |
| `input_audio_buffer.clear` | Clear audio buffer |
| `response.create` | Trigger model response (only needed when VAD disabled) |
| `response.cancel` | Cancel in-progress response |
| `conversation.item.create` | Add text/image/audio items to conversation |
| `conversation.item.truncate` | Truncate unplayed assistant audio (for interruptions) |

### Key Server Events

| Event | Purpose |
|-------|---------|
| `session.created` | Session ready |
| `session.updated` | Session config confirmed |
| `input_audio_buffer.speech_started` | User started speaking (VAD) |
| `input_audio_buffer.speech_stopped` | User stopped speaking (VAD) |
| `input_audio_buffer.committed` | Audio committed to conversation |
| `response.created` | Model started generating |
| `response.output_audio.delta` | **TTS audio chunk (base64 PCM)** — this is the audio to play |
| `response.output_audio.done` | TTS complete (no audio data in this event!) |
| `response.output_audio_transcript.delta` | Partial transcript of model's speech |
| `response.output_audio_transcript.done` | Final transcript |
| `conversation.item.input_audio_transcription.completed` | User speech transcript |
| `conversation.item.input_audio_transcription.delta` | Incremental user transcript |
| `response.done` | Response complete (contains full text, NOT audio) |
| `error` | Error occurred (most are recoverable, session stays open) |
| `rate_limits.updated` | Token reservation info |

### Interruption Handling (WebSocket — MUST IMPLEMENT)
When using WebSocket (not WebRTC), the client must handle interruptions manually:

1. Monitor `input_audio_buffer.speech_started` — user started talking while model is speaking
2. Immediately stop audio playback, note how much was played
3. Send `conversation.item.truncate` with `audio_end_ms` = milliseconds of audio actually played
4. Server auto-cancels in-progress response

This is **critical** for natural conversation. Without it, the model doesn't know what the user heard vs. didn't hear.

### Function Calling
Fully supported — configure tools in `session.update`, model emits `function_call` items, client executes and returns results via `conversation.item.create` with `type: "function_call_output"`.

### Image Input
Supported by `gpt-5.4-realtime` and `gpt-5.4-realtime-mini` — send base64 image via `conversation.item.create` with `type: "input_image"`. This is useful for M3 (Vision).

---

## 3. Platform Audio — Android

### Recording: `Android.Media.AudioRecord`
```csharp
// Platform/Android specific
var audioRecord = new AudioRecord(
    AudioSource.Mic,
    sampleRate: 24000,
    channelConfig: ChannelIn.Mono,
    encoding: Encoding.Pcm16bit,
    bufferSize: AudioRecord.GetMinBufferSize(24000, ChannelIn.Mono, Encoding.Pcm16bit)
);

audioRecord.StartRecording();

// Read loop on background thread:
var buffer = new byte[2400]; // 50ms at 24kHz 16-bit mono
while (isRecording)
{
    int bytesRead = audioRecord.Read(buffer, 0, buffer.Length);
    if (bytesRead > 0)
        AudioChunkAvailable?.Invoke(this, buffer[..bytesRead]);
}

audioRecord.Stop();
audioRecord.Release();
```

### Permissions Required
```xml
<!-- Platforms/Android/AndroidManifest.xml -->
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```
Must also request runtime permission via MAUI Permissions API.

### Playback: `Android.Media.AudioTrack`
```csharp
var audioTrack = new AudioTrack(
    new AudioAttributes.Builder()
        .SetUsage(AudioUsageKind.Media)
        .SetContentType(AudioContentType.Speech)
        .Build(),
    new AudioFormat.Builder()
        .SetSampleRate(24000)
        .SetChannelMask(ChannelOut.Mono)
        .SetEncoding(Encoding.Pcm16bit)
        .Build(),
    bufferSize,
    AudioTrackMode.Stream,
    AudioManager.AudioSessionIdGenerate
);

audioTrack.Play();
audioTrack.Write(pcmChunk, 0, pcmChunk.Length); // streaming write
audioTrack.Stop();
audioTrack.Release();
```

### Echo Cancellation: HANDLED BY OS
Android's audio stack includes `AcousticEchoCanceler` (since API 16). When using the standard `AudioRecord` with `AudioSource.VoiceCommunication`, the OS applies AEC automatically.

**Key:** Use `AudioSource.VoiceCommunication` instead of `AudioSource.Mic` to get automatic AEC on Android.

---

## 4. Platform Audio — iOS (Future — M4 or later)

### Recording: `AVAudioEngine`
iOS uses `AVAudioEngine` with an input node tap for real-time mic capture.

```
// Conceptual (would be native interop or binding):
AVAudioEngine engine;
engine.InputNode.InstallTap(bufferSize: 1200, format: pcm24kMono, handler);
engine.Start();
```

### Echo Cancellation: HANDLED BY OS
iOS `AVAudioSession` with category `.playAndRecord` and mode `.voiceChat` enables the built-in AEC pipeline automatically. This is the standard approach for voice apps on iOS.

---

## 5. Echo Cancellation Summary

| Platform | AEC Available? | Approach |
|----------|---------------|----------|
| **Windows** | **NO** (NAudio has none) | **Headphones mandatory** |
| **Android** | YES (OS-level) | Use `AudioSource.VoiceCommunication` |
| **iOS** | YES (OS-level) | Use `.playAndRecord` + `.voiceChat` |
| **Glasses (BT)** | Depends on hardware | BT headset profile = separate mic/speaker channels, echo unlikely |

---

## 6. Realtime-First Architecture

### Core Insight
The OpenAI Realtime API handles **STT + reasoning + TTS in a single WebSocket session**. This makes separate `ConversationAgent` (Chat Completions) and `VoiceOutputAgent` (separate TTS) unnecessary for the primary voice loop. The Realtime session IS the conversation — audio goes in, audio + transcripts come out.

### What Changes from Current Scaffold

| Current Component | Realtime-First Role | Change |
|---|---|---|
| `IOpenAiStreamingClient` | **Replace** with `IRealtimeClient` | Event-based (not `IAsyncEnumerable`), single WS handles everything |
| `VoiceInputAgent` | **Simplify** — just pipes mic → `IRealtimeClient` | Remove `RunAsync` loop, remove transcript handling (events do it) |
| `ConversationAgent` | **Demote** to local history tracker | No more Chat Completions call — Realtime handles reasoning. Agent only tracks messages for UI/context |
| `VoiceOutputAgent` | **Simplify** — plays audio deltas from events | No more `SynthesizeStreamingAsync` loop. Plays `AudioDelta` event chunks + tracks playback position for interruption |
| `AgentOrchestrator` | **Event-driven** — subscribes to `IRealtimeClient` events | No more VoiceIn→Conversation→VoiceOut pipeline. Orchestrator wires: events → agents → UI |
| `VisionAgent` | **Uses Realtime** for image input | `gpt-5.4-realtime` supports `input_image` natively — no separate Vision API call needed |
| `AppSettings` | Add Realtime-specific settings | `RealtimeModel`, `Voice`, `TurnDetection`, `NoiseReduction`, `SystemInstructions` |

### Proposed `IRealtimeClient` Interface
```csharp
public interface IRealtimeClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    bool IsConnected { get; }

    // Audio streaming
    Task SendAudioChunkAsync(byte[] pcm16Data, CancellationToken ct = default);

    // Manual controls (push-to-talk when VAD disabled)
    Task CommitAudioBufferAsync(CancellationToken ct = default);
    Task CreateResponseAsync(CancellationToken ct = default);
    Task CancelResponseAsync(CancellationToken ct = default);

    // Interruption handling
    Task TruncateResponseAudioAsync(string itemId, int audioEndMs, CancellationToken ct = default);

    // Vision input (gpt-5.4-realtime supports images natively)
    Task SendImageAsync(byte[] imageData, string format = "jpeg", CancellationToken ct = default);

    // Session management
    Task UpdateSessionAsync(RealtimeSessionConfig config, CancellationToken ct = default);

    // Events — multiple concurrent streams from the single WebSocket
    event EventHandler<byte[]>? AudioDelta;               // TTS audio chunks
    event EventHandler<string>? OutputTranscriptDelta;     // Model's speech transcript
    event EventHandler<string>? InputTranscriptCompleted;  // User's speech transcript
    event EventHandler? SpeechStarted;                     // VAD: user speaking
    event EventHandler? SpeechStopped;                     // VAD: user stopped
    event EventHandler<RealtimeResponseInfo>? ResponseDone;// Response complete
    event EventHandler<string>? ErrorOccurred;             // Recoverable errors
}
```

**Why events over `IAsyncEnumerable`:** The Realtime API emits multiple independent streams concurrently (audio deltas + transcript deltas + speech detection + errors). Events let us handle all of them without multiple consumer loops competing for the same WebSocket read.

### New Flow
```
                   ┌─────────────────────────────┐
                   │   OpenAI Realtime API (WS)   │
                   │  STT + Reasoning + TTS       │
                   └──────┬──────────────┬────────┘
                          │              │
              audio chunks│              │events (audio delta, transcripts,
              (base64 PCM)│              │ speech started/stopped, errors)
                          │              │
                   ┌──────▼──────────────▼────────┐
                   │       IRealtimeClient         │
                   └──────┬──────────────┬────────┘
                          │              │
              ┌───────────┘              └───────────┐
              │                                      │
    ┌─────────▼─────────┐              ┌─────────────▼──────────┐
    │   VoiceInputAgent  │              │     AgentOrchestrator   │
    │   (mic → realtime) │              │  (events → agents → UI) │
    └───────────────────┘              └─────────────┬──────────┘
                                                     │
                                         ┌───────────┼───────────┐
                                         │           │           │
                                  ┌──────▼───┐ ┌────▼─────┐ ┌──▼──────────┐
                                  │VoiceOut   │ │Convo     │ │VisionAgent  │
                                  │Agent      │ │Agent     │ │(image→      │
                                  │(play TTS) │ │(history) │ │ realtime)   │
                                  └──────────┘ └──────────┘ └─────────────┘
```

### Milestones Impact

| Milestone | Old Plan | Realtime-First Plan |
|-----------|----------|---------------------|
| **M1 Audio** | Mic→OpenAI transcript, separate TTS playback | Mic→Realtime session (full voice loop in one shot) |
| **M2 Conversation** | Add Chat Completions reasoning agent | **ABSORBED into M1** — Realtime handles reasoning natively. M2 becomes: system prompt design, function calling, context window management |
| **M3 Vision** | Separate gpt-5.4 Vision API call | Send images directly into Realtime session via `input_image` — model integrates vision with conversation natively |
| **M4 Glasses** | Unchanged | Unchanged |
| **M5 Smart Features** | Unchanged | Function calling via Realtime tools becomes the mechanism |
| **M6 Polish** | Unchanged | Unchanged |

### When Realtime Is NOT the Right Choice
- **Batch text processing** (summarization, translation of documents) → use Chat Completions REST API
- **Image-only analysis** without voice context → could use Vision API directly
- **Cost sensitivity** for text-only interactions → Chat Completions is cheaper than Realtime

For BodyCam, Realtime is the default path for all interactive voice+vision. Fall back to REST APIs only for background/batch tasks.

---

## 7. Key Architecture Decisions

### Decision 1: Realtime-First for all interactive voice+vision
**Why:** The Realtime API combines STT + reasoning + TTS in one session, eliminating separate agents and API calls. It's the natural fit for a voice-driven wearable.

### Decision 2: WebSocket (not WebRTC)
**Why:** We're a .NET MAUI app, not a browser. `System.Net.WebSockets.ClientWebSocket` is built-in. WebRTC would need a native library per platform.

### Decision 3: semantic_vad for hands-free, null for push-to-talk
**Why:** Smart glasses are hands-free → semantic_vad is ideal. Phone/laptop could offer push-to-talk option for noisy environments.

### Decision 4: Headphones on Windows, speaker OK on mobile
**Why:** No AEC on Windows. Android/iOS handle it in the OS audio stack.

### Decision 5: NAudio WaveInEvent + WaveOutEvent (Windows)
**Why:** Most compatible, event-driven, reliable for streaming. WasapiOut could be a future optimization for lower latency.

### Decision 6: 24kHz, 16-bit, mono PCM everywhere
**Why:** This is the OpenAI Realtime API's native format. Matching it end-to-end avoids any resampling.

### Decision 7: 50ms chunks for mic capture
**Why:** ~2400 bytes per chunk at 24kHz/16-bit/mono. Small enough for responsive VAD, large enough to avoid excessive WebSocket message overhead.

### Decision 8: Events over IAsyncEnumerable for Realtime events
**Why:** Multiple concurrent streams (audio + transcripts + VAD + errors) are natural as events. IAsyncEnumerable would require multiple consumer loops or a multiplexer.

### Decision 9: Chat Completions / Vision API only for batch/background tasks
**Why:** Realtime handles interactive voice+vision natively. Fall back to REST APIs only for non-interactive work (summarization, document analysis, etc.).

---

## 8. Updated NuGet Packages

| Package | Version | Platform | Purpose |
|---------|---------|----------|---------|
| NAudio | 2.3.0 | Windows | Audio capture + playback |
| System.Net.WebSockets.Client | built-in | All | WebSocket to OpenAI |
| System.Text.Json | built-in | All | JSON serialization for WS messages |

No additional packages needed for Android audio (uses platform APIs directly).

---

## 9. Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| NAudio WaveFormat mismatch | No audio / garbage audio | Hard-code 24kHz/16bit/mono, validate on init |
| WebSocket disconnects | Conversation lost | Auto-reconnect with exponential backoff, session state recovery |
| Base64 encoding overhead | ~33% bandwidth increase | Unavoidable with WS JSON protocol; chunks are small |
| BufferedWaveProvider overflow | Audio glitch/skip | Set `DiscardOnBufferOverflow = true`, tune buffer size |
| 60-minute session limit | Session drops | Detect approaching limit, graceful reconnect |
| Android permission denied | No mic access | Graceful error, explain to user, re-prompt |
| High latency on slow networks | Poor UX | Monitor round-trip time, adjust chunk size, warn user |
| Model generates audio faster than realtime | Buffer bloat on playback | Interruption handling + truncation |
