# M16 — Voice Dictation (Wispr Flow-style)

**Status:** Not started  
**Goal:** System-wide AI dictation — speak naturally through BodyCam, get clean
formatted text injected into any app on the phone or connected PC.

**Depends on:** M1 (audio pipeline), M2 (Realtime API), M12 (audio input architecture).

---

## What This Is

Wispr Flow is an AI dictation tool that converts speech to clean, formatted text
in real time — removing filler words, adding punctuation, and optionally rewriting
for clarity. It works system-wide in any text field.

BodyCam already has the core STT infrastructure via the Realtime API's
`InputTranscriptCompleted` event. M16 adds a **dictation mode** that captures
transcribed speech and injects it as text into the focused app, turning BodyCam
into a hands-free writing tool for smart glasses.

---

## Why This Matters for Smart Glasses

Smart glasses have no keyboard. The only way to compose text (emails, messages,
notes, search queries) is voice. Current phone dictation requires:
1. Pulling out the phone
2. Opening the right app
3. Tapping the mic button
4. Speaking carefully (no cleanup)
5. Manually fixing errors

With M16, the flow becomes:
1. Say "Hey BodyCam, start dictation" (or press glasses button)
2. Speak naturally — the AI cleans up your speech in real time
3. Text appears in whatever app is focused on the phone
4. Say "stop dictation" or press button to finish

---

## Architecture Overview

```
Microphone (PCM 24kHz)
  │
  ▼
AudioInputManager (M12)
  │
  ▼
VoiceInputAgent → RealtimeClient.SendAudioChunkAsync()
                      │
                      ▼
                  OpenAI Realtime API
                      │
                      ├── InputTranscriptCompleted (raw STT)
                      │           │
                      │           ▼
                      │   DictationAgent (NEW — M16)
                      │       │
                      │       ├── Filler removal + cleanup (LLM post-processing)
                      │       ├── Punctuation + formatting
                      │       └── Command detection ("make this a list", "delete that")
                      │           │
                      │           ▼
                      │   ITextInjectionService (NEW — M16)
                      │       │
                      │       ├── Windows: UIAutomation / SendKeys
                      │       ├── Android: AccessibilityService / IME
                      │       └── Clipboard fallback
                      │
                      └── (existing conversation path continues unchanged)
```

---

## What Already Exists

| Component | Status | Reuse |
|-----------|--------|-------|
| Audio capture (PCM) | ✅ M12 | Direct — `IAudioInputProvider` |
| Realtime API client | ✅ M2 | Direct — `IRealtimeClient` |
| Real-time STT | ✅ Built-in | `InputTranscriptCompleted` event |
| VAD (voice activity detection) | ✅ Built-in | `SpeechStarted` / `SpeechStopped` |
| Tool framework | ✅ M3 | Add dictation tools |
| Wake word bindings | ✅ M5 infra | "bodycam-dictate" keyword |
| Button actions | ✅ M14 | Map button → toggle dictation |
| Agent orchestrator | ✅ M2 | Route dictation events |

## What Needs Building

| Component | New? | Description |
|-----------|------|-------------|
| `DictationAgent` | ✅ New | Manages dictation state, buffers transcripts, triggers cleanup |
| `IDictationService` | ✅ New | High-level dictation API (Start/Stop/Pause, mode selection) |
| `ITextInjectionService` | ✅ New | Platform abstraction for injecting text into focused app |
| `ITextInjectionProvider` | ✅ New | Platform-specific implementations (Windows, Android) |
| `TextInjectionManager` | ✅ New | Provider manager (follows M12/M13 pattern) |
| `DictationCleanupService` | ✅ New | LLM post-processing (filler removal, formatting) |
| Dictation mode in orchestrator | Extend | New mode alongside conversation mode |
| Dictation UI overlay | ✅ New | Floating indicator + live preview |
| `StartDictationTool` / `StopDictationTool` | ✅ New | Voice-triggered dictation control |

---

## Dictation Modes

### 1. Raw Mode
Pass-through transcription with minimal cleanup. Just punctuation and
capitalization from the STT model. Lowest latency.

### 2. Clean Mode (Default)
LLM post-processing removes filler words ("um", "uh", "like", "you know"),
fixes false starts, adds proper punctuation. ~500ms additional latency.

### 3. Rewrite Mode
LLM rewrites the speech into polished prose. Higher latency (~1-2s) but
produces publication-quality text. User says rambling thoughts, gets clean
paragraphs.

