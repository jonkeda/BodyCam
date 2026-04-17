# Design: Per-Model Connection Validation

## Problem

The current "Test Connection" button does a single `GET /models` call. It tells you if the API key + endpoint work, but not whether each individual model/deployment is valid. If a deployment name is wrong, you only discover it at runtime.

## Goal

After pressing "Test Connection", show a ✓ or ✗ next to each model/deployment name to indicate whether that specific model is reachable and correctly configured.

---

## Behaviour

### Azure Provider

For each configured deployment, probe with a lightweight HEAD-style request:

| Role           | Deployment Property                 | Probe Endpoint                                                                                         |
|----------------|--------------------------------------|--------------------------------------------------------------------------------------------------------|
| Realtime       | `AzureRealtimeDeploymentName`        | `GET https://{resource}.openai.azure.com/openai/deployments/{deployment}?api-version={version}`        |
| Chat           | `AzureChatDeploymentName`            | `GET https://{resource}.openai.azure.com/openai/deployments/{deployment}?api-version={version}`        |
| Vision         | `AzureVisionDeploymentName`          | `GET https://{resource}.openai.azure.com/openai/deployments/{deployment}?api-version={version}`        |
| Transcription  | `AzureTranscriptionDeploymentName`   | `GET https://{resource}.openai.azure.com/openai/deployments/{deployment}?api-version={version}`        |

Azure's `GET /openai/deployments/{name}` returns 200 if the deployment exists, 404 if not. No tokens consumed.

Skip any deployment that is blank (show `—` instead of ✓/✗).

### OpenAI Provider

For OpenAI, models are shared (not per-deployment). Probe with a single `GET /v1/models`, parse the response JSON to check if each selected model ID exists in the returned list.

| Role           | Model Property            | Check                                |
|----------------|---------------------------|--------------------------------------|
| Realtime       | `RealtimeModel`           | model id in `/v1/models` response    |
| Chat           | `ChatModel`               | model id in `/v1/models` response    |
| Vision         | `VisionModel`             | model id in `/v1/models` response    |
| Transcription  | `TranscriptionModel`      | model id in `/v1/models` response    |

---

## ViewModel Changes (`SettingsViewModel.cs`)

### New Properties

```csharp
// Per-model validation status: "✓", "✗ reason", "—" (skipped), "" (not tested yet)
private string _realtimeStatus = string.Empty;
public string RealtimeStatus { get => _realtimeStatus; set => SetProperty(ref _realtimeStatus, value); }

private string _chatStatus = string.Empty;
public string ChatStatus { get => _chatStatus; set => SetProperty(ref _chatStatus, value); }

private string _visionStatus = string.Empty;
public string VisionStatus { get => _visionStatus; set => SetProperty(ref _visionStatus, value); }

private string _transcriptionStatus = string.Empty;
public string TranscriptionStatus { get => _transcriptionStatus; set => SetProperty(ref _transcriptionStatus, value); }
```

### Updated `TestConnectionAsync()`

```
1. Validate key exists → ✗ if not
2. Clear all 4 status properties
3. Set ConnectionStatus = "Testing..."
4. If Azure:
   a. Validate resource name → ✗ if empty
   b. For each deployment (realtime, chat, vision, transcription):
      - If blank → set status to "—"
      - Else → GET /openai/deployments/{name}
        - 200 → "✓"
        - 404 → "✗ not found"
        - else → "✗ {statusCode}"
   c. Run all 4 checks concurrently (Task.WhenAll)
5. If OpenAI:
   a. GET /v1/models → parse JSON array of model IDs
   b. For each model (realtime, chat, vision, transcription):
      - If model ID in list → "✓"
      - Else → "✗ not found"
6. Set ConnectionStatus to summary:
   - All ✓ → "✓ All models verified"
   - Mixed → "⚠ Some models failed"
   - All ✗ → "✗ All models failed"
```

---

## XAML Changes (`SettingsPage.xaml`)

### Add Status Labels Inline with Model Pickers/Entries

For **OpenAI models section**, add a status label after each Picker:

```xml
<HorizontalStackLayout Spacing="8">
    <Picker ItemsSource="{Binding RealtimeModelOptions}"
            SelectedItem="{Binding SelectedRealtimeModel}"
            HorizontalOptions="FillAndExpand" />
    <Label Text="{Binding RealtimeStatus}"
           VerticalOptions="Center" FontSize="13" />
</HorizontalStackLayout>
```

Same pattern for Chat, Vision, Transcription pickers.

For **Azure deployments section**, add a status label after each Entry:

```xml
<HorizontalStackLayout Spacing="8">
    <Entry Text="{Binding AzureRealtimeDeployment}"
           Placeholder="my-realtime-deployment"
           HorizontalOptions="FillAndExpand" />
    <Label Text="{Binding RealtimeStatus}"
           VerticalOptions="Center" FontSize="13" />
</HorizontalStackLayout>
```

Same pattern for Chat, Vision, Transcription entries.

Both sections bind to the **same** status properties — only one provider is visible at a time.

---

## Tasks

| # | Task | Files |
|---|------|-------|
| 1 | Add 4 status backing fields + properties to `SettingsViewModel` | `SettingsViewModel.cs` |
| 2 | Rewrite `TestConnectionAsync()` to probe per-model/deployment and set status properties | `SettingsViewModel.cs` |
| 3 | Wrap each model Picker in `HorizontalStackLayout` with status Label | `SettingsPage.xaml` |
| 4 | Wrap each deployment Entry in `HorizontalStackLayout` with status Label | `SettingsPage.xaml` |
| 5 | Add unit tests for new validation logic | `SettingsViewModelTests.cs` |

---

## Test Cases

| Test | Scenario |
|------|----------|
| `TestConnection_Azure_AllDeploymentsValid_AllCheckmarks` | Mock 4x 200 → all ✓ |
| `TestConnection_Azure_OneDeploymentMissing_ShowsFailure` | Mock 3x 200 + 1x 404 → 3 ✓ + 1 ✗ |
| `TestConnection_Azure_BlankDeployment_ShowsDash` | Empty deployment name → "—" |
| `TestConnection_OpenAi_AllModelsFound_AllCheckmarks` | Models list contains all 4 → all ✓ |
| `TestConnection_OpenAi_ModelNotFound_ShowsFailure` | Models list missing one → ✗ not found |
| `TestConnection_NoKey_ShowsError` | No API key → ✗ on all |
| `TestConnection_NetworkError_ShowsError` | HttpClient throws → ✗ message |
