# M8a — Azure OpenAI Model Support

**Status:** NOT STARTED  
**Goal:** Full Azure OpenAI provider support — per-role deployment names, proper endpoint construction, auth, and API versioning.

---

## Problem

The current code has basic Azure scaffolding (`OpenAiProvider.Azure`, `AzureResourceName`, `AzureDeploymentName`) but it's incomplete:

1. **Single deployment name** — Azure requires a separate deployment per model. BodyCam uses 4 model roles (realtime, chat, vision, transcription), each needing its own Azure deployment.
2. **Realtime-only URI** — `GetRealtimeUri()` builds a Realtime WebSocket URL but there's no equivalent for Chat Completions or Audio API endpoints.
3. **No Azure API version setting in UI** — hardcoded to `2025-04-01-preview`.
4. **Model picker mismatch** — On Azure, the user deploys a model under a custom name (e.g., `my-gpt-realtime`). The picker should show deployment names, not OpenAI model IDs.
5. **Auth difference** — OpenAI uses `Bearer` token; Azure uses `api-key` header (already handled) or Entra ID (not supported).

---

## Azure OpenAI Model Availability (April 2026)

Key models relevant to BodyCam, confirmed available on Azure:

| Role | Azure Model ID | Deployment Version | Regions (Global Standard) |
|------|---------------|--------------------|---------------------------|
| Realtime | `gpt-realtime-1.5` | 2026-02-23 | East US 2, Sweden Central |
| Realtime (budget) | `gpt-realtime-mini` | 2025-10-06 / 2025-12-15 | East US 2, Sweden Central, Central US |
| Chat | `gpt-5.4-mini` | 2026-03-17 | East US 2, Sweden Central, South Central US, Poland Central |
| Chat (flagship) | `gpt-5.4` | 2026-03-05 | Registration required |
| Chat (cheapest) | `gpt-5.4-nano` | 2026-03-17 | East US 2, Sweden Central, South Central US, Poland Central |
| Vision | `gpt-5.4` / `gpt-5.4-mini` | (same as chat) | Same as chat |
| Transcription (STT) | `gpt-4o-mini-transcribe` | 2025-12-15 | Most regions |
| Transcription (best) | `gpt-4o-transcribe` | 2025-03-20 | Most regions |

> **Note:** Azure uses the same model IDs as OpenAI but requires deployment names. A user might deploy `gpt-realtime-1.5` as `my-realtime-v1`. The deployment name goes in the URL, not the model ID.

### Azure Realtime API Endpoint Format

```
wss://{resource}.openai.azure.com/openai/realtime?api-version={version}&deployment={deployment}
```

### Azure Chat Completions API Endpoint Format

```
https://{resource}.openai.azure.com/openai/deployments/{deployment}/chat/completions?api-version={version}
```

### Azure Audio Transcription API Endpoint Format

```
https://{resource}.openai.azure.com/openai/deployments/{deployment}/audio/transcriptions?api-version={version}
```

### Azure API Versions (current)

| API | Version | Status |
|-----|---------|--------|
| Realtime | `2025-04-01-preview` | Preview (latest with `gpt-realtime-1.5`) |
| Chat Completions | `2025-04-01-preview` | Preview |
| Audio | `2025-04-01-preview` | Preview |

---

## Design

### Overview

When `Provider == Azure`, the user configures **deployment names** instead of selecting from a fixed model list. Each model role gets its own deployment name + optional resource override (for multi-resource setups). A single default resource name applies when no override is set.

### Architecture Change

```
Settings UI (Provider = Azure)
┌──────────────────────────────────────────────┐
│  Provider:  [OpenAI ▾]  /  [Azure ▾]        │
│                                              │
│  Azure Resource Name: [myresource________]   │
│  API Version:         [2025-04-01-preview]   │
│                                              │
│  Realtime Deployment: [my-realtime________]  │
│  Chat Deployment:     [my-chat____________]  │
│  Vision Deployment:   [my-vision__________]  │
│  Transcription Dep.:  [my-transcribe______]  │
│                                              │
│  (Voice, Turn Detection, Noise Reduction     │
│   same as OpenAI — these are session params) │
└──────────────────────────────────────────────┘
```

When `Provider == OpenAI`, the current model pickers (with fixed lists from `ModelOptions`) remain unchanged.

---

## Tasks

### 8a.1 — Expand `ISettingsService` with per-role Azure deployments

Add new properties for Azure deployment names per role:

```csharp
// Add to ISettingsService:
string? AzureChatDeploymentName { get; set; }
string? AzureVisionDeploymentName { get; set; }
string? AzureTranscriptionDeploymentName { get; set; }
string AzureApiVersion { get; set; }
```

Rename existing `AzureDeploymentName` → `AzureRealtimeDeploymentName` for clarity.

`SettingsService` implementation adds matching `Preferences.Get/Set` properties.

### 8a.2 — Extend `AppSettings` with per-role Azure endpoints

```csharp
// Add to AppSettings:
public string? AzureChatDeploymentName { get; set; }
public string? AzureVisionDeploymentName { get; set; }
public string? AzureTranscriptionDeploymentName { get; set; }

// New endpoint builders:
public Uri GetRealtimeUri()       // existing — uses AzureRealtimeDeploymentName
public Uri GetChatUri()           // new — Chat Completions endpoint
public Uri GetVisionUri()         // new — same as Chat (vision is via Chat Completions)
public Uri GetTranscriptionUri()  // new — Audio transcription endpoint
```

