# RCA-005: Garbled/Overlapping Voice Playback

## Symptom

The AI's voice output sounds garbled — as if a new audio segment starts playing before the previous one finishes. Audio chunks seem to overlap, creating a "double voice" or stuttering effect.

## Architecture

```
API:  response.audio.delta  →  base64 PCM chunks (24kHz, 16-bit mono)
         ↓
RealtimeClient.DispatchMessage()  →  AudioDelta event (byte[])
         ↓
AgentOrchestrator.OnAudioDelta()  →  async void handler
         ↓
VoiceOutputAgent.PlayAudioDeltaAsync()
         ↓
WindowsAudioOutputService.PlayChunkAsync()  →  _buffer.AddSamples()
         ↓
NAudio WaveOutEvent  →  reads from BufferedWaveProvider → speaker
```

## Root Cause: `BytesPlayed` tracks bytes *sent to buffer*, not bytes *actually played*

The `AudioPlaybackTracker.BytesPlayed` is incremented in `VoiceOutputAgent.PlayAudioDeltaAsync()`:

```csharp
public async Task PlayAudioDeltaAsync(byte[] pcmData, CancellationToken ct = default)
{
    await _audioOutput.PlayChunkAsync(pcmData, ct);
    _tracker.BytesPlayed += pcmData.Length;  // ← bytes BUFFERED, not played
}
```

And `WindowsAudioOutputService.PlayChunkAsync()` just adds to the buffer:

```csharp
public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
{
    if (_buffer is null || !IsPlaying) return Task.CompletedTask;
    _buffer.AddSamples(pcmData, 0, pcmData.Length);  // ← non-blocking
    return Task.CompletedTask;
}
```

**`AddSamples` is non-blocking** — it writes PCM data into a ring buffer. NAudio's `WaveOutEvent` reads from this buffer asynchronously on a separate thread. So `BytesPlayed` tracks how much data has been *queued*, not how much has been *played through the speaker*.

This means `PlayedMs` in the tracker always overestimates how far into the audio the speaker actually is. When interruption/truncation happens, it reports a later timestamp than reality.

### But how does this cause overlapping audio?

The garbling is **not** from the truncation bug. It has a simpler cause:

**The `async void OnAudioDelta` handler has no concurrency control:**

```csharp
private async void OnAudioDelta(object? sender, byte[] pcmData)
{
    try { await _voiceOut.PlayAudioDeltaAsync(pcmData); }
    catch (Exception ex) { DebugLog?.Invoke(this, $"Playback error: {ex.Message}"); }
}
```

Since `PlayChunkAsync` returns `Task.CompletedTask` (synchronously), the `await` completes immediately and there's no actual concurrency issue *with the current implementation*. The `AddSamples` calls are sequential because the event handler runs on the receive loop thread.

### The real cause: `DiscardOnBufferOverflow = true`

```csharp
_buffer = new BufferedWaveProvider(waveFormat)
{
    BufferDuration = TimeSpan.FromSeconds(5),
    DiscardOnBufferOverflow = true   // ← THIS
};
```

When the API sends audio data faster than the speaker plays it (which happens during bursts of `response.audio.delta`), the `BufferedWaveProvider` can overflow. With `DiscardOnBufferOverflow = true`, **NAudio silently drops audio data that doesn't fit in the buffer**.

This creates the "garbled" effect:

1. A burst of audio deltas arrive rapidly (5-10 in quick succession from the API)
2. NAudio's buffer (5 seconds) fills up because the speaker hasn't played enough yet
3. New `AddSamples` calls discard the data that doesn't fit
4. The speaker plays a gap-filled version: beginning of sentence → gap → later part of sentence
5. This sounds like overlapping or stuttering because the listener hears discontinuous audio

### Secondary factor: No back-pressure

The API streams `response.audio.delta` as fast as the model generates them. There's no back-pressure from the client — the WebSocket receive loop calls `DispatchMessage` synchronously, which fires `AudioDelta`, which calls `AddSamples`. If the buffer is full, data is silently dropped. The API doesn't know to slow down.

