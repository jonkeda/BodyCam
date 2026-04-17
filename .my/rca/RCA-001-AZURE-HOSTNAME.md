# RCA: All Azure Model Validation Checks Fail

## Symptom

"Test Connection" reports all 4 deployments as failed (✗) despite correct deployment names, API key, and resource name.

## Root Cause

**Wrong Azure hostname suffix.** The codebase hardcodes `.openai.azure.com` but the actual Azure AI endpoint uses `.cognitiveservices.azure.com`.

| What we use | What Azure actually is |
|---|---|
| `https://jonk4-me1wfl8r-eastus2.openai.azure.com/...` | `https://jonk4-me1wfl8r-eastus2.cognitiveservices.azure.com/...` |

Azure AI Services resources created through the Azure AI Foundry portal use the `cognitiveservices.azure.com` domain, not the legacy `openai.azure.com` domain. The `.openai.azure.com` suffix only applies to dedicated Azure OpenAI resources created through the older Azure OpenAI portal.

## Evidence

Azure-generated sample code for the `bodycam-chat` deployment:

```python
endpoint = "https://jonk4-me1wfl8r-eastus2.cognitiveservices.azure.com/"
```

Our `AppSettings.cs` builds all URIs with the wrong domain:

```csharp
new Uri($"https://{AzureResourceName}.openai.azure.com/openai/deployments/...")
```

## Impact

- **All 4 deployment probes fail** — DNS resolves but returns errors or times out
- **All runtime API calls would also fail** — same URLs used by `GetRealtimeUri()`, `GetChatUri()`, `GetVisionUri()`, `GetTranscriptionUri()`
- The `.env` seeding, DI fix, and per-model validation all work correctly — they were just calling the wrong host

## Affected Files

| File | Hardcoded `.openai.azure.com` |
|------|-------------------------------|
| `AppSettings.cs` | `GetRealtimeUri()`, `GetChatUri()`, `GetVisionUri()`, `GetTranscriptionUri()` |
| `SettingsViewModel.cs` | `TestConnectionAsync()` → `ProbeAzureDeployment()` |
| `AppSettingsTests.cs` | Test assertions for Azure URI host |

## Fix

Replace the hardcoded hostname suffix with a configurable `AzureEndpointSuffix` property, or better — store the full endpoint base URL (e.g. `https://jonk4-me1wfl8r-eastus2.cognitiveservices.azure.com`) instead of just the resource name, so users paste exactly what Azure gives them.

**Recommended approach**: Replace `AzureResourceName` with `AzureEndpoint` (full URL). This:
1. Eliminates hostname-suffix guessing entirely
2. Matches Azure SDK convention (`AzureOpenAI(endpoint, credential)`)
3. Works for both `.cognitiveservices.azure.com` and `.openai.azure.com` resources
4. Works for private endpoints with custom domains

### Required changes

1. `AppSettings.cs`: Replace `AzureResourceName` → `AzureEndpoint` (string, full base URL). Update all 4 URI builders.
2. `ISettingsService.cs` / `SettingsService.cs`: Replace property name.
3. `SettingsViewModel.cs`: Replace `AzureResourceName` → `AzureEndpoint`. Update probe URL.
4. `SettingsPage.xaml`: Update Entry label/placeholder to "Endpoint" with placeholder `https://your-resource.cognitiveservices.azure.com`
5. `MauiProgram.cs`: Read `AZURE_OPENAI_ENDPOINT` from .env instead of `AZURE_OPENAI_RESOURCE`.
6. `.env` / `.env.example`: Replace `AZURE_OPENAI_RESOURCE` with `AZURE_OPENAI_ENDPOINT`.
7. `AppSettingsTests.cs`: Update assertions.
