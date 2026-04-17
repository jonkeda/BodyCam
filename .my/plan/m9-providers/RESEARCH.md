# M9 — Multi-Provider Support: Provider Analysis

## Executive Summary

This analysis evaluates six major LLM providers for integration into BodyCam's real-time voice pipeline. The key constraint is **voice quality parity with OpenAI Realtime** — providers must support native speech-to-speech (audio-in → audio-out) with low latency, not just text-based LLM access piped through separate STT/TTS services.

**Verdict:** Only **OpenAI** (already implemented) and **Google Gemini Live** offer production-grade, native real-time voice APIs over WebSocket with comparable quality. **Anthropic** has nascent voice support. **xAI Grok**, **Meta**, and **Microsoft** don't offer independent real-time voice APIs (Grok has consumer voice mode but no developer API; Meta is open-source/self-hosted; Microsoft wraps OpenAI via Azure).

---

## Provider Comparison Matrix

| Capability | OpenAI Realtime | Google Gemini Live | Anthropic Claude | xAI Grok | Meta Llama | Microsoft (Azure) |
|---|---|---|---|---|---|---|
| **Native speech-to-speech** | ✅ Yes | ✅ Yes | ⚠️ Limited/New | ❌ No API | ❌ No | ✅ Via Azure OpenAI |
| **Protocol** | WebSocket / WebRTC | WebSocket | Unknown/New | N/A | N/A (self-host) | WebSocket / WebRTC |
| **Input audio format** | PCM16, 24kHz | PCM16, 16kHz (resamples any) | TBD | N/A | N/A | PCM16, 24kHz |
| **Output audio format** | PCM16, 24kHz | PCM16, 24kHz | TBD | N/A | N/A | PCM16, 24kHz |
| **Server-side VAD** | ✅ server_vad, semantic_vad | ✅ Automatic VAD (configurable) | ❓ Unknown | N/A | N/A | ✅ Same as OpenAI |
| **Barge-in / interruption** | ✅ Yes | ✅ Yes | ❓ Unknown | N/A | N/A | ✅ Same as OpenAI |
| **Function calling** | ✅ Yes | ✅ Yes | ❓ Unknown in voice | ✅ Text API | N/A | ✅ Same as OpenAI |
| **Input transcription** | ✅ Async (whisper/gpt-4o) | ✅ Yes | ❓ | N/A | N/A | ✅ Same as OpenAI |
| **Output transcription** | ✅ Streaming deltas | ✅ Yes | ❓ | N/A | N/A | ✅ Same as OpenAI |
| **Vision (image frames)** | ❌ Not in Realtime | ✅ JPEG ≤1 FPS | ❓ | ✅ Text API | N/A | ❌ Not in Realtime |
| **Multiple voices** | ✅ 10+ voices | ✅ Multiple (Kore, etc.) | ❓ | ❌ N/A | N/A | ✅ 8 voices |
| **Thinking/reasoning** | ❌ Not in Realtime | ✅ ThinkingLevel | ❓ | ✅ Think mode | N/A | ❌ |
| **Auth mechanism** | Bearer token | API key | API key | Bearer token | N/A | api-key header |
| **Latency** | ~300-500ms | ~300-600ms (estimated) | Unknown | N/A (text only) | Varies | ~300-500ms |
| **.NET SDK** | ✅ OpenAI NuGet | ⚠️ No official .NET SDK | ⚠️ No official .NET SDK | ⚠️ OpenAI-compat | N/A | ✅ Azure.AI.OpenAI |
| **Integration effort** | ✅ Done | 🟡 Medium (raw WebSocket) | 🔴 High (immature API) | 🔴 No voice API | 🔴 Very High | ✅ Done (same as OpenAI) |

---

## Detailed Provider Analysis

### 1. OpenAI Realtime API ✅ (Already Implemented)

**Status:** Production, GA (graduated from beta)

**Architecture:**
- WebSocket connection to `wss://api.openai.com/v1/realtime?model={model}`
- Session-based: `session.update` configures modalities, voice, tools, VAD
- Audio: `input_audio_buffer.append` (base64 PCM16) → server processes → `response.audio.delta` (base64 PCM16)
- Function calling: `response.done` contains function_call items → client executes → `conversation.item.create` with output
- Interruption: `input_audio_buffer.speech_started` → client truncates playback

**Models:** gpt-realtime-1.5 (latest), previous gpt-4o-realtime-preview

**Voice quality:** Industry-leading. Native audio token processing — the model "thinks" in audio, not text→TTS.

**Current implementation in BodyCam:**
- `RealtimeClient.cs`: Full WebSocket client with receive loop
- Provider routing: OpenAI direct vs Azure via `AppSettings.Provider` enum
- Event-driven: 11 events mapped to agent orchestration

**Verdict:** Reference implementation. All other providers measured against this.

---

### 2. Google Gemini Live API ✅ Strong Candidate

**Status:** Preview (Gemini 3.1 Flash Live Preview, Gemini 2.5 Flash Native Audio Preview)

