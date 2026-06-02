# Phase 2 - Grok Auth And OAuth Spike

## Goal

Determine and implement the safest official credential path for Grok.

## Current Finding

As of 2026-05-31, xAI inference and management docs show API-key bearer auth
for `api.x.ai`. xAI also documents ephemeral client secrets for browser/mobile
Realtime API connections. Grok Build opens a browser for authentication on
first launch, but that is documented for the Grok Build tool, not clearly as a
third-party app OAuth flow for inference API access.

## Implementation Decision

Checked again during implementation. Official xAI docs still describe
`Authorization: Bearer <your xAI API key>` for inference requests and API keys
for account authorization. No official OAuth PKCE or device-code flow for
third-party inference API usage was found.

Phase 2 therefore implements:

- Grok as selectable through provider metadata.
- xAI API key storage under a provider-specific key.
- OAuth disabled/unavailable in provider metadata with a visible explanation.
- A Grok ephemeral-token broker abstraction for future realtime voice so the
  mobile app can use a short-lived client secret instead of sending a long-lived
  xAI API key to client-side WebSocket code.

Sources:

- xAI inference API overview:
  https://docs.x.ai/developers/rest-api-reference/inference
- xAI accounts and authorization:
  https://docs.x.ai/developers/rest-api-reference/management/auth
- xAI ephemeral tokens:
  https://docs.x.ai/developers/model-capabilities/audio/ephemeral-tokens

## Work

- Verify whether xAI offers official OAuth PKCE/device-code login for
  third-party inference API usage.
- If official OAuth exists:
  - add a `GrokOAuthCredentialProvider`;
  - store refresh/access tokens in SecureStorage;
  - implement token refresh and sign-out;
  - document scopes, token lifetime, and failure modes.
- If OAuth is not official for inference:
  - implement xAI API key entry/storage;
  - implement optional server-side ephemeral token broker for realtime voice;
  - keep OAuth UI disabled or hidden behind a feature flag.
- Add credential mode tests for:
  - API key present/missing;
  - OAuth token present/expired;
  - ephemeral client secret create/fail;
  - sign-out clears provider secrets.

## Acceptance

- The app never exposes a long-lived xAI API key in client-side realtime
  WebSocket code when an ephemeral token broker is configured.
- The connection page clearly says whether Grok is signed in, using an API
  key, or waiting for OAuth support.
- The POC does not depend on undocumented consumer Grok endpoints.

## Decision Gate

If official OAuth cannot be proven, the rest of M45 continues with documented
API-key bearer auth plus ephemeral realtime tokens.
