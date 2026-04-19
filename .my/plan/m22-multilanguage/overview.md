# M22 — Multilanguage Support

**Status:** NOT STARTED  
**Goal:** Enable BodyCam to operate in any language — the user picks their language and the AI speaks, listens, and describes scenes in that language. Also support live translation between two languages.

**Depends on:** None (Realtime API already supports multilingual voice).

---

## Why This Matters

BodyCam currently works only in English. The system instructions are in English, the AI responds in English, and the UI labels are English. The OpenAI Realtime API already supports multilingual input/output — the missing piece is configuration, prompt engineering, and UI localization.

Key use cases:
1. **Native language operation** — a Spanish-speaking user wants the AI to listen and respond in Spanish
2. **Live translation** — user speaks French, AI translates and repeats in English (already stubbed via `SetTranslationModeTool`)
3. **Scene description in user's language** — vision tool outputs descriptions in the configured language
4. **UI localization** — buttons, labels, and status text in the user's language

---

## Current State

- **System instructions** are hardcoded in English (`AppSettings.SystemInstructions`)
- **Voice presets** (alloy, ash, marin, etc.) support multilingual output natively — no change needed
- **`SetTranslationModeTool`** exists and appends a translation instruction to the system prompt, but the approach is fragile (string concatenation, no undo)
- **Vision tools** (`DescribeSceneTool`, `ReadTextTool`, `FindObjectTool`) don't specify response language — they inherit from the system prompt
- **UI text** is all hardcoded English strings in XAML and C#
- **No `CultureInfo` or locale awareness** anywhere in the app

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│  Settings                                        │
│  ├─ AppLanguage (UI locale)                      │
│  ├─ ConversationLanguage (AI speaks/listens)     │
│  └─ TranslationTarget (optional, for live mode)  │
├─────────────────────────────────────────────────┤
│  System Prompt Builder                           │
│  └─ Injects language directives into prompt      │
│     "Respond in {ConversationLanguage}"          │
│     "Translate user speech to {Target}"          │
├─────────────────────────────────────────────────┤
│  Tool Integration                                │
│  ├─ DescribeSceneTool  → respond in language     │
│  ├─ ReadTextTool       → output in language      │
│  ├─ SetTranslationMode → managed language swap   │
│  └─ All tools          → context.Language        │
├─────────────────────────────────────────────────┤
│  UI Localization                                 │
│  └─ .resx resource files per locale              │
│     AppResources.en.resx, AppResources.es.resx   │
└─────────────────────────────────────────────────┘
```

---

## Phases

### Phase 1: Conversation Language Setting

Add a language setting that controls how the AI listens and responds.

- Add `ConversationLanguage` property to `ISettingsService` (default: `"English"`)
- Build system prompt dynamically: inject `"Always respond in {language}."` directive
- Create `SystemPromptBuilder` service that composes the final prompt from base instructions + language + translation mode
- Replace hardcoded `AppSettings.SystemInstructions` with builder output
- Update `SessionConfig` to include `Language` field
- Settings UI: language picker in Voice section (searchable dropdown with common languages)
- Supported languages: all languages supported by the Realtime API (English, Spanish, French, German, Italian, Portuguese, Dutch, Polish, Russian, Chinese, Japanese, Korean, Arabic, Hindi, Turkish, etc.)

### Phase 2: Vision & Tool Language Awareness

Make all tool outputs respect the configured language.

- Add `Language` property to `ToolContext`
- Update `DescribeSceneTool` prompt: `"Describe this scene in {language}"`
- Update `ReadTextTool` prompt: `"Read and translate any text in the image to {language}"`
- Update `FindObjectTool` prompt: `"Respond in {language}"`
- Update `DeepAnalysisTool` prompt: include language directive
- Refactor `SetTranslationModeTool` to use `SystemPromptBuilder` instead of string concatenation
- Add `TranslationTarget` to `ISettingsService` (null = no translation)
- Translation mode becomes a first-class state: `SystemPromptBuilder` composes the directive cleanly

### Phase 3: UI Localization

Localize all user-facing strings in the app.

- Create `Resources/Strings/AppResources.resx` (English, default)
- Extract all hardcoded strings from XAML and C# into resource keys
- MainPage: button labels, status text, tab names, accessibility descriptions
- SettingsPage: section headers, labels, placeholders
- Create initial translations:
  - `AppResources.es.resx` (Spanish)
  - `AppResources.fr.resx` (French)
  - `AppResources.de.resx` (German)
  - `AppResources.ja.resx` (Japanese)
  - `AppResources.zh.resx` (Chinese Simplified)
- Add `AppLanguage` setting to `ISettingsService` (default: device locale)
- Set `CultureInfo.CurrentUICulture` on startup based on setting
- XAML bindings: `Text="{x:Static resources:AppResources.LookButton}"`

### Phase 4: Live Translation Mode

Polish the real-time translation experience.

- `SystemPromptBuilder` composes translation directive: `"The user speaks {source}. Translate everything they say into {target} and speak the translation aloud. Also respond to questions in {target}."`
- Translation mode indicator in status bar (shows source → target)
- Quick-toggle: button or wake word to flip source/target languages
- Conversation language auto-detection (let the Realtime API detect, show detected language in UI)
- Translation history: transcript entries tagged with original/translated language

### Phase 5: iOS Platform Support

- Verify `.resx` localization works on iOS
- Verify `CultureInfo` setting applies correctly
- Test Realtime API multilingual voice on iOS
- RTL layout support for Arabic/Hebrew (if needed)

---

## Supported Languages

The OpenAI Realtime API supports these languages natively (voice in + voice out):

| Language | Code | Voice Quality |
|----------|------|---------------|
| English | en | Native |
| Spanish | es | Native |
| French | fr | Native |
| German | de | Native |
| Italian | it | Native |
| Portuguese | pt | Native |
| Dutch | nl | Native |
| Polish | pl | Native |
| Russian | ru | Native |
| Chinese (Mandarin) | zh | Native |
| Japanese | ja | Native |
| Korean | ko | Native |
| Arabic | ar | Native |
| Hindi | hi | Native |
| Turkish | tr | Native |
| Swedish | sv | Good |
| Norwegian | no | Good |
| Danish | da | Good |
| Finnish | fi | Good |
| Czech | cs | Good |
| Romanian | ro | Good |
| Ukrainian | uk | Good |
| Thai | th | Good |
| Vietnamese | vi | Good |
| Indonesian | id | Good |

No special configuration needed — the API handles language detection and voice synthesis automatically. The `ConversationLanguage` setting controls the system prompt directive which steers the response language.

---

## Integration Points

| System | Change |
|--------|--------|
| **AppSettings** | Add `ConversationLanguage`, `TranslationTarget` |
| **ISettingsService** | Add `ConversationLanguage`, `TranslationTarget`, `AppLanguage` |
| **SessionConfig** | Add `Language` field |
| **AgentOrchestrator.StartAsync** | Use `SystemPromptBuilder` for prompt composition |
| **ToolContext** | Add `Language` property |
| **DescribeSceneTool** | Include language in vision prompt |
| **ReadTextTool** | Include language in OCR prompt |
| **FindObjectTool** | Include language in search prompt |
| **DeepAnalysisTool** | Include language in analysis prompt |
| **SetTranslationModeTool** | Refactor to use `SystemPromptBuilder` |
| **SettingsPage.xaml** | Language picker (conversation + UI) |
| **SettingsViewModel** | Language binding properties |
| **MainPage.xaml** | All strings → resource references |
| **MauiProgram.cs** | Set `CultureInfo` from settings |

---

## Privacy

- Language preference is stored locally (device `Preferences`)
- Language setting is included in telemetry tags (if M19 analytics enabled) — not PII
- Translated content follows same privacy rules as original content (no remote logging of transcripts)

---

## Success Criteria

1. User selects Spanish → AI listens in Spanish, responds in Spanish, describes scenes in Spanish
2. Translation mode: user speaks French → AI repeats in English
3. Vision tools respect language setting
4. UI labels display in configured language (at least 5 locales)
5. Language persists across app restarts
6. No regression in English-only operation
