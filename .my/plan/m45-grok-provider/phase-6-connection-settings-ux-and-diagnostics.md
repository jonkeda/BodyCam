# Phase 6 - Connection Settings UX And Diagnostics

## Goal

Make provider configuration understandable on a phone.

## Work

- Replace the OpenAI/Azure radio row with a provider list.
- Show one provider at a time with flat, non-indented sections.
- Add credential status:
  - signed in with OAuth;
  - using API key;
  - using server-issued ephemeral realtime tokens;
  - not configured.
- Add capability status rows:
  - Text
  - Voice
  - STT
  - TTS
  - Vision
  - Images
- Add per-provider test calls and readable diagnostics.
- Add masked key/token display and sign-out/clear actions.
- Log selected provider ID, credential mode, model IDs, endpoint class, and
  capability test results without logging secrets.

## Acceptance

- The Connection page works on narrow phone screens.
- A tester can see which Grok capabilities are configured and which failed.
- Changing provider does not erase another provider's credentials or model
  choices.
- UI tests cover provider switching and Grok settings visibility.