### 4. Command Mode
User highlights text (or references last dictated block) and says editing
commands: "make this more concise", "turn into bullet points", "fix grammar".
LLM processes the command and replaces the text.

---

## Text Injection Strategies

### Windows
| Method | Pros | Cons |
|--------|------|------|
| **UIAutomation** | Direct text insertion into focused control | Requires focus detection, some apps resist |
| **SendKeys** | Works everywhere | Slow for long text, special char issues |
| **Clipboard + Ctrl+V** | Universal, fast | Overwrites user clipboard |

**Recommended:** Clipboard + Ctrl+V with clipboard save/restore. Fast, universal,
minimal edge cases. Falls back to SendKeys for apps that block paste.

### Android
| Method | Pros | Cons |
|--------|------|------|
| **AccessibilityService** | Can type into any app | Requires accessibility permission, scary to users |
| **Custom IME (InputMethodService)** | Native text input, clean UX | User must set as default keyboard, complex |
| **ClipboardManager + paste** | Simple, no special permissions | Requires manual paste action |

**Recommended:** Start with ClipboardManager + notification to paste. Phase 2
adds AccessibilityService for automatic injection.

---

## Phases

### Phase 1: Core Dictation Pipeline
Build the dictation agent, text injection abstraction, and basic Windows
implementation. Raw mode only (direct STT pass-through). Toggle via voice
command or button.

**Deliverables:** `IDictationService`, `DictationAgent`, `ITextInjectionService`,
Windows clipboard injection, dictation tool, orchestrator dictation mode.

### Phase 2: AI Cleanup & Formatting
Add LLM post-processing for clean mode and rewrite mode. Buffer sentences,
send to GPT-4o-mini for cleanup, inject cleaned text. Personal dictionary
support.

**Deliverables:** `DictationCleanupService`, clean/rewrite modes, sentence
buffering, personal dictionary, latency optimization.

### Phase 3: Android & Command Mode
Android text injection (ClipboardManager → AccessibilityService). Command mode
for editing previously dictated text. Multi-language detection.

**Deliverables:** Android `TextInjectionProvider`, command mode, language
detection, accessibility permissions flow.

### Phase 4: iOS Platform Support
iOS text injection via `UIPasteboard` with programmatic paste. Investigate iOS
custom keyboard extension (`UIInputViewController` / `UITextDocumentProxy`) for
direct text insertion without clipboard. Dictation status indicator via iOS
Live Activity or compact notification. Register provider in DI with `#elif IOS`.

**Deliverables:** iOS `ClipboardTextInjectionProvider` (UIPasteboard),
optional keyboard extension for direct injection, iOS dictation status UI,
`NSPasteboardUsageDescription` if required.

---

## Exit Criteria

- [ ] User can activate dictation mode via voice ("start dictation") or button
- [ ] Speech is transcribed and injected into focused app in real time
- [ ] Raw mode works with <500ms end-to-end latency
- [ ] Clean mode removes filler words and adds punctuation
- [ ] Rewrite mode produces polished prose from rambling speech
- [ ] Works on Windows (clipboard + SendKeys)
- [ ] Works on Android (ClipboardManager, then AccessibilityService)
- [ ] Dictation mode coexists with conversation mode (can switch)
- [ ] Personal dictionary recognizes custom names/jargon
- [ ] Visual indicator shows dictation is active

---

## Cost Considerations

Dictation reuses the existing Realtime API connection. The `InputTranscriptCompleted`
event is already included in the audio input cost ($0.06/min for gpt-4o-transcribe).

Clean/Rewrite modes add GPT-4o-mini text calls (~$0.15/1M input tokens).
Typical dictation session of 5 minutes ≈ 1000 words ≈ 1500 tokens ≈ $0.000225.
Negligible cost.

---

## Documents

| Document | Purpose |
|----------|---------|
| [overview.md](overview.md) | This file — scope, architecture, exit criteria |
| [phase1-dictation-pipeline.md](phase1-dictation-pipeline.md) | Phase 1 — Core dictation + text injection |
| [phase2-ai-cleanup.md](phase2-ai-cleanup.md) | Phase 2 — LLM cleanup, rewrite, dictionary |
| [phase3-android-commands.md](phase3-android-commands.md) | Phase 3 — Android + command mode |
