# Phase 3 — Threading & Latency

**Status:** Proposed
**Depends on:** Phase 1 (correctness)
**Sibling phases:** [Phase 1](../phase-1-correctness/overview.md), [Phase 2](../phase-2-resampling/overview.md), [Phase 4](../phase-4-platform-coverage/overview.md), [Phase 5](../phase-5-polish/overview.md), [Phase 6](../phase-6-observability/overview.md)

---

## Summary

Phase 3 decouples audio processing from the realtime thread paths,
eliminating thread blocking on capture and output glitches due to API
delivery jitter. **3.1** moves WebRTC APM off the mic capture thread via a
bounded `Channel<byte[]>` with drop-oldest flow control, ensuring Android
`AudioRecord` and NAudio `WaveInEvent` never block. **3.2** adds an
adaptive jitter buffer inside [AudioOutputManager](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs)
(starting at 40 ms, growing on underruns up to 200 ms, shrinking on
overflow) so playback always has sufficient buffered frames. Together
these stabilize voice quality on lossy networks and devices with variable
speaker latency.

---

## 3.1 — Move AEC processing off the capture thread

### Symptom

**Windows:** [VoiceInputAgent.OnAudioChunk](../../../src/BodyCam/Agents/VoiceInputAgent.cs)
runs `_aec.ProcessCapture()` synchronously on the NAudio `WaveInEvent`
callback thread. APM lock + resampling + 5×GCHandle allocations takes
3–8 ms per 50 ms chunk. If a spike occurs (GC pause, page fault), the
callback thread blocks and the audio driver is starved → buffered samples
overflow and are discarded by the OS.

**Android:** [PlatformMicProvider.RecordLoopAsync](../../../src/BodyCam/Platforms/Android/PlatformMicProvider.cs)
reads from `AudioRecord` on a `Task.Run` thread. `AudioRecord.ReadAsync`
is not truly async — it's a blocking p/invoke. If AEC takes 8 ms and the
device has a 10 ms audio frame, the next frame arrives while we're still
processing the previous one and the hardware drops samples.

### Solution: bounded `Channel` with drop-oldest

Decouple the audio provider (source thread) from AEC (consumer thread)
via a `System.Threading.Channels.Channel<byte[]>`:

1. **Architecture:**
   - [AudioInputManager](../../../src/BodyCam/Services/Audio/AudioInputManager.cs) creates a bounded channel (capacity 10 chunks = 500 ms) with `FullMode = DropOldest`.
   - `OnProviderChunk` writes non-blockingly via `TryWrite`.
   - A dedicated `Task.Run` consumer drains the channel and runs AEC.
   - `VoiceInputAgent` consumes from the manager's already-AEC'd output (or stays event-based — see backwards-compat notes below).

2. **Where it lives:**
   ```csharp
   // In AudioInputManager
   private readonly Channel<byte[]> _aecChannel =
       Channel.CreateBounded<byte[]>(new BoundedChannelOptions(10)
       {
           FullMode = BoundedChannelFullMode.DropOldest,
           SingleReader = true,
           SingleWriter = false
       });

   private Task? _consumerTask;
   private long _droppedAecChunks;
   public  long DroppedAecChunks => Interlocked.Read(ref _droppedAecChunks);

   private void OnProviderChunk(object? sender, byte[] chunk)
   {
       if (!_aecChannel.Writer.TryWrite(chunk))
           Interlocked.Increment(ref _droppedAecChunks);
   }

   private async Task ConsumerLoopAsync(CancellationToken ct)
   {
       await foreach (var chunk in _aecChannel.Reader.ReadAllAsync(ct))
       {
           byte[] processed = _aec?.ProcessCapture(chunk) ?? chunk;
           AudioChunkAvailable?.Invoke(this, processed);
       }
   }
   ```

3. **Capacity 10 chunks = 500 ms:** at 24 kHz / 16-bit / 50 ms per chunk =
   2400 bytes; 10 chunks = 24 KB total. Big enough to absorb GC + network
   spikes; small enough that real-time stays close.

