# M45 - Grok Provider Roadmap

**Status:** Proposed
**Scope:** Add xAI/Grok as a first-class AI provider next to the current
OpenAI and Azure OpenAI connections, while preparing the provider layer for
more providers later.
**Research date:** 2026-05-31

M45 is a provider-expansion milestone. The goal is not just "make Grok work",
but to stop the connection layer from assuming that every future provider is
OpenAI or Azure OpenAI with a slightly different URL.

## Refactor Scope

Yes, M45 should be treated as a real LLM/provider architecture refactor.

There is no requirement to keep the old internal provider APIs, enum names,
settings view models, connection page layout, or OpenAI/Azure branching shape.
M45 may replace them with a new provider registry, provider adapter layer,
credential layer, and capability model.

The compatibility requirement is user-facing and behavioral, not architectural:

- the existing OpenAI connection must still work after it is migrated to the new
  provider model;
- the existing Azure OpenAI connection must still work after it is migrated to
  the new provider model;
- existing app features such as chat, realtime voice, Look, Read, settings,
  tests, and diagnostics should move through the new provider interfaces;
- old internal switches and duplicated OpenAI/Azure paths should be removed
  once the new path covers the behavior.

Do not build Grok as a third special-case branch beside the old OpenAI/Azure
branches. Build the new way first, then port OpenAI, Azure OpenAI, and Grok into
it.

## Product Goal

BodyCam should let the user choose:

- OpenAI
- Azure OpenAI
- Grok / xAI
- later providers without another settings redesign

Grok should be able to power the same product capabilities we care about:

- conversational text and tool use
- image understanding for Look, Read, and future visual commands
- real-time voice conversation where available
- speech to text
- text to speech
- image generation and editing when command/product scope needs it

## Current xAI Capability Snapshot

Official xAI docs currently show:

| Capability | xAI/Grok status | Notes |
| --- | --- | --- |
| Text / chat | Supported | Quickstart shows `https://api.x.ai/v1` and OpenAI-compatible usage. |
| Tool use | Supported | Model docs list function calling. |
| Structured outputs | Supported | Model docs list structured outputs. |
| Vision / image understanding | Supported by some models | Image input uses content arrays with `input_image` and `input_text`. |
| Voice agent | Supported | `wss://api.x.ai/v1/realtime`, speech-to-speech, tool use. |
| TTS | Supported | REST and streaming TTS under `/v1/tts`. |
| STT | Supported | REST and streaming STT under `/v1/stt`. |
| Image generation/editing | Supported | Imagine API supports image generation and editing. |
| OAuth for public inference API | Unconfirmed | Docs for inference and management APIs describe API-key bearer auth; Grok Build opens browser auth on first launch; connector flows use OAuth. |
| Client-side realtime auth | Supported via ephemeral token | Server creates short-lived client secret, client uses it for realtime WebSocket auth. |

This means Grok looks viable for the POC, but the OAuth assumption must be
validated before we design the final credential path.

## OAuth Decision

The user-facing intent is: sign in to Grok instead of pasting an API key when
that is officially available and safe.

The implementation decision for M45:

1. Build the provider credential layer with OAuth support from the beginning.
2. Prove whether xAI exposes an official OAuth or account-login flow that can
   authorize the inference API for third-party apps.
3. If yes, implement OAuth PKCE/device browser login for Grok.
4. If no, ship the POC with xAI API key auth plus ephemeral realtime tokens,
   and keep the OAuth UI/state behind a feature flag until xAI documents the
   flow.

Do not rely on scraped consumer Grok cookies or undocumented private endpoints.
If OAuth is not official enough for an app, M45 should say so plainly and use
the documented API-key/ephemeral-token path.

## Provider Architecture Decision

M45 should move provider selection from an OpenAI-specific enum to provider
metadata.

Current shape:

```csharp
public enum OpenAiProvider { OpenAi, Azure }
public OpenAiProvider Provider { get; set; }
```

Target shape:

```csharp
public sealed record AiProviderDefinition(
    string Id,
    string DisplayName,
    AiProviderCapabilities Capabilities,
    IReadOnlyList<ModelOption> Models,
    IReadOnlyList<CredentialMode> CredentialModes);

public sealed record AiProviderCapabilities(
    bool SupportsChat,
    bool SupportsRealtimeVoice,
    bool SupportsSpeechToText,
    bool SupportsTextToSpeech,
    bool SupportsVision,
    bool SupportsImageGeneration,
    bool SupportsFunctionCalling,
    bool SupportsStructuredOutputs,
    bool SupportsEphemeralClientSecrets,
    bool SupportsOAuthLogin,
    bool IsOpenAiCompatible);

public enum CredentialMode
{
    ApiKey,
    OAuthPkce,
    OAuthDeviceCode,
    EphemeralClientSecret
}
```

