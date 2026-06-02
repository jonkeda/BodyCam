# Phase 6a - LLM Provider Settings Design

## Goal

Design the phone-first settings flow for managing LLM providers. The user
should be able to add OpenAI, Azure OpenAI, Grok, and later providers without
the settings page becoming another provider-specific switch statement.

This is a design phase. It can be implemented after the provider registry and
credential layer are stable enough to support provider instances.

## Navigation

Settings

LLM Providers

Add LLM Provider

Provider Settings

The existing Connection settings page can either be renamed to LLM Providers or
link to this new page. The important change is that provider management becomes
a list of configured provider instances, not a single OpenAI/Azure choice row.

## Page 1 - LLM Providers

Purpose: show configured providers and the active provider.

Layout:

- Title: LLM Providers
- Primary button: Add LLM Provider
- Flat list of configured providers
- Each provider row:
  - provider name
  - configured status
  - active status
  - short capability summary
  - edit button

Example rows:

- OpenAI
  - Active
  - API key configured
  - Text, Voice, Vision
  - Edit
- Azure OpenAI
  - Not active
  - Endpoint missing
  - Text, Vision
  - Edit
- Grok
  - Not configured
  - Sign in or add API key
  - Text, Voice, STT, TTS, Vision, Images
  - Edit

Phone design rules:

- Do not indent nested settings.
- Do not put cards inside cards.
- Use full-width rows with simple separators.
- Put status text under the provider name, not in a right-side desktop column.
- Use icon buttons for edit, test, delete, and open-browser actions.
- Keep Add LLM Provider visible near the top.

Actions:

- Add LLM Provider opens the provider picker.
- Edit opens the provider-specific settings page.
- Tapping the row can also open edit.
- Active provider can be changed from the provider-specific page, or with a
  compact active control on the row if it stays readable on phone.

## Page 2 - Add LLM Provider

Purpose: choose the provider type to add.

Layout:

- Title: Add LLM Provider
- Simple full-width provider buttons

Buttons:

- Grok
- OpenAI
- Azure OpenAI

Each button should show a short second line:

- Grok: xAI account, OAuth if available, API key fallback
- OpenAI: OpenAI platform API key
- Azure OpenAI: Azure endpoint and deployment names

Behavior:

- Selecting a provider creates a configured provider instance if one does not
  exist yet, then opens that provider's settings page.
- If the provider already exists, open the existing provider settings page.
- Later, allow multiple provider instances by adding an account name field.
  Example: OpenAI Personal, OpenAI Work, Azure Production.

## Page 3 - Provider Settings

Purpose: configure one provider instance.

Common layout:

- Title: provider display name
- Status row
- Set active button or switch
- Credential section
- Provider setup button
- Provider-specific settings
- Model and capability settings
- Test connection button
- Diagnostics summary
- Remove provider button

Common actions:

- Set Active
- Test Connection
- Open Provider Setup
- Clear Credentials
- Remove Provider

The provider setup button opens the system browser using provider metadata. It
should not be hard-coded in the page. Each provider definition owns its setup
links.

Recommended provider metadata:

```csharp
public sealed record AiProviderSetupLink(
    string Label,
    Uri Url,
    AiProviderSetupLinkKind Kind);

public enum AiProviderSetupLinkKind
{
    Account,
    Billing,
    ApiKeys,
    Documentation,
    Portal
}
```

## Grok Settings Page

Primary user path:

- Open Grok setup
- Sign in with browser if official OAuth is available
- Otherwise enter xAI API key
- Test capabilities
- Set Active

Fields and controls:

- Credential mode
  - Browser sign-in
  - API key
- API key field when API key mode is selected
- Realtime voice model
- Text model
- Vision model
- STT model
- TTS voice/model
- Image generation model
- Test Text
- Test Vision
- Test Voice
- Test STT
- Test TTS
- Test Images

Provider setup buttons:

- Open Grok account
- Open xAI billing or subscription
- Open API keys
- Open docs

Notes:

- Browser sign-in must only ship if the auth path is official.
- If OAuth is not official yet, show API key as the available path and keep
  browser sign-in disabled or hidden behind a feature flag.
- Ephemeral realtime tokens should be an implementation detail unless the user
  needs diagnostics.

## OpenAI Settings Page

Primary user path:

- Open OpenAI setup
- Create or copy an API key
- Paste API key
- Pick models
- Test connection
- Set Active

Fields and controls:

- API key field
- Organization or project field if needed later
- Realtime model
- Chat model
- Vision model
- Transcription model
- Voice
- Test Text
- Test Vision
- Test Voice
- Test STT

Provider setup buttons:

- Open OpenAI account
- Open billing
- Open API keys
- Open docs

## Azure OpenAI Settings Page

Primary user path:

- Open Azure portal
- Create or select Azure OpenAI resource
- Enter endpoint
- Enter deployment names
- Enter API version
- Enter API key
- Test deployments
- Set Active

Fields and controls:

- Azure endpoint
- API key
- API version
- Realtime deployment name
- Chat deployment name
- Vision deployment name
- Transcription deployment name if needed
- Test Realtime Deployment
- Test Chat Deployment
- Test Vision Deployment

Provider setup buttons:

- Open Azure portal
- Open Azure OpenAI resource
- Open keys and endpoint
- Open deployments
- Open docs

Notes:

- Azure remains deployment-based, not model-picker based.
- The UI should say deployment name, not model name, on Azure pages.

## Storage Design

Use provider instances, not one global provider settings object.

Recommended shape:

```csharp
public sealed record AiProviderInstanceSettings
{
    public required string InstanceId { get; init; }
    public required string ProviderId { get; init; }
    public required string DisplayName { get; init; }
    public bool IsActive { get; init; }
    public required string CredentialMode { get; init; }
    public Dictionary<string, string> Settings { get; init; } = [];
}
```

Persistence:

- Store the provider instance list as JSON in `Preferences`.
- Store non-secret provider settings in the instance JSON.
- Store API keys, OAuth refresh tokens, and other secrets in `SecureStorage`.
- Key secrets by `InstanceId`, not only by provider ID.
- Keep `ActiveProviderInstanceId` separate for quick lookup.

This lets OpenAI, Azure OpenAI, and Grok keep different fields without adding
new flat settings properties for every future provider.

## Acceptance

- LLM Providers page shows all configured provider instances.
- Add LLM Provider page offers Grok, OpenAI, and Azure OpenAI as simple
  buttons.
- Each provider has a specialized settings page with provider setup links.
- Grok page can guide the user to subscription/account setup and supports the
  OAuth/API-key decision from Phase 2.
- Azure page uses deployment terminology.
- OpenAI page uses model terminology.
- Settings UI remains flat and comfortable on narrow phone screens.
- Adding a later provider requires provider metadata and a settings component,
  not a redesign of the LLM Providers page.