**Architecture:**
- WebSocket connection (stateful WSS)
- Input: PCM16 audio (16kHz native, resamples any rate), JPEG images (≤1 FPS), text
- Output: PCM16 audio (24kHz)
- Session config sent on connect: `response_modalities`, `speech_config`, `realtime_input_config`
- Audio sent via `send_realtime_input` with `audio` blob
- Responses received as streaming server events with `inline_data` (audio) and transcription
- Function calling: Supported, including async (NON_BLOCKING) in 2.5 models

**Key differentiators vs OpenAI:**
- **Vision IN realtime session** — can send JPEG frames directly in the voice session (OpenAI Realtime does NOT support vision; BodyCam uses a separate VisionAgent + ChatCompletions for this)
- **Thinking/reasoning** — configurable thinking depth (minimal/low/medium/high)
- **Affective dialog** — adapts tone to user's emotional expression (2.5 Flash only)
- **Proactive audio** — model can choose not to respond if input isn't relevant (2.5 Flash only)
- **97 languages** supported with natural switching

**Limitations:**
- Session duration: 15 min audio-only, 2 min with video
- No official .NET SDK — would need raw WebSocket implementation
- Preview status — API surface may change

**Event mapping to BodyCam's IRealtimeClient:**

| BodyCam Event | Gemini Equivalent |
|---|---|
| `AudioDelta` | `server_content.model_turn.parts[].inline_data` |
| `OutputTranscriptDelta` | `server_content.output_transcription.text` |
| `InputTranscriptCompleted` | `server_content.input_transcription.text` |
| `SpeechStarted` | VAD activity_start (automatic or manual) |
| `SpeechStopped` | VAD activity_end |
| `ResponseDone` | `server_content.turn_complete` |
| `FunctionCallReceived` | `tool_call` in server content |
| `ErrorOccurred` | Error messages in stream |

**Audio format compatibility:**
- Input: 16kHz PCM16 (BodyCam captures at 24kHz — would need resample OR send at 24kHz and let Gemini resample)
- Output: 24kHz PCM16 ✅ Same as BodyCam's playback pipeline

**Integration effort:** Medium. Raw WebSocket, different JSON protocol, but same fundamental pattern (connect → send audio → receive audio + events). No .NET SDK means manual JSON serialization.

**Verdict:** Best alternative to OpenAI. Vision-in-session is a major advantage for BodyCam's use case (smart glasses). Worth implementing.

---

### 3. Anthropic Claude Voice ⚠️ Emerging

**Status:** Early/Limited availability

**What we know:**
- Anthropic has announced Claude voice capabilities in consumer products (Claude app)
- API-level real-time voice streaming documentation was not accessible during research
- The `docs.anthropic.com/en/docs/build-with-claude/voice` paths exist but returned no content, suggesting either:
  - Gated/preview access only
  - Very new and incomplete documentation
  - Regional restrictions

**Assessment:**
- Claude's text model quality is excellent (on par with GPT-5.4)
- Voice API maturity is significantly behind OpenAI and Google
- No clear WebSocket-based streaming protocol documented
- No .NET SDK for voice features
- May use a STT → Claude text → TTS pipeline rather than native audio tokens

**Integration effort:** High. Would need to monitor API availability, likely implement a composite pipeline (Whisper STT → Claude API → TTS service) rather than native speech-to-speech.

**Verdict:** Not ready for integration now. Monitor for API maturity. Could be added later as a "text-mode" provider using STT→LLM→TTS pipeline if user wants Claude's reasoning quality.

---

### 4. xAI Grok ❌ No Real-Time Voice API

**Status:** Text API only (OpenAI-compatible chat completions). Consumer voice mode exists in Grok app but no developer API.

**What xAI offers:**
- **Chat Completions API** (OpenAI-compatible) — launched April 2025 for Grok 3, pricing $3/M input, $15/M output tokens
- **Models:** Grok 4.20 Beta (latest, Feb 2026), Grok 4.1/4.1 Fast, Grok 4, Grok 3
- **Vision:** Supported in text API (image understanding since Oct 2024)
- **Function calling:** Supported in text API
- **Thinking/reasoning:** "Think" mode available
- **Agent Tools API:** Grok 4.1 Fast supports tool orchestration (search, web, code execution)
- **gRPC APIs:** xai-proto repo has protobuf definitions, but no real-time audio protocol
- **SDKs:** Python SDK (xai-sdk-python). No .NET SDK but OpenAI-compatible endpoint works with OpenAI .NET SDK

**What xAI does NOT offer:**
- No WebSocket-based real-time voice/audio streaming API
- No native speech-to-speech capabilities via API
- No audio input/output in the API — text only
- Consumer voice mode in Grok app/X is not exposed as a developer API
- Also available on Azure (May 2025) but only text completions

**OpenAI Compatibility:**
- Grok's API is OpenAI-compatible (`https://api.x.ai/v1/chat/completions`)
- Works with the OpenAI .NET SDK by changing the base URL
- This means Grok could be used as a **chat/vision provider** (replacing `IChatClient` for ConversationAgent/VisionAgent) without any code changes beyond configuration
- However, this does NOT help with the real-time voice pipeline

