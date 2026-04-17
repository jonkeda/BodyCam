# M9 — Azure AI Foundry & Local Model Analysis

## Scope

Which models available on **Azure AI Foundry** and which **local (on-device) models** could serve BodyCam's needs across:
1. Real-time voice pipeline (speech-in → speech-out)
2. Text/vision (chat completions, image understanding)

Hardware assumption for local: **consumer GPU (8–12 GB VRAM)**.

---

## Part 1 — Azure AI Foundry Models

Azure AI Foundry (formerly Azure AI Studio) hosts models from Microsoft, OpenAI, Anthropic, xAI, Meta, Mistral, DeepSeek, Cohere, and others. Over 9,400 models in the catalog.

### 1A. Real-Time Voice (Speech-to-Speech)

Only OpenAI's realtime models are available on Azure for native speech-to-speech. No third-party model in the Azure catalog offers a real-time audio API.

| Model | Type | Status | Input | Output | Notes |
|---|---|---|---|---|---|
| **gpt-realtime-1.5** | Realtime audio | GA | PCM16 24kHz, 32K tokens | PCM16 24kHz, 4K tokens | Latest. BodyCam currently uses this. |
| **gpt-realtime** | Realtime audio | GA | PCM16 24kHz, 32K tokens | PCM16 24kHz, 4K tokens | Previous gen, still available. |
| **gpt-realtime-mini** | Realtime audio | GA | PCM16 24kHz, 128K tokens | PCM16 24kHz, 4K tokens | Cheaper, smaller. |
| gpt-4o-realtime-preview | Realtime audio | Preview | PCM16 24kHz, 16–32K tokens | PCM16 24kHz, 4K tokens | Legacy preview. |
| gpt-4o-mini-realtime-preview | Realtime audio | Preview | PCM16 24kHz, 128K tokens | PCM16 24kHz, 4K tokens | Legacy preview. |

**Protocol:** WebSocket (`/openai/v1/realtime`), WebRTC, or SIP. Same event protocol as direct OpenAI.

**Key features:** Semantic VAD, server VAD, function calling, image input, MCP server support, barge-in/interruption, session max 30 min.

**Regions with full availability:** East US2, Sweden Central (most models). Many regions support Global Standard deployment.

**Verdict:** BodyCam already uses `gpt-realtime-1.5` via Azure. These are the only realtime voice models available on Azure. No Gemini Live, no Anthropic voice, no Grok voice on Azure.

---

### 1B. Audio Pipeline Models (STT + TTS, Non-Realtime)

Useful for the **composite pipeline** pattern (STT → LLM → TTS) or standalone transcription/speech.

#### Speech-to-Text (STT)

| Model | Quality | Max Input | Notes |
|---|---|---|---|
| **MAI-Transcribe-1** | Enterprise-grade | TBD | Microsoft's own STT model. New (Jan 2026). |
| **gpt-4o-transcribe** | Excellent | 25 MB | GPT-4o powered. High accuracy. |
| **gpt-4o-mini-transcribe** | Very good | 25 MB | Cheaper, fast. BodyCam uses this for transcription. |
| gpt-4o-transcribe-diarize | Excellent | 25 MB | Adds speaker diarization. |
| whisper | Good | 25 MB | Original OpenAI Whisper. Cheapest. |

#### Text-to-Speech (TTS)

| Model | Quality | Notes |
|---|---|---|
| **MAI-Voice-1** | High fidelity | Microsoft's own TTS. Enterprise-grade. New (Dec 2025). |
| **gpt-4o-mini-tts** | Excellent | Style-guided voice. Can instruct tone/emotion. |
| tts-hd | Good | Optimized for quality. |
| tts | Good | Optimized for speed. |

#### Audio Generation (Non-Realtime, Full Conversation)

| Model | Notes |
|---|---|
| **gpt-audio-1.5** | Audio completions (not realtime WebSocket). Text+audio in/out via REST. |
| gpt-audio / gpt-audio-mini | Previous gen audio completions. |

**Composite pipeline relevance:** Could build STT → any-LLM → TTS on Azure using `gpt-4o-mini-transcribe` + any chat model + `gpt-4o-mini-tts`. Latency would be higher (~1–2s) but opens up **any** Azure-hosted LLM for voice.

---

### 1C. Chat / Vision Models (Text Pipeline)

These can power `ConversationAgent`, `VisionAgent`, and `DeepAnalysisTool`. All use Chat Completions or Responses API.

#### OpenAI (via Azure OpenAI)

