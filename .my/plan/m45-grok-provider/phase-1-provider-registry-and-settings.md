# Phase 1 - Provider Registry And Settings Foundation

## Goal

Make provider selection generic before adding Grok behavior. This is allowed to
be a breaking internal redesign. Grok should not be added as another branch in
an OpenAI-specific enum unless that branch is only temporary while the new
registry lands.

OpenAI and Azure OpenAI should be ported into the new provider model in this
phase, not preserved as a parallel legacy architecture.

## Compatibility Position

No backward compatibility is required for old internal provider APIs, enum
names, settings classes, or view-model branching.

Compatibility is required for product behavior:

- OpenAI still works as a selectable provider.
- Azure OpenAI still works as a selectable provider.
- Existing API keys, Azure endpoint/deployment settings, model selections, and
  connection tests are migrated or read into the new settings shape.
- Features that already depend on OpenAI/Azure continue to work after they use
  the new provider interfaces.

## Work

- Introduce provider IDs such as `openai`, `azure-openai`, and `xai-grok`.
- Add an `AiProviderDefinition` registry with display name, credential modes,
  capability flags, model lists, and supported endpoints.
- Move model options behind provider-aware lookup methods.
- Split settings into shared settings and provider-specific settings.
- Replace the current `OpenAiProvider` enum with provider IDs after migration.
- Add migration from the current `OpenAiProvider` enum to the new provider ID.
- Add OpenAI and Azure OpenAI provider adapters that implement the new
  capability interfaces.
- Move existing OpenAI/Azure connection tests to target the new provider
  registry and adapters.
- Remove old provider-specific UI branching once metadata-driven rendering
  covers it.

## Acceptance

- Current OpenAI and Azure OpenAI settings still load and save.
- OpenAI and Azure OpenAI run through the new provider registry and adapters.
- Connection settings can render from provider metadata.
- Tests prove a new provider can be registered without changing a central UI
  switch.
- Existing OpenAI and Azure connection tests still pass.
- No new Grok behavior is required yet.

## Notes

This phase is infrastructure only. It should not call xAI yet.
