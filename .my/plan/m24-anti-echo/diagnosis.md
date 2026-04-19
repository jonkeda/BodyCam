# M24 — "AI replying to itself" — Root cause analysis

## Symptom

When you ask a question, the AI starts responding — but then immediately starts replying to its own response, creating a runaway loop. This continues until the AI is interrupted or the conversation degenerates.

## Is it echo?

**Almost certainly yes, but the specific mechanism matters.** There are two layers at play:

### Layer 1 — Acoustic echo (mic picks up speaker)

The speaker plays the AI's voice. The mic hears it. Those audio bytes go straight to the Realtime API. The API's server-side VAD sees this as "the user is speaking" and interrupts itself — or worse, **treats the echoed audio as a new user turn** and starts a new response.

**Evidence this is happening:**
- **Windows has zero echo cancellation.** The `PlatformMicProvider` on Windows uses raw NAudio `WaveInEvent` with no AEC. Every word the AI speaks through the speaker goes straight back into the mic.
- **Android has hardware AEC** (`AudioSource.VoiceCommunication` + `AcousticEchoCanceler`), but you still see the problem — meaning the hardware AEC is either not fully cancelling the echo, or the problem isn't purely acoustic.

### Layer 2 — The mic never stops streaming

This is the more fundamental issue. Looking at the code:

```
VoiceInputAgent.OnAudioChunk:
    if (_realtime.IsConnected)
        await _realtime.SendAudioChunkAsync(chunk);   // ← ALWAYS. No gating.
```

**The mic streams audio to the API continuously** — even while the AI is playing back its response. There is:
- No muting during AI playback
- No turn-based gating in the orchestrator
- No coordination between `VoiceOutputAgent` and `VoiceInputAgent`
- No "is the AI currently speaking?" check anywhere in the pipeline

The **only** defense against the AI hearing itself is:
1. **Server-side `semantic_vad`** — tries to distinguish real speech from echo. But it's not designed to be the primary echo cancellation; it's turn-detection, not AEC.
2. **Android hardware AEC** — platform-level, partially effective.
3. **Nothing on Windows.**

## Could it be something other than echo?

### Possibility: Software audio routing loop

On some Windows setups, "Stereo Mix" or virtual audio devices route system output directly to input. This would be a **digital** echo (perfect copy, not degraded by speaker/mic acoustics). Check: is the mic device set to a physical mic, not "Stereo Mix"?

→ **Unlikely** as the primary cause (would be user-specific), but worth documenting.

### Possibility: Server VAD too sensitive

Even without echo, if background noise triggers `semantic_vad`, the API might think the user is speaking and start a new response. But this would cause random interruptions, not "replying to itself" — so this is probably a secondary issue.

→ **Not the primary cause**, but tuning `eagerness` could help as a complementary fix.

### Possibility: API duplex design mismatch

The OpenAI Realtime API is **full-duplex** — it expects to receive mic audio even while the AI is speaking. It's designed for this. The problem isn't that we're sending audio during playback — the problem is that the audio we're sending **contains the AI's own voice** (echo).

→ This confirms it's an echo problem, not an architecture problem.

## Diagnosis: definite echo, amplified by no gating

The root cause is:
1. **Acoustic echo** — speaker output enters the mic
2. **No software mitigation** — the app does nothing to reduce or prevent this
3. **Server VAD can't fully compensate** — `semantic_vad` isn't an echo canceller

The fix needs to address **at least one** of:
- **Eliminate the echo** at the audio level (WebRTC APM — the plan in `apm-implementation-steps.md`)
- **Gate the mic** during playback (simpler but kills true full-duplex / interruption)
- **Both** for defense in depth

## Quick validation test

To confirm it's echo (not something else), try this 30-second test:

1. Start a conversation
2. **Mute your speakers** (or plug in headphones with no bleed)
3. Ask a question
4. If the AI responds normally without looping → **confirmed echo**
5. If it still loops → something else is wrong (check audio routing, server config)

## Fix options ranked by effort

| Fix | Effort | Effectiveness | Tradeoff |
|-----|--------|---------------|----------|
| **Headphones** | 0 | 100% | User must wear headphones |
| **Mic gating during playback** | Small | ~80% | Kills interruption / barge-in |
| **Reduce server VAD eagerness** | Tiny | ~30% | May miss real user speech |
| **WebRTC APM (Option B)** | Medium | ~90% | Best long-term; preserves full-duplex |
| **Platform AEC only** | Done (Android) | ~60% | Windows still unprotected |

### Quick-win: mic gating (interim before APM)

If you want an immediate partial fix while building the APM integration:

```csharp
// In VoiceInputAgent.OnAudioChunk:
private async void OnAudioChunk(object? sender, byte[] chunk)
{
    try
    {
        if (_realtime.IsConnected && !_audioOutput.IsPlaying)  // ← gate
            await _realtime.SendAudioChunkAsync(chunk);
    }
    catch (Exception) { }
}
```

This stops the echo loop but **prevents the user from interrupting the AI mid-sentence**. The WebRTC APM approach is better because it removes echo while keeping the mic hot for barge-in.

## Conclusion

**It is echo.** The AI's speaker output enters the mic, gets sent back to the API, the API hears its own voice as a new user turn, and starts responding again. The fix is WebRTC APM (removes the echo) or mic gating (prevents it from reaching the API).