| Model | Tier | Context | Reasoning | Vision | Notes |
|---|---|---|---|---|---|
| **gpt-5.4** | Frontier | 1M | Yes | Yes | Most capable. Registration required. |
| **gpt-5.4-mini** | Mid | 400K | Yes | Yes | Best value for most tasks. |
| **gpt-5.4-nano** | Small | 400K | Yes | Yes | Ultra-low latency. |
| gpt-5.4-pro | Frontier+ | 1M | Yes | Yes | Extended reasoning. |
| gpt-5.2 / gpt-5.1 / gpt-5 | Prev gen | 400K | Yes | Yes | Still available, cheaper. |
| gpt-4.1 / gpt-4.1-mini / gpt-4.1-nano | Older | 1M | No | Yes | Legacy, widely available. |
| gpt-4o / gpt-4o-mini | Legacy | 128K | No | Yes | Being superseded. |

#### OpenAI Reasoning (o-series)

| Model | Notes |
|---|---|
| **o4-mini** | Fast reasoning. Good for complex analysis. |
| **o3** | Strong reasoning. |
| codex-mini | Fine-tuned o4-mini for code. |
| o3-pro | Extended compute reasoning. |

#### Anthropic (via Azure)

| Model | Notes |
|---|---|
| **Claude Opus 4.7** | Latest flagship. Coding/enterprise. |
| **Claude Sonnet 4.6** | Fast + capable. Good value. |
| Claude Opus 4.6 / 4.5 / 4.1 | Previous generations. |
| Claude Haiku 4.5 | Fastest, cheapest Claude. |

#### xAI (via Azure)

| Model | Notes |
|---|---|
| **Grok 4.20** (reasoning + non-reasoning) | Latest Grok. |
| Grok 4.1 Fast (reasoning + non-reasoning) | Optimized for tool calling. |
| Grok 4 | Previous gen. |
| Grok 3 / 3-mini | Older. |
| **Grok Code Fast 1** | Agentic coding, very cheap. |

#### Others (via Azure)

| Provider | Models | Notes |
|---|---|---|
| **DeepSeek** | V3.2, V3.2-Speciale, V3.1, R1 | Strong reasoning, competitive pricing. |
| **Meta** | Llama-4-Maverick-17B, Llama-3.3-70B | Open weights hosted on Azure. |
| **Mistral** | Large-3, document-ai-2512 | Strong European models. |
| **Moonshot AI** | Kimi-K2.5, Kimi-K2-Thinking | Reasoning models. |
| **Cohere** | command-a, embed-v-4-0, rerank-v4.0 | Embeddings + reranking. |
| **Microsoft** | gpt-oss-120b, gpt-oss-20b | Open-weight reasoning (Foundry Local compatible). |
| **Qwen** | qwen3.5-9b, qwen3.5-35b-a3b | HuggingFace-hosted on Azure. |
| **Fireworks** | FW-MiniMax-M2.5, FW-GLM-5, FW-Kimi-K2.5 | Fireworks-hosted variants. |

---

### 1D. Azure Model Summary for BodyCam

| BodyCam Component | Current Model | Azure Alternatives | Recommendation |
|---|---|---|---|
| **Realtime voice** | gpt-realtime-1.5 | gpt-realtime, gpt-realtime-mini | Stick with gpt-realtime-1.5. Only option. |
| **Chat (ConversationAgent)** | gpt-5.4-mini | Claude Sonnet 4.6, Grok 4.20, DeepSeek V3.2, gpt-5.4-nano | Any could drop-in via OpenAI-compat API. |
| **Vision (VisionAgent)** | gpt-5.4 | Claude Opus 4.7, gpt-5.4-mini | Claude strong alternative. |
| **Deep analysis** | gpt-5.4 | o4-mini, o3, Claude Opus 4.7 | Reasoning models excellent here. |
| **Transcription** | gpt-4o-mini-transcribe | MAI-Transcribe-1, gpt-4o-transcribe | Current choice is fine. |
| **Composite voice** | N/A | gpt-4o-mini-transcribe + any LLM + gpt-4o-mini-tts | Opens voice to any Azure LLM. |

---

## Part 2 — Local Models

Local models run on the user's hardware with no cloud dependency. Target: consumer GPU (8–12 GB VRAM, e.g., RTX 3070/4070).

### 2A. Native Speech-to-Speech (Local Realtime)

#### Moshi (Kyutai) ⭐ Best Local Option