Persist the selected provider as a string ID, such as:

- `openai`
- `azure-openai`
- `xai-grok`

This keeps settings and tests from needing a new enum value every time a
provider is added.

## Provider Service Shape

Provider-specific code should be behind small capability interfaces, not one
giant `IGrokClient`.

```csharp
public interface IAiTextProvider
{
    Task<AiTextResult> CompleteAsync(AiTextRequest request, CancellationToken ct);
}

public interface IAiVisionProvider
{
    Task<AiTextResult> CompleteWithImagesAsync(AiVisionRequest request, CancellationToken ct);
}

public interface IAiRealtimeVoiceProvider
{
    Task<IRealtimeSession> ConnectAsync(RealtimeSessionOptions options, CancellationToken ct);
}

public interface ISpeechToTextProvider
{
    Task<SpeechToTextResult> TranscribeAsync(SpeechToTextRequest request, CancellationToken ct);
}

public interface ITextToSpeechProvider
{
    Task<TextToSpeechResult> SpeakAsync(TextToSpeechRequest request, CancellationToken ct);
}

public interface IImageGenerationProvider
{
    Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken ct);
}
```

OpenAI, Azure OpenAI, and Grok can each provide only the interfaces they
support. The orchestration layer selects by capability and provider ID. Any
existing orchestrator code that assumes "OpenAI or Azure" should be rewritten to
ask the selected provider for the needed capability instead.

## Settings UX

The Connection settings page should become provider-centric:

1. Provider list at the top: OpenAI, Azure OpenAI, Grok.
2. Account/credential card for the selected provider.
3. Capability matrix for the selected provider:
   - Text
   - Voice
   - STT
   - TTS
   - Vision
   - Images
4. Provider-specific model pickers.
5. A single "Test Connection" action that reports per-capability status.

This should be phone-first: no nested panels, no deep indentation, and no
desktop-style admin layout.

## Implementation Phases

1. [Provider Registry And Settings Foundation](phase-1-provider-registry-and-settings.md)
2. [Grok Auth And OAuth Spike](phase-2-grok-auth-and-oauth-spike.md)
3. [Grok Text, Tools, And Vision](phase-3-grok-text-tools-and-vision.md)
4. [Grok Voice, STT, And TTS](phase-4-grok-voice-stt-and-tts.md)
5. [Grok Images And Command Capabilities](phase-5-grok-images-and-command-capabilities.md)
6. [Connection Settings UX And Diagnostics](phase-6-connection-settings-ux-and-diagnostics.md)
6a. [LLM Provider Settings Design](phase-6a-llm-provider-settings-design.md)
7. [Tests, Hardening, And Future Provider Readiness](phase-7-tests-hardening-and-future-provider-readiness.md)

## Acceptance

- Grok appears beside OpenAI and Azure OpenAI on Connection settings.
- OpenAI and Azure OpenAI are implemented through the new provider registry and
  adapter layer, not through legacy special-case branches.
- The user can configure Grok credentials using the best official auth path.
- The app can test Grok text, vision, voice, STT, TTS, and image availability
  independently.
- Existing OpenAI and Azure chat, voice, model selection, API-key storage, and
  vision behavior still work through the new provider path.
- Look and Read can use Grok vision.
- Voice conversation can run through either Grok realtime voice or the
  composite STT -> text -> TTS path.
- The code can add a later provider without redesigning settings again.
- Provider-specific failures say which capability failed and why.

## Out Of Scope

- Using undocumented Grok consumer endpoints.
- Shipping OAuth based on reverse-engineered cookies or private app tokens.
- Rewriting camera, audio device, or command architecture.
- Making image generation part of the default Look command.

## Official Sources Reviewed

- xAI Quickstart: https://docs.x.ai/developers/quickstart
- xAI Accounts and Authorization: https://docs.x.ai/developers/rest-api-reference/management/auth
- xAI Ephemeral Tokens: https://docs.x.ai/developers/model-capabilities/audio/ephemeral-tokens
- xAI Voice Overview: https://docs.x.ai/developers/model-capabilities/audio/voice
- xAI Voice REST Reference: https://docs.x.ai/developers/rest-api-reference/inference/voice
- xAI Image Understanding: https://docs.x.ai/developers/model-capabilities/images/understanding
- xAI Imagine Overview: https://docs.x.ai/developers/model-capabilities/imagine
- xAI Grok model docs: https://docs.x.ai/developers/models/grok-4.20
- Grok Build auth note: https://docs.x.ai/build/overview
