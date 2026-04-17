# M8 — GPT Model Research

**Date:** April 16, 2026
**Source:** [OpenAI Models](https://developers.openai.com/api/docs/models) | [Pricing](https://openai.com/api/pricing/)

---

## Current Model IDs in BodyCam Code

| Setting | Current Value | Correct? |
|---------|--------------|----------|
| `RealtimeModel` | `gpt-5.4-realtime` | **WRONG** — model does not exist |
| `ChatModel` | `gpt-5.4-mini` | Correct |
| `VisionModel` | `gpt-5.4` | Correct |
| `InputAudioTranscription` | `gpt-5.4-mini-transcribe` | **WRONG** — model does not exist |

> **ACTION REQUIRED:** The previous bulk rename from `gpt-4o` → `gpt-5.4` incorrectly changed the specialized model names. Realtime and transcribe models have their own naming scheme — they are NOT under the `gpt-5.4-*` family.

---

## All Available Models (April 2026)

### Frontier Models (Text + Vision)

| Model ID | Description | Input $/1M | Cached | Output $/1M | Context | Max Output | Vision | Reasoning | Cutoff |
|----------|-------------|-----------|--------|-------------|---------|------------|--------|-----------|--------|
| `gpt-5.4` | Flagship, best intelligence | $2.50 | $0.25 | $15.00 | 1.05M | 128K | Input only | none/low/med/high/xhigh | Aug 2025 |
| `gpt-5.4-mini` | Fast/cheap for coding & agents | $0.75 | $0.075 | $4.50 | 400K | 128K | Input only | none/low/med/high/xhigh | Aug 2025 |
| `gpt-5.4-nano` | Cheapest, high-volume tasks | $0.20 | $0.02 | $1.25 | 400K | 128K | Input only | none/low/med/high | Aug 2025 |

**Key features (all three):** Streaming, function calling, structured outputs, web search, file search, MCP, distillation support.

### Realtime Models (Voice — Speech-to-Speech)

| Model ID | Description | Text In/Out $/1M | Audio In/Out $/1M | Image In $/1M | Context | Max Output | Cutoff |
|----------|-------------|------------------|-------------------|--------------|---------|------------|--------|
| `gpt-realtime-1.5` | Best voice model | $4.00 / $16.00 | $32.00 / $64.00 | $5.00 | 32K | 4K | Sep 2024 |
| `gpt-realtime-mini` | Cost-efficient voice | $0.60 / $2.40 | (included) | (included) | 32K | 4K | Oct 2023 |

**Key features:** WebRTC, WebSocket, SIP connections. Function calling. Audio + text + image input. Audio + text output. No structured outputs. No fine-tuning.

### Speech-to-Text Models

| Model ID | Description | Audio In $/1M | Text Out $/1M | Streaming |
|----------|-------------|--------------|--------------|-----------|
| `gpt-4o-transcribe` | Best transcription quality | $2.50 | $5.00 | Yes |
| `gpt-4o-mini-transcribe` | Cheaper, still good quality | $1.25 | $5.00 | Yes |
| `gpt-4o-transcribe-diarize` | Speaker labels | $2.50 | $5.00 | Yes |
| `whisper-1` | Legacy open-source | $0.006/min | — | No |

> **Note:** Transcribe models are still `gpt-4o-*` branded. No `gpt-5.4-*-transcribe` models exist yet.

### Text-to-Speech

| Model ID | Description |
|----------|-------------|
| `gpt-4o-mini-tts` | Text-to-speech powered by GPT-4o mini |

---

## BodyCam Use Cases → Recommended Models

### Use Case 1: Voice Conversation (M1 — Realtime Audio Pipeline)

The core BodyCam experience: user speaks through glasses mic → AI responds through glasses speakers.

| Option | Model | Cost (audio) | Latency | Intelligence | Image Input |
|--------|-------|-------------|---------|-------------|-------------|
| **A — Premium** | `gpt-realtime-1.5` | $32/$64 per 1M audio tokens | Low | Best | Yes |
| **B — Budget** | `gpt-realtime-mini` | ~$0.60/$2.40 per 1M text tokens | Lower | Good | Yes |

**Recommendation:** Start with **`gpt-realtime-1.5`** for best voice quality and intelligence. Fall back to `gpt-realtime-mini` if cost is a concern.

**Why not use `gpt-5.4` + separate STT/TTS?** The Realtime models handle speech-to-speech natively with lower latency. Splitting into STT → text LLM → TTS adds 2-3 round trips and loses conversational naturalness.

### Use Case 2: Chat / Text Reasoning (M2 — Conversation Agent)

For any text-based reasoning that doesn't require voice (e.g., summarizing, analyzing saved transcripts).

| Option | Model | Cost | Context | Intelligence |
|--------|-------|------|---------|-------------|
| **A — Smart** | `gpt-5.4-mini` | $0.75 / $4.50 | 400K | Strong |
| **B — Cheapest** | `gpt-5.4-nano` | $0.20 / $1.25 | 400K | Adequate |
| **C — Max intelligence** | `gpt-5.4` | $2.50 / $15.00 | 1.05M | Best |

**Recommendation:** **`gpt-5.4-mini`** is the sweet spot. Use `gpt-5.4-nano` for sub-agent tasks (classification, extraction). Reserve `gpt-5.4` for complex multi-step reasoning only.

### Use Case 3: Vision / Image Understanding (M3 — Vision Agent)

Sending camera frames from glasses to AI for scene description.

| Option | Model | How | Cost |
|--------|-------|-----|------|
| **A — Via Realtime** | `gpt-realtime-1.5` | Send `input_image` in Realtime session | $5.00 / 1M image tokens |
| **B — Standalone** | `gpt-5.4-mini` | Vision input via Chat Completions | $0.75 / 1M text input |
| **C — Max quality** | `gpt-5.4` | Vision input via Chat Completions | $2.50 / 1M text input |

**Recommendation:** **Option A** if already in a voice session (no extra connection). **Option B** for background analysis without voice.

### Use Case 4: Input Audio Transcription (inside Realtime session)

The Realtime API needs a transcription model for `input_audio_transcription` to get text transcripts of what the user said.

| Option | Model | Cost |
|--------|-------|------|
| **A — Cheaper** | `gpt-4o-mini-transcribe` | $1.25 / 1M audio tokens |
| **B — Best** | `gpt-4o-transcribe` | $2.50 / 1M audio tokens |

**Recommendation:** **`gpt-4o-mini-transcribe`** — sufficient for conversational transcription, half the price.

---

## Proposed Default Configuration

```csharp
// Frontier (text + vision)
public string ChatModel { get; set; } = "gpt-5.4-mini";        // ✅ correct
public string VisionModel { get; set; } = "gpt-5.4";           // ✅ correct

// Realtime (voice)
public string RealtimeModel { get; set; } = "gpt-realtime-1.5"; // ❌ NEEDS FIX (was "gpt-5.4-realtime")

// Transcription (inside Realtime session)
// InputAudioTranscription model = "gpt-4o-mini-transcribe"      // ❌ NEEDS FIX (was "gpt-5.4-mini-transcribe")
```

---

## Cost Estimate (1 hour casual use)

Assumptions: ~30 min active voice, ~100 camera frames, ~5 text queries.

| Component | Model | Estimated Tokens | Cost |
|-----------|-------|-----------------|------|
| Voice (audio in) | `gpt-realtime-1.5` | ~1.8M audio tokens | ~$57.60 |
| Voice (audio out) | `gpt-realtime-1.5` | ~0.9M audio tokens | ~$57.60 |
| Vision frames | `gpt-realtime-1.5` (image input) | ~50K tokens | ~$0.25 |
| Text queries | `gpt-5.4-mini` | ~10K tokens | ~$0.05 |
| Transcription | `gpt-4o-mini-transcribe` | ~900K tokens | ~$1.13 |
| **Total** | | | **~$116.63** |

With `gpt-realtime-mini` instead:

| Component | Model | Estimated Tokens | Cost |
|-----------|-------|-----------------|------|
| Voice (audio in) | `gpt-realtime-mini` | ~1.8M | ~$1.08 |
| Voice (audio out) | `gpt-realtime-mini` | ~0.9M | ~$2.16 |
| Vision + Text + Transcription | same | same | ~$1.43 |
| **Total** | | | **~$4.67** |

> `gpt-realtime-mini` is **~25x cheaper** for voice. Consider making it the default for casual use and offering `gpt-realtime-1.5` as a "premium" toggle.

---

## Recommended Immediate Actions

1. **Fix `RealtimeModel`** → change from `"gpt-5.4-realtime"` to `"gpt-realtime-1.5"` (or `"gpt-realtime-mini"`)
2. **Fix `InputAudioTranscription`** → change from `"gpt-5.4-mini-transcribe"` to `"gpt-4o-mini-transcribe"`
3. **Consider adding `gpt-5.4-nano`** as an option for M2 sub-agent / classification tasks
4. **Add model selection to M6 Settings UI** — let user pick realtime model tier
5. **Update `.env.example`** Azure deployment name to match real model IDs

---

## Model Snapshot Versions (for pinning)

| Model | Latest Snapshot |
|-------|----------------|
| `gpt-5.4` | `gpt-5.4-2026-03-05` |
| `gpt-5.4-mini` | `gpt-5.4-mini-2026-03-17` |
| `gpt-5.4-nano` | `gpt-5.4-nano-2026-03-17` |
| `gpt-realtime-1.5` | `gpt-realtime-1.5` (no dated snapshot yet) |
| `gpt-realtime-mini` | `gpt-realtime-mini-2025-12-15` |
| `gpt-4o-mini-transcribe` | `gpt-4o-mini-transcribe-2025-12-15` |
| `gpt-4o-transcribe` | (check latest) |