## Evidence from RealTests

The `AllEventTypesLogged` test showed the event distribution for a typical response:

```
response.audio.delta: 5           ← for short "Hi" response
response.audio_transcript.delta: 8
```

For the cookie recipe (long response), the ratio would be much higher — potentially 20-50 audio deltas in a few seconds. At 24kHz 16-bit mono, each delta is typically 4800 bytes (100ms of audio). 50 deltas × 4800 bytes = 240KB = 5 seconds of audio. The 5-second buffer is exactly at the limit.

## Proposed Fixes

### Fix 1: Increase buffer duration

```csharp
_buffer = new BufferedWaveProvider(waveFormat)
{
    BufferDuration = TimeSpan.FromSeconds(30),  // ← increase from 5 to 30
    DiscardOnBufferOverflow = true
};
```

Simple but doesn't solve the root problem — just delays it for very long responses.

### Fix 2: Don't discard on overflow — block instead

```csharp
_buffer = new BufferedWaveProvider(waveFormat)
{
    BufferDuration = TimeSpan.FromSeconds(10),
    DiscardOnBufferOverflow = false  // ← throws if overflow
};
```

With `DiscardOnBufferOverflow = false`, `AddSamples` throws `InvalidOperationException` when the buffer is full. We'd need to handle this with retry/wait logic. This adds complexity but prevents silent data loss.

### Fix 3: Use back-pressure with a blocking buffer (recommended)

Replace `BufferedWaveProvider` with a bounded producer-consumer pattern:

```csharp
public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
{
    if (_buffer is null || !IsPlaying) return;

    // Wait until there's space in the buffer
    while (_buffer.BufferedBytes > _buffer.BufferLength - pcmData.Length)
    {
        await Task.Delay(10, ct);  // Back-pressure: wait for playback to drain
    }

    _buffer.AddSamples(pcmData, 0, pcmData.Length);
}
```

This creates natural back-pressure — when the buffer is nearly full, the caller waits for the speaker to catch up before adding more data.

**Implication:** `PlayChunkAsync` would now actually be async, and the audio delta event handler would need to respect this. Since `OnAudioDelta` is `async void`, it would correctly await the back-pressure delay.

However, this blocks the `DispatchMessage` pipeline (since the event is raised synchronously from the receive loop). To avoid blocking all event processing, audio data should be queued to a separate channel/task.

### Fix 4: Decouple audio buffering from the receive loop

Use a `Channel<byte[]>` to decouple the receive loop from audio playback:

```csharp
private readonly Channel<byte[]> _audioChannel = Channel.CreateBounded<byte[]>(100);

// In DispatchMessage for audio.delta:
_audioChannel.Writer.TryWrite(audioBytes);

// Separate playback loop:
private async Task AudioPlaybackLoopAsync(CancellationToken ct)
{
    await foreach (var chunk in _audioChannel.Reader.ReadAllAsync(ct))
    {
        while (_buffer.BufferedBytes > maxBuffer)
            await Task.Delay(10, ct);
        _buffer.AddSamples(chunk, 0, chunk.Length);
    }
}
```

This is the most robust solution — the receive loop never blocks, and the playback loop applies back-pressure independently.

## Files Involved

| File | Role |
|------|------|
| `src/BodyCam/Platforms/Windows/WindowsAudioOutputService.cs` | `BufferedWaveProvider` with 5s buffer + discard on overflow |
| `src/BodyCam/Agents/VoiceOutputAgent.cs` | `PlayAudioDeltaAsync` — no back-pressure |
| `src/BodyCam/Orchestration/AgentOrchestrator.cs` | `OnAudioDelta` — async void, no concurrency control |
| `src/BodyCam/Services/RealtimeClient.cs` | `DispatchMessage` — fires events synchronously from receive loop |

## Severity

**High** — Audio is the primary output channel. Garbled voice makes the app unusable for real conversations.
