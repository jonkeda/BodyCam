# Phase 7 - Grok Voice Android Pass/Fail Matrix

This matrix is for the Android-only Grok voice verification that cannot be
completed from the Windows test runner.

## Preconditions

- Active provider is Grok.
- xAI API key is configured on the device.
- `BODYCAM_GROK_LIVE_TESTS=1` is set only when intentionally running live
  probes.
- Realtime session uses `wss://api.x.ai/v1/realtime` with an ephemeral client
  secret broker.
- Batch STT and TTS are available through the provider layer before realtime
  session testing starts.

## Matrix

| Route | Test | Pass Criteria | Fail Notes | Status |
| --- | --- | --- | --- | --- |
| Phone speaker + phone mic | Start Grok voice session, ask a short question, play response aloud. | Session opens, mic captures speech, response plays through phone speaker, no crash. | Record auth, route, AEC, or timeout error category. | Not run |
| Bluetooth headset | Connect headset, start Grok voice session, ask a short question. | Input and output both use headset route; response is intelligible. | Record whether Android routed only input or only output. | Not run |
| HeyCyan glasses route | Connect HeyCyan, start session, ask a short question. | Input and output use HeyCyan-aware route policy, or fail with clear unsupported route message. | Record SDK connection, route, codec, or AEC failure. | Not run |
| Batch STT | Record a short clip and call Grok STT. | `/v1/stt` returns readable text and language metadata when available. | Record media type, auth, timeout, or rate-limit issue. | Not run |
| Batch TTS | Synthesize one short sentence and play it over the selected route. | `/v1/tts` returns audio bytes and playback uses selected route. | Record codec, playback, or provider error. | Not run |
| Realtime ephemeral token | Request client secret, open websocket. | Token is received server-side and never stored as user API key; websocket connects. | Record broker failure or websocket close code. | Not run |
| Barge-in | Interrupt assistant playback with speech. | Assistant stops or lowers playback according to app policy and captures the new turn. | Record whether issue is route policy, AEC, or provider turn detection. | Not run |
| No-key failure | Clear Grok key and run connection test. | User sees missing-key diagnostics; no network call is attempted. | Should be fixed before any live call testing. | Unit-passed |

## Recording Results

For each Android run, record:

- device model and Android version;
- audio route;
- provider model ids;
- whether network was Wi-Fi or mobile;
- pass/fail status;
- observed latency;
- telemetry event names and error category;
- short notes for reproduction.
