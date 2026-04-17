# M8b — Settings UI Improvements

**Status:** NOT STARTED  
**Goal:** Improve the settings page with provider switching, .env auto-fill for Azure, test connection, and key visibility toggle.

---

## Current State

- Provider picker exists (`openai` / `azure`) and toggles between model pickers (OpenAI) and deployment entries (Azure)
- API key stored in `SecureStorage` via `IApiKeyService`, loaded from `.env` on first launch
- `.env` overrides applied in `MauiProgram.cs` for Azure resource/deployments, but **only** into `AppSettings` — they don't flow back into `ISettingsService` (the UI never sees them)
- Key field is always masked (`IsPassword="True"`) with no reveal toggle
- No way to verify the connection works without starting a full session

---

## Tasks

### 8b.1 — .env values visible in Settings UI when Azure selected

**Problem:** When `.env` has `OPENAI_PROVIDER=azure` + resource/deployment/key, `MauiProgram.cs` loads them into the `AppSettings` singleton but the `ISettingsService` (Preferences) stays empty. The Settings page reads from `ISettingsService`, so all Azure fields show blank.

**Fix:** In `MauiProgram.cs`, after loading `.env` values into `AppSettings`, also write them into `ISettingsService` so the UI reflects them:

```csharp
if (string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase))
{
    // Write into AppSettings (runtime)
    settings.Provider = OpenAiProvider.Azure;
    settings.AzureResourceName = DotEnvReader.Read("AZURE_OPENAI_RESOURCE");
    // ... existing code ...

    // Also seed into ISettingsService so UI shows the values
    settingsService.Provider = "azure";
    settingsService.AzureResourceName = settings.AzureResourceName;
    settingsService.AzureRealtimeDeploymentName = settings.AzureRealtimeDeploymentName;
    settingsService.AzureChatDeploymentName = settings.AzureChatDeploymentName;
    settingsService.AzureVisionDeploymentName = settings.AzureVisionDeploymentName;
    settingsService.AzureTranscriptionDeploymentName = settings.AzureTranscriptionDeploymentName;
    settingsService.AzureApiVersion = settings.AzureApiVersion;
}
```

This is a one-time seed — once the user changes values in the UI, Preferences takes over.

### 8b.2 — API key visibility toggle

**Problem:** The API key entry has `IsPassword="True"` and `IsReadOnly="True"` with no way to reveal it for verification.

**Fix:** Add a show/hide toggle button next to the key field.

**SettingsViewModel:**
```csharp
private bool _isKeyVisible;
public bool IsKeyVisible
{
    get => _isKeyVisible;
    set { SetProperty(ref _isKeyVisible, value); OnPropertyChanged(nameof(KeyToggleIcon)); }
}

public string KeyToggleIcon => IsKeyVisible ? "👁" : "👁‍🗨";

public ICommand ToggleKeyVisibilityCommand { get; }
    = new RelayCommand(() => IsKeyVisible = !IsKeyVisible);
```

When `IsKeyVisible` is true, `ApiKeyDisplay` shows the full key instead of the masked version. The `IsPassword` binding on the Entry flips to `false`.

**SettingsPage.xaml** — replace the key row:
```xml
<Entry Text="{Binding ApiKeyDisplay}"
       IsPassword="{Binding IsKeyVisible, Converter={StaticResource InvertBoolConverter}}"
       IsReadOnly="True"
       HorizontalOptions="FillAndExpand"
       WidthRequest="200" />
<Button Text="{Binding KeyToggleIcon}"
        Command="{Binding ToggleKeyVisibilityCommand}"
        WidthRequest="40" Padding="0" />
```

Need a simple `InvertBoolConverter` (negate `IsKeyVisible` for `IsPassword`).

### 8b.3 — Test Connection button

**Problem:** No way to verify credentials/endpoints without starting a full audio session.

**Fix:** Add a "Test Connection" button that does a lightweight probe against the configured endpoint.

**Connection test logic:**

| Provider | Test Method |
|----------|-------------|
| OpenAI | `GET https://api.openai.com/v1/models` with `Authorization: Bearer {key}` — returns 200 if key is valid |
| Azure | `GET https://{resource}.openai.azure.com/openai/models?api-version={version}` with `api-key: {key}` — returns 200 if resource + key are valid |

This avoids WebSocket complexity and doesn't consume any model tokens.