4. **Drop-oldest** rather than drop-newest because if we're already 500 ms
   behind real time, the user's most recent words matter more than
   half-second-stale audio.

5. **Ordering guarantee for AEC:**
   - Render frames are fed synchronously inside [VoiceOutputAgent.PlayAudioDeltaAsync](../../../src/BodyCam/Agents/VoiceOutputAgent.cs)
     — they reach `AecProcessor` before the corresponding capture, which is
     exactly the temporal relationship APM expects.
   - Capture frames flow through the channel sequentially. The channel
     preserves enqueue order; the single-reader consumer preserves
     processing order.

6. **Backwards-compat note:** keep `AudioInputManager.AudioChunkAvailable` event
   firing **after** AEC (post-channel) so existing consumers don't change
   behaviour. Producers (providers) wire to `OnProviderChunk` instead.

### Test plan

**Unit — `AudioInputManager_AecChannel`:**
- Mock provider raises 100 chunks rapidly while a stub AEC sleeps 10 ms each.
- Assert `TryWrite` returns true initially, then false on overflow.
- Assert `DroppedAecChunks > 80` (consumer can't keep up; oldest dropped).
- Assert producer thread is never blocked > 1 ms.

**Stress (Android) — `CaptureThreadDoesNotBlock`:**
- Instrument `RecordLoopAsync` with a `Stopwatch` between successive `ReadAsync` calls.
- Inject a 20 ms slow AEC operation.
- Assert max gap ≤ 60 ms (50 ms expected + 10 ms slack).
- Without the channel, this test fails.

**Integration — `VoiceInputAgent_ThreadingStress`:**
- Real platform mic at 24 kHz / 50 ms; real AEC.
- Run 10 s.
- Assert `DroppedAecChunks == 0` under normal conditions.

### Acceptance

- [ ] Bounded channel (10 chunks, DropOldest) inside `AudioInputManager`.
- [ ] Dedicated consumer task running AEC.
- [ ] Drop counter exposed (used by Phase 6 metrics).
- [ ] Capture thread never blocks under simulated AEC stalls.
- [ ] Existing `AudioChunkAvailable` event semantics preserved (post-AEC).

---

## 3.2 — Output jitter buffer with adaptive sizing

### Symptom

The Realtime API delivers audio at irregular intervals — sometimes 40 ms
after the previous chunk, sometimes 80 ms. Today
[AudioOutputManager.PlayChunkAsync](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs)
forwards directly to the provider; on Windows
[WindowsSpeakerProvider](../../../src/BodyCam/Platforms/Windows/WindowsSpeakerProvider.cs)'s
`BufferedWaveProvider.AddSamples` underruns when delivery jitter exceeds
the buffer depth, producing audible clicks. Android `AudioTrack.Write` is
blocking — slow delivery starves it.

### Solution: adaptive jitter buffer

A buffer that:
- Starts at a small target depth (40 ms).
- Grows on underrun events (up to 200 ms).
- Shrinks on overflow events (down to 40 ms).
- Resets on `ClearBuffer` / interruption.

### Where it lives

New file `src/BodyCam/Services/Audio/JitterBuffer.cs`:

```csharp
public sealed class JitterBuffer
{
    private const int MinDepthMs = 40;
    private const int MaxDepthMs = 200;
    private const int StepMs     = 50;
    private const int CooldownSec = 5;

    private readonly Channel<byte[]> _queue;
    private readonly ILogger<JitterBuffer> _logger;
    private int _targetDepthMs = MinDepthMs;
    private DateTime _lastChange = DateTime.UtcNow;
    private long _underruns, _overflows;

    public int  CurrentTargetMs => _targetDepthMs;
    public long Underruns       => Interlocked.Read(ref _underruns);
    public long Overflows       => Interlocked.Read(ref _overflows);

    public ValueTask EnqueueAsync(byte[] pcm, CancellationToken ct);
    public Task DrainToProviderAsync(IAudioOutputProvider p, int sampleRate, CancellationToken ct);
    public void Clear();
    private void OnUnderrun() { /* grow with cooldown */ }
    private void OnOverflow() { /* shrink with cooldown */ }
}
```

Integrate inside `AudioOutputManager`:

```csharp
public async Task PlayChunkAsync(byte[] pcm, CancellationToken ct = default)
{
    if (_active is null) return;
    if (_jitter is not null)
        await _jitter.EnqueueAsync(pcm, ct);
    else
        await _active.PlayChunkAsync(pcm, ct);
}

public void ClearBuffer()
{
    _jitter?.Clear();
    _active?.ClearBuffer();
}
```

A drain task (started in `StartAsync`) pulls from the jitter buffer at the
provider's rate, monitors fill level, and adjusts target depth.

### Adaptive sizing logic

- **Underrun**: drain found queue empty when it expected data → grow target
  by `StepMs` up to `MaxDepthMs`. Wait `CooldownSec` before next adjustment.
- **Overflow**: queue depth exceeds `2 × _targetDepthMs` → shrink by `StepMs`
  down to `MinDepthMs` (cooldown).
- Both events count via `Underruns` / `Overflows` for Phase 6 metrics.

### Interaction with interruption

When [VoiceOutputAgent.HandleInterruption](../../../src/BodyCam/Agents/VoiceOutputAgent.cs)
fires, `AudioOutputManager.ClearBuffer` clears both the jitter buffer
*and* the provider's internal buffer. The jitter buffer also resets its
adaptive target to `MinDepthMs` because a brand-new playback context
starts fresh.

### Test plan

**Unit `JitterBuffer_AdaptiveResizing`:**
- Mock provider that reports "drained" 3 times in a row → assert target grew to 90 ms then 140 ms then stays.
- Sustained overflow (queue filled to 2× target, no underruns for 5 s) → assert target shrinks back.
- Always within `[MinDepthMs, MaxDepthMs]`.

**Integration `AudioOutputManager_VariableDelivery`:**
- Mock API delivering with intervals `{10, 100, 20, 50, 30, 90, ...}` ms.
- Mock provider records timestamps of each `PlayChunkAsync` call.
- Assert no gap > 10 ms between provider calls (jitter absorbed).

**Manual (Windows):** play a 30-second response while throttling network → no audible clicks.

### Acceptance

- [ ] `JitterBuffer` class with adaptive depth `[40, 200] ms`.
- [ ] Integrated into `AudioOutputManager.PlayChunkAsync` / `ClearBuffer`.
- [ ] Underrun/overflow counters exposed.
- [ ] Variable-delivery integration test passes.
- [ ] No audible clicks in manual test on Windows + Android.

---

## Files touched

| File | Change |
|------|--------|
| [AudioInputManager.cs](../../../src/BodyCam/Services/Audio/AudioInputManager.cs) | Bounded channel + consumer; drop counter. |
| [VoiceInputAgent.cs](../../../src/BodyCam/Agents/VoiceInputAgent.cs) | Stop running AEC inline (still subscribes for post-AEC chunks). |
| New: `src/BodyCam/Services/Audio/JitterBuffer.cs` | Adaptive buffer. |
| [AudioOutputManager.cs](../../../src/BodyCam/Services/Audio/AudioOutputManager.cs) | Wraps provider with jitter buffer; clear forwards. |
| [VoiceOutputAgent.cs](../../../src/BodyCam/Agents/VoiceOutputAgent.cs) | Verify `HandleInterruption` wiring (no behaviour change). |
| New: `src/BodyCam.Tests/Services/Audio/JitterBufferTests.cs` | Adaptive sizing unit tests. |
| New: `src/BodyCam.Tests/Services/Audio/AudioInputManagerChannelTests.cs` | Drop-oldest + thread-safety tests. |
| New: `src/BodyCam.IntegrationTests/Audio/CaptureThreadStressTests.cs` | Capture thread non-blocking under load. |

---

## Execution order within Phase 3

1. **3.1** first — moves the heavy work off the audio callback thread; everything downstream benefits.
2. **3.2** second — depends on `AudioOutputManager` being structurally clean (3.1 doesn't touch it but having tests in place makes 3.2 safer).