Each builder returns either:
- **OpenAI:** Direct API URL with model in query/body
- **Azure:** `https://{resource}.openai.azure.com/openai/deployments/{deployment}/...?api-version=...`

### 8a.3 — Provider-aware Settings UI

Modify `SettingsPage.xaml` to show/hide sections based on provider:

- **Provider picker** — at the top of Settings, `[OpenAI | Azure]`
- **When OpenAI:** Show model pickers (existing behavior)
- **When Azure:** Show text entries for Resource Name, API Version, and 4 deployment names. Hide model pickers.
- Voice settings, system instructions, debug — always visible regardless of provider.

`SettingsViewModel` adds:
```csharp
public string SelectedProvider { get; set; }   // "openai" or "azure"
public bool IsAzure => SelectedProvider == "azure";
public bool IsOpenAi => SelectedProvider != "azure";

// Azure deployment fields (two-way bound to ISettingsService)
public string AzureRealtimeDeployment { get; set; }
public string AzureChatDeployment { get; set; }
public string AzureVisionDeployment { get; set; }
public string AzureTranscriptionDeployment { get; set; }
public string AzureApiVersion { get; set; }
```

### 8a.4 — Wire Azure deployments into `AgentOrchestrator.StartAsync()`

Extend the settings refresh block to copy Azure deployment names:

```csharp
// In StartAsync():
_settings.AzureResourceName = _settingsService.AzureResourceName;
_settings.AzureRealtimeDeploymentName = _settingsService.AzureRealtimeDeploymentName;
_settings.AzureChatDeploymentName = _settingsService.AzureChatDeploymentName;
_settings.AzureVisionDeploymentName = _settingsService.AzureVisionDeploymentName;
_settings.AzureTranscriptionDeploymentName = _settingsService.AzureTranscriptionDeploymentName;
_settings.AzureApiVersion = _settingsService.AzureApiVersion;

var providerStr = _settingsService.Provider;
_settings.Provider = providerStr == "azure" ? OpenAiProvider.Azure : OpenAiProvider.OpenAi;
```

### 8a.5 — Provider-aware auth in `RealtimeClient`

Already done — `ConnectAsync()` sets either `api-key` or `Authorization: Bearer` based on `Provider`. No changes needed.

Future work (out of scope): Entra ID / managed identity auth for Azure.

### 8a.6 — Update `RealtimeClient.UpdateSessionAsync()` transcription model

When on Azure, the transcription model inside the Realtime session should use the deployment name, not the OpenAI model ID. However, the Realtime API's `input_audio_transcription.model` field may still require the model ID (not deployment name) even on Azure — **verify at implementation time**.

```csharp
InputAudioTranscription = new InputAudioTranscription 
{ 
    Model = _settings.TranscriptionModel  // Use from settings, not hardcoded
}
```

This is already partially broken — it's hardcoded to `"gpt-4o-mini-transcribe"` instead of reading from `_settings`. Fix to use `_settings.TranscriptionModel` (which is set from `ISettingsService.TranscriptionModel` during `StartAsync`).

### 8a.7 — Add `TranscriptionModel` to `AppSettings`

`AppSettings` currently lacks a `TranscriptionModel` property. Add it so the orchestrator can propagate it:

```csharp
public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";
```

### 8a.8 — Tests

| Test | Validates |
|------|-----------|
| `AppSettings.GetRealtimeUri_Azure_IncludesDeployment` | Realtime URI contains deployment name and api-version |
| `AppSettings.GetChatUri_Azure_IncludesDeployment` | Chat URI format correct |
| `AppSettings.GetTranscriptionUri_Azure_IncludesDeployment` | Transcription URI format correct |
| `AppSettings.GetRealtimeUri_OpenAi_IncludesModel` | OpenAI URI contains model in query |
| `AppSettings.GetChatUri_OpenAi_ReturnsApiUrl` | OpenAI Chat Completions URL |
| `SettingsService_AzureDeployments_RoundTrip` | All 4 deployment names persist and read back |
| `SettingsViewModel_ProviderToggle_ShowsAzureFields` | `IsAzure` / `IsOpenAi` toggle correctly |

---

## Migration

Rename `AzureDeploymentName` → `AzureRealtimeDeploymentName` everywhere:
- `ISettingsService.AzureDeploymentName` → `AzureRealtimeDeploymentName`
- `SettingsService` — same (Preferences key changes; old key value is lost, acceptable)
- `AppSettings.AzureDeploymentName` → `AzureRealtimeDeploymentName`
- `AppSettings.GetRealtimeUri()` — reference new name

---

## API Key Handling

- **OpenAI:** Single API key (`sk-proj-...`) used for all endpoints.
- **Azure:** Single API key per resource. If all deployments are on the same resource, one key suffices. Multi-resource is out of scope for now.
- Both stored via existing `IApiKeyService` / `SecureStorage`. No changes needed.

---

## Out of Scope

- **Entra ID / Managed Identity auth** — would need `Azure.Identity` SDK. Defer to later milestone.
- **Multi-resource Azure** — different keys per deployment. Defer.
- **Azure API version picker in UI** — just a text entry; user can type any version.
- **Provisioned throughput / deployment scaling** — Azure portal concern, not app concern.