| Aspect | Details |
|---|---|
| **What** | Full-duplex speech-to-speech foundation model. Audio-in → audio-out, native. |
| **Architecture** | 7B param Temporal Transformer + Depth Transformer. Uses Mimi neural codec (24kHz, 12.5 Hz token rate). |
| **Latency** | ~200ms on L4 GPU. Theoretical 160ms (80ms frame + 80ms acoustic delay). |
| **VRAM** | ~24 GB (bf16), ~12 GB (int8), ~6 GB (int4 via MLX on Mac). |
| **Voices** | Moshiko (male), Moshika (female). |
| **Function calling** | ❌ Not supported. Text inner monologue only. |
| **License** | Code: MIT/Apache-2.0. Weights: CC-BY 4.0. |
| **Backends** | PyTorch (GPU), Rust/Candle (GPU), MLX (Apple Silicon). |
| **Stars** | 10K+ on GitHub. Active development. |
| **.NET integration** | None. Would need to run as a sidecar server (Python/Rust) and communicate via WebSocket. |

**Feasibility on 8–12 GB VRAM:** Marginal. The int8 quantized model (~12 GB) could fit on a 12 GB GPU but leaves no room for other tasks. The int4 MLX variant is Mac-only. Not practical on an 8 GB GPU.

**Quality assessment:** Good for a local model but noticeably below OpenAI Realtime in:
- Voice naturalness and expressiveness
- Instruction following
- Multi-turn coherence
- No function calling means no tool integration

**Verdict:** Most promising local speech-to-speech model. Requires 12+ GB VRAM or Apple Silicon. Would run as a sidecar process, not embedded in the MAUI app. No function calling is a significant limitation for BodyCam's tool-based architecture.

---

### 2B. Local Composite Pipeline (STT → LLM → TTS)

The practical alternative: three separate local models composed into a voice pipeline.

#### STT: Whisper.net ⭐ Production-Ready

| Aspect | Details |
|---|---|
| **What** | .NET bindings for whisper.cpp. Local speech recognition. |
| **NuGet** | `Whisper.net` v1.9.0 (278K downloads). |
| **Platforms** | Windows, Linux, macOS, Android, iOS, MAUI, WebAssembly. |
| **GPU accel** | CUDA (12+13), CoreML, OpenVINO, Vulkan. |
| **Models** | tiny (75MB/~1GB VRAM), base (142MB), small (466MB), medium (1.5GB), large-v3 (3GB). |
| **Quality** | Excellent with medium/large models. Good with small. Acceptable with base. |
| **Latency** | ~0.5–1.5s for a sentence (GPU-accelerated). |
| **License** | MIT. |

**Feasibility on 8–12 GB VRAM:** Excellent. Even `whisper-medium` uses ~2 GB VRAM, leaving room for other models. `whisper-small` is very practical.

**MAUI integration:** First-class. NuGet package works on all MAUI platforms including Android.

**Verdict:** Drop-in STT for .NET. Already has MAUI support. Could replace or supplement the cloud-based transcription. Best-in-class local STT.

#### LLM: Ollama + Small Models

| Model | Params | VRAM (Q4) | Quality | Function Calling |
|---|---|---|---|---|
| **Phi-4-mini** | 3.8B | ~3 GB | Good for size | ✅ Yes |
| **Phi-4** | 14B | ~9 GB | Very good | ✅ Yes |
| **Qwen 3.5-9B** | 9B (3B active MoE) | ~4 GB | Excellent for size | ✅ Yes |
| **Llama-3.3-8B** | 8B | ~5 GB | Good | ✅ Yes |
| **Gemma 3-4B** | 4B | ~3 GB | Good | ⚠️ Limited |
| **Mistral-7B** | 7B | ~5 GB | Good | ✅ Yes |
| DeepSeek-R1-8B (distill) | 8B | ~5 GB | Good reasoning | ⚠️ Limited |

**Integration approach:** Run Ollama locally, call via OpenAI-compatible REST API. The existing `IChatClient` factory in BodyCam could be pointed at `http://localhost:11434/v1/` with minimal code changes.

**Feasibility on 8–12 GB VRAM:** Good. Phi-4-mini or Qwen 3.5-9B + Whisper-small leaves room for TTS. Phi-4 (14B) would consume most of a 12 GB GPU.

**Verdict:** Practical via Ollama. Function calling support varies. Quality is usable but noticeably below GPT-5.4-mini/Claude.

#### TTS: Piper / Kokoro / Edge-TTS

| Engine | Quality | Latency | VRAM | .NET Integration | License |
|---|---|---|---|---|---|
| **Piper** (OHF-Voice/piper1-gpl) | Good (VITS-based) | ~50–200ms | CPU only (ONNX) | Process call | GPL (was MIT) |
| **Kokoro** | Very good | ~100–300ms | ~1–2 GB GPU | Process call | Apache-2.0 |
| **Edge-TTS** | Excellent (cloud) | ~200–500ms | 0 (cloud) | HTTP/WebSocket | Free (Microsoft) |
| **Coqui XTTS** | Very good | ~500ms–1s | ~2–4 GB GPU | Process call | CPML (restrictive) |
| **gpt-4o-mini-tts** (Azure) | Excellent | ~200ms | 0 (cloud) | REST API | Pay-per-use |