**SettingsViewModel:**
```csharp
private string _connectionStatus = string.Empty;
public string ConnectionStatus { get; set; }   // "", "Testing...", "✓ Connected", "✗ Failed: ..."

private bool _isTesting;
public bool IsTesting { get; set; }

public ICommand TestConnectionCommand { get; }
    = new AsyncRelayCommand(TestConnectionAsync);

private async Task TestConnectionAsync()
{
    IsTesting = true;
    ConnectionStatus = "Testing...";

    try
    {
        var key = await _apiKeyService.GetApiKeyAsync();
        if (string.IsNullOrEmpty(key))
        {
            ConnectionStatus = "✗ No API key configured";
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        if (IsAzure)
        {
            var resource = _settings.AzureResourceName;
            var version = _settings.AzureApiVersion;
            if (string.IsNullOrWhiteSpace(resource))
            {
                ConnectionStatus = "✗ Azure resource name is empty";
                return;
            }
            http.DefaultRequestHeaders.Add("api-key", key);
            var uri = $"https://{resource}.openai.azure.com/openai/models?api-version={version}";
            var resp = await http.GetAsync(uri);
            ConnectionStatus = resp.IsSuccessStatusCode
                ? "✓ Connected to Azure OpenAI"
                : $"✗ Azure returned {(int)resp.StatusCode}: {resp.ReasonPhrase}";
        }
        else
        {
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");
            var resp = await http.GetAsync("https://api.openai.com/v1/models");
            ConnectionStatus = resp.IsSuccessStatusCode
                ? "✓ Connected to OpenAI"
                : $"✗ OpenAI returned {(int)resp.StatusCode}: {resp.ReasonPhrase}";
        }
    }
    catch (Exception ex)
    {
        ConnectionStatus = $"✗ {ex.Message}";
    }
    finally
    {
        IsTesting = false;
    }
}
```

**SettingsPage.xaml** — after the API Key section:
```xml
<HorizontalStackLayout Spacing="8">
    <Button Text="Test Connection"
            Command="{Binding TestConnectionCommand}"
            IsEnabled="{Binding IsTesting, Converter={StaticResource InvertBoolConverter}}"
            BackgroundColor="{AppThemeBinding Light=#512BD4, Dark=#7C4DFF}"
            TextColor="White" />
    <Label Text="{Binding ConnectionStatus}"
           VerticalOptions="Center"
           FontSize="13" />
</HorizontalStackLayout>
```

### 8b.4 — InvertBoolConverter

Both 8b.2 and 8b.3 need a `BoolInverterConverter`. Add once, register in `App.xaml` resources:

```csharp
public class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value!;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value!;
}
```

```xml
<!-- App.xaml ResourceDictionary -->
<local:InvertBoolConverter x:Key="InvertBoolConverter" />
```

---

## UI Layout After Changes

```
┌─────────────────────────────────────────────┐
│  Provider                                   │
│  [OpenAI ▾] / [Azure ▾]                    │
│                                             │
│  ┌─ OpenAI ──────────────────────────────┐  │
│  │  Voice Model:        [picker]         │  │
│  │  Chat Model:         [picker]         │  │
│  │  Vision Model:       [picker]         │  │
│  │  Transcription:      [picker]         │  │
│  └───────────────────────────────────────┘  │
│                                             │
│  ┌─ Azure (pre-filled from .env) ────────┐  │
│  │  Resource Name:   [jonk4-me1wf...]    │  │
│  │  API Version:     [2024-10-01-prev]   │  │
│  │  Realtime Deploy: [bodycam-realtime]  │  │
│  │  Chat Deploy:     [______________]    │  │
│  │  Vision Deploy:   [______________]    │  │
│  │  Transcribe Dep.: [______________]    │  │
│  └───────────────────────────────────────┘  │
│                                             │
│  Voice Settings                             │
│  ...                                        │
│                                             │
│  API Key                                    │
│  [••••****••••] [👁] [Change] [Clear]       │
│                                             │
│  [Test Connection]  ✓ Connected to Azure    │
│                                             │
│  Debug                                      │
│  ...                                        │
└─────────────────────────────────────────────┘
```

---

## Tests

| Test | Validates |
|------|-----------|
| `InvertBoolConverter_True_ReturnsFalse` | Converter works |
| `InvertBoolConverter_False_ReturnsTrue` | Converter works |
| `SettingsViewModel_IsAzure_WhenProviderAzure` | Provider toggle |
| `SettingsViewModel_IsOpenAi_WhenProviderOpenAi` | Provider toggle |
| `SettingsViewModel_TestConnection_NoKey_ShowsError` | Error path |

---

## Files Changed

| File | Change |
|------|--------|
| `MauiProgram.cs` | Seed `.env` values into `ISettingsService` |
| `SettingsViewModel.cs` | Add `IsKeyVisible`, `ToggleKeyVisibilityCommand`, `TestConnectionCommand`, `ConnectionStatus` |
| `SettingsPage.xaml` | Key visibility toggle, test connection button + status label |
| `Converters/InvertBoolConverter.cs` | New file |
| `App.xaml` | Register converter |
