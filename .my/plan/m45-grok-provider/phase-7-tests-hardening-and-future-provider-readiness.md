# Phase 7 - Tests, Hardening, And Future Provider Readiness

## Goal

Make Grok reliable enough for continued POC work and make the provider layer
ready for the next provider.

## Work

- Add unit tests for provider registry, credential modes, model lookup, and
  settings migration.
- Add fake provider implementations for text, vision, voice, STT, TTS, and
  image generation.
- Add Grok API integration tests behind opt-in environment variables.
- Add real-device voice tests for direct phone speaker and headset routes.
- Add command tests proving Look/Read work through provider capability
  selection.
- Add telemetry for:
  - selected provider;
  - capability path;
  - latency;
  - token or character usage when available;
  - error category;
  - fallback path.
- Document how to add the next provider.

## Acceptance

- OpenAI and Azure regression tests still pass.
- Grok text and vision integration tests pass when credentials are configured.
- Grok voice path has a documented pass/fail matrix.
- Adding a future provider requires adding a provider definition and adapter
  classes, not rewriting the settings page.

## Implementation Notes

- Provider diagnostics live in `AiProviderDiagnosticsService`.
- Grok text and vision live probes are opt-in outside Android with
  `BODYCAM_GROK_LIVE_TESTS=1`.
- Live test credentials are read from `XAI_API_KEY`, `BODYCAM_XAI_API_KEY`, or
  `BODYCAM_GROK_API_KEY`.
- Look and Read now check the active provider for `Vision` or `ImageInput`
  before taking a picture.
- Future-provider operator docs are in `docs/functionality/07-ai-providers.md`.
- Android voice validation is tracked in
  `.my/plan/m45-grok-provider/phase-7-voice-pass-fail-matrix.md`.

## Future Providers

The next provider should be a proof that M45 worked. Candidate providers:

- Gemini
- Anthropic, when realtime/audio support is sufficient
- local/offline provider for STT or text
- composite provider mixing best-in-class STT, LLM, and TTS