**Note on Piper:** The original repo (rhasspy/piper) was **archived Oct 2025**. Development moved to `OHF-Voice/piper1-gpl` under GPL license. Still the fastest CPU-only TTS option.

**Verdict:** Piper is fastest for CPU-only. Kokoro is best quality for GPU. Edge-TTS is a good hybrid (free cloud, excellent quality). None have native .NET bindings — all need process interop or HTTP.

---

### 2C. Local Composite Pipeline Latency Estimate

| Stage | Model | Estimated Latency |
|---|---|---|
| STT | Whisper.net (small, CUDA) | ~500–800ms |
| LLM | Phi-4-mini via Ollama (Q4) | ~300–600ms (first token ~200ms, full response ~500ms) |
| TTS | Piper (CPU) | ~100–200ms |
| **Total** | | **~900–1600ms** |

Compare: OpenAI Realtime = ~300–500ms end-to-end.

The composite pipeline adds **2–3x latency**. Acceptable for non-interactive scenarios but noticeably worse for real-time conversation.

---

### 2D. Local Model Summary for BodyCam

| BodyCam Component | Local Model | VRAM | Quality vs Cloud | Practical? |
|---|---|---|---|---|
| **Realtime voice** | Moshi (int8) | ~12 GB | 60–70% of OpenAI | ⚠️ Tight on 8–12GB, no func calls |
| **Composite voice** | Whisper.net + Phi-4-mini + Piper | ~5–6 GB | 50–60% of OpenAI | ✅ Fits, but 2–3x latency |
| **Chat** | Phi-4-mini via Ollama | ~3 GB | 40–50% of GPT-5.4-mini | ✅ Usable for simple tasks |
| **Vision** | LLaVA-7B / Phi-4-vision | ~5–8 GB | 30–40% of GPT-5.4 | ⚠️ Marginal quality |
| **Transcription** | Whisper.net (small) | ~1 GB | 80–90% of gpt-4o-mini-transcribe | ✅ Excellent |

---

## Part 3 — Recommendations

### For Azure AI Foundry

1. **Realtime voice stays OpenAI** — only option on Azure. `gpt-realtime-1.5` remains the best choice.
2. **Add composite voice pipeline** — enables any Azure-hosted LLM (Claude, Grok, DeepSeek) for voice via `gpt-4o-mini-transcribe` + LLM + `gpt-4o-mini-tts`. Higher latency but much more model flexibility.
3. **Chat/Vision model swapping** — Claude Sonnet 4.6, Grok 4.20, DeepSeek V3.2 are all viable drop-in alternatives for text tasks via OpenAI-compatible API. Could add a model selector to settings.
4. **MAI-Voice-1 / MAI-Transcribe-1** — monitor Microsoft's own audio models. If they mature, could provide a cheaper Azure-native STT/TTS path.

### For Local Models

1. **Whisper.net for local STT** — production-ready, MAUI-compatible, excellent quality. Best immediate value. Could run alongside cloud services as a fallback or for offline mode.
2. **Ollama + Phi-4-mini for local chat** — practical on 8 GB VRAM, function calling support, OpenAI-compatible API. Quality is limited but usable for simple interactions.
3. **Moshi for experimental local voice** — the only true local speech-to-speech option. Needs 12+ GB VRAM and a sidecar process. Lack of function calling is a blocker for BodyCam's tool-based workflow. Worth watching but not recommended for M9.
4. **Piper/Kokoro for local TTS** — if building a composite pipeline, Piper (CPU) or Kokoro (GPU) provide acceptable quality. No .NET bindings though.

### Architecture Implications

The `IRealtimeClient` abstraction from [DESIGN.md](DESIGN.md) already accommodates these scenarios:

| Pattern | Azure Models | Local Models |
|---|---|---|
| **Native realtime** (`IRealtimeClient` impl) | `OpenAiRealtimeClient` (gpt-realtime-1.5) | `MoshiRealtimeClient` (future, if Moshi gains function calls) |
| **Composite pipeline** (`CompositeRealtimeClient`) | Any LLM + gpt-4o-mini-transcribe + gpt-4o-mini-tts | Whisper.net + Ollama + Piper |

The `CompositeRealtimeClient` is the key enabler for both Azure model diversity and local/offline support. It should be a secondary M9 goal after the Gemini native provider.
