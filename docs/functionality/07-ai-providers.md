# AI Providers

This app now treats OpenAI, Azure OpenAI, and Grok as provider instances behind
one provider registry. The settings UI should not need a redesign when another
provider is added.

## Runtime Shape

- `AiProviderRegistry` owns the provider definitions, capability flags, setup
  links, credential modes, and model lists.
- `AiProviderInstanceStore` stores the configured provider instances as JSON in
  MAUI Preferences. API keys remain in secure storage through `IApiKeyService`.
- `AppChatClient` creates the active chat client from `AppSettings.ProviderId`.
- `LlmProvider*` settings pages render from the registry and instance store.
- `AiProviderDiagnosticsService` owns provider-specific connection checks and
  emits diagnostic telemetry.

## Adding A Provider

1. Add a provider id to `AiProviderIds`.
2. Add an `IAiProviderAdapter` implementation with:
   - display names and description;
   - supported `AiProviderCapability` flags;
   - credential modes;
   - model lists by `AiModelKind`;
   - setup links for account, billing, API keys, and docs.
3. Register the adapter in `AiProviderRegistry.CreateDefaultAdapters()` and DI.
4. Add small capability providers only for the features the provider supports:
   text, vision, STT, TTS, image generation, image editing, or realtime voice.
5. Add chat/realtime creation to `AppChatClient` and `ServiceExtensions`.
6. Extend `AiProviderDiagnosticsService` with the cheapest useful connection
   probe. Keep expensive image/audio probes opt-in.
7. Add unit tests using fake provider implementations.
8. Add live integration tests behind provider-specific environment variables.

The settings list and provider detail page should pick up the provider from the
registry. If a provider needs a special field, add the field to settings and
show it conditionally on the detail page.

## Credentials

Use `ApiKeyService` with the provider id as the storage key. Store provider
instance metadata separately as JSON in MAUI Preferences so the app can support
multiple configured providers later without moving secrets.

OAuth should only be enabled for a provider when the provider documents an
official third-party auth flow for inference API access. Realtime ephemeral
tokens are treated as a credential mode but are brokered separately from the
stored API key.

## Capability Selection

Commands should check provider capabilities before using a feature. For example,
Look and Read require `Vision` or `ImageInput`; they fail before capture when
the active provider is text-only.

Telemetry should include:

- `provider.id`
- `capability.path`
- `command`, when command-driven
- `model.id`, when available
- `latency.ms`
- token usage when returned by the provider
- `error.category`
- `fallback.path`

## Test Contract

A future provider is ready when:

- registry tests prove capabilities, credential modes, setup links, and models;
- instance-store tests prove the provider can be created and activated;
- command tests prove unsupported capabilities fail early;
- diagnostics tests prove missing credentials and live probes behave correctly;
- live tests are opt-in and do not run by default.
