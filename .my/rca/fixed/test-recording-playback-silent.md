# RCA: Test Recording Playback Silent

**Date:** 2025-05-17
**Status:** Fixed
**Severity:** Medium — test-only bug, does not affect live audio pipeline

---

## Symptom

"Test Recording" in Device settings records 48 chunks from the glasses mic
(success) but playback is inaudible. "Test Sound" (sine wave) plays fine
through the same speaker.

---

## Root Cause

**Sample rate mismatch.** The capture provider resamples audio to
`_settings.SampleRate` (48000 Hz), but `TestRecordingAsync` started playback
at a hardcoded 16000 Hz:

```csharp
await output.StartAsync(16000);  // ← 16 kHz
```

The WASAPI output interpreted the 48 kHz PCM data as 16 kHz — playing at
1/3 speed and 1/3 pitch. A 3-second recording became ~9 seconds of
sub-audible rumble.

Additionally, the post-playback delay was only 500ms, cutting off even the
distorted audio before it could play.

---

## Fix

1. Use `_settings.SampleRate` (48000) for playback to match the capture
   pipeline's output rate.
2. Increased drain delay from 500ms to 3500ms to match the 3-second
   recording duration.
