# Phase 4 - Grok Voice, STT, And TTS

## Goal

Prove Grok can cover BodyCam voice use cases.

## Two Paths

### Path A - Grok Realtime Voice

Use `wss://api.x.ai/v1/realtime` for speech-to-speech conversations with tool
use.

Work:

- Add a `GrokRealtimeVoiceProvider`.
- Map realtime session setup, audio input, audio output, interruption,
  transcript events, tool calls, and errors into BodyCam's existing
  orchestrator expectations.
- Use ephemeral client secrets for mobile/browser-style realtime auth when a
  token broker is configured.
- Verify sample-rate handling with the existing 48 kHz internal audio pipeline
  and 24 kHz API boundary.

### Path B - Composite Voice

Use xAI STT, text/vision, and TTS as separate services:

```text
microphone -> STT -> Grok text/tool/vision -> TTS -> speaker
```

Work:

- Add `GrokSpeechToTextProvider`.
- Add `GrokTextToSpeechProvider`.
- Add a composite voice session for providers that do not fit the realtime
  orchestrator.
- Support both REST and streaming STT/TTS where useful.

## Acceptance

- The user can speak a request and hear a Grok answer.
- Grok can call Look/Read/Scan tools during a voice session.
- The transcript includes input and output text.
- Barge-in/interruption behavior is either supported or clearly degraded with
  a provider capability flag.
- Direct speaker/headset echo policy from M43 continues to apply.

## Decision Gate

If Grok realtime voice maps cleanly, prefer Path A for live assistant mode. If
not, keep Path B as the POC path and document the latency tradeoff.