**Integration assessment:**
- **As a realtime voice provider:** Not possible — no API exists
- **As a chat/vision provider (non-realtime):** Trivially easy via OpenAI compatibility. Could swap in Grok for the `ConversationAgent` or `VisionAgent` text completions
- **As a composite voice provider (STT→Grok→TTS):** Same as any text-only LLM — high latency, requires orchestrating 3 services

**Verdict:** Cannot replace OpenAI Realtime for the voice pipeline. Could be offered as an alternative chat/vision model via OpenAI-compatible API (trivial config change, not an M9 concern). Monitor for future real-time voice API announcement.

---

### 5. Meta Llama ❌ Not Applicable (Cloud API)

**Status:** Open-source models, no hosted real-time voice API

**What Meta offers:**
- Open-source LLM models (Llama 3, Llama 4, etc.)
- Text-only models — no native audio understanding or generation
- Self-hosted deployment (requires GPU infrastructure)
- Third-party hosting (Together AI, Fireworks, Groq, etc.) — text API only

**For real-time voice, you'd need:**
1. Self-hosted STT service (Whisper, etc.)
2. Self-hosted Llama inference (GPU server)
3. Self-hosted TTS service (Coqui, StyleTTS2, etc.)
4. Custom WebSocket orchestration layer
5. Latency management across 3 separate services

**Integration effort:** Very high. Essentially building a complete real-time voice platform from scratch. Latency would be significantly worse than OpenAI/Google native solutions.

**Verdict:** Not viable as a direct provider. Could theoretically be supported via the "composite pipeline" pattern (STT→LLM→TTS) if someone self-hosts, but this is a very different use case. Exclude from M9 scope. Could revisit if Meta releases a hosted Realtime API.

---

### 6. Microsoft Azure ✅ (Already Implemented via Azure OpenAI)

**Status:** Production, same API as OpenAI

**What Microsoft offers:**
- **Azure OpenAI Service** — hosts OpenAI models (including Realtime) with Azure infrastructure
- **Azure AI Speech** — standalone STT/TTS services (not real-time conversational)
- **Azure AI Voice Live API** — mentioned in docs, appears to be the Azure-hosted version of OpenAI Realtime

**Current BodyCam implementation:**
- Already supports Azure OpenAI as a provider via `OpenAiProvider.Azure` enum
- Same WebSocket protocol, different auth (api-key header vs Bearer token)
- Different endpoint URL pattern (`wss://{endpoint}/openai/realtime?api-version=...&deployment=...`)

**No independent Microsoft voice model** — Azure OpenAI hosts OpenAI's models, not Microsoft's own voice-native model.

**Verdict:** Already implemented. No additional work needed. Microsoft's value-add is enterprise features (VNet, RBAC, compliance), not a different AI voice model.

---

## Recommendation & Priority

### Tier 1: Implement Now
1. **Google Gemini Live** — Best ROI. Production-grade voice API, complementary features (vision-in-session), competitive quality. Medium integration effort.

### Tier 2: Implement When Ready
2. **Anthropic Claude** — Excellent model quality but voice API immature. Design the architecture to accommodate it when the API stabilizes. Could support a "composite mode" (STT→Claude→TTS) as interim.

### Tier 3: Exclude from M9
3. **xAI Grok** — No real-time voice API. Consumer voice exists but not exposed to developers. Text API is OpenAI-compatible so could be used for chat/vision trivially, but not for the real-time voice pipeline.
4. **Meta Llama** — No hosted real-time voice API. Self-hosting complexity too high for this milestone.
5. **Microsoft Azure** — Already implemented (it's just Azure-hosted OpenAI).

### Architecture Implication
The provider abstraction must support two patterns:
1. **Native real-time** (OpenAI, Gemini, future Anthropic) — single WebSocket, audio-in → audio-out
2. **Composite pipeline** (future: any text LLM + STT + TTS) — three services orchestrated to approximate real-time

M9 should implement pattern 1 with Gemini as the second provider, and design the abstraction to accommodate pattern 2 later.

---

## Audio Format Compatibility

| Provider | Input Sample Rate | Output Sample Rate | Format | Notes |
|---|---|---|---|---|
| OpenAI | 24kHz | 24kHz | PCM16 LE | Base64-encoded in JSON |
| Gemini | 16kHz (native), accepts any | 24kHz | PCM16 LE | Raw bytes in protobuf/JSON blob |
| Azure OpenAI | 24kHz | 24kHz | PCM16 LE | Same as OpenAI |

BodyCam currently captures at 24kHz and plays at 24kHz. Gemini prefers 16kHz input — either:
- Resample 24kHz → 16kHz before sending (simple linear interpolation)
- Send 24kHz and rely on Gemini's server-side resampling

---

## Cost Comparison (Estimated)

| Provider | Input Audio | Output Audio | Text (equivalent) |
|---|---|---|---|
| OpenAI Realtime | ~$0.06/min | ~$0.24/min | Included in audio pricing |
| Google Gemini Live | Free tier available, then usage-based | Usage-based | Included |
| Azure OpenAI | Same as OpenAI + Azure markup | Same as OpenAI + Azure markup | Same |

Google's pricing is generally lower than OpenAI's, especially with the free tier for development.
