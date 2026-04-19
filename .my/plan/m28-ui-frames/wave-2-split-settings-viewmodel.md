# Wave 2 — Split SettingsViewModel

**Prerequisite:** Wave 1 (settings hub cards exist, tests green)  
**Solves:** P1 (God ViewModel)  
**Target:** Build green, all unit tests green, all UI tests green  

---

## Problem

`SettingsViewModel` is 464 lines serving 5 pages. Each sub-page only uses ~20% of its properties.

Current SettingsViewModel responsibilities:
- **Connection** (lines 87–186): Provider, IsOpenAi/IsAzure, model selections, Azure deployment names
- **Connection** (lines 273–376): API key display/change/clear/mask, test connection + HTTP probing
- **Voice** (lines 188–217): Voice, TurnDetection, NoiseReduction, SystemInstructions
- **Device** (lines 345–400): CameraProviders, AudioInputProviders, AudioOutputProviders
- **Advanced** (lines 219–270): DebugMode, ShowTokenCounts, ShowCostEstimate, diagnostics, telemetry
- **Tool Settings** (lines 62–85): ToolSettingsSections, LoadToolSettings, SaveToolSettings
- **Shared** (lines 100–107): Model option arrays, voice option arrays

---

## Extraction Plan

Split into 5 ViewModels, one commit per extraction. Each extraction:
1. Create new ViewModel file
2. Move properties + commands from SettingsViewModel
3. Update sub-page code-behind to inject new ViewModel
4. Update DI registration
5. Build + run affected unit tests

### Step 2a — Extract `ConnectionViewModel`

**New file:** `src/BodyCam/ViewModels/Settings/ConnectionViewModel.cs`

Move from `SettingsViewModel`:
- Constructor deps: `ISettingsService`, `IApiKeyService`, `Func<HttpClient>`
- Properties: `SelectedProvider`, `IsOpenAi`, `IsAzure`
- Model picker options: `RealtimeModelOptions`, `ChatModelOptions`, `VisionModelOptions`, `TranscriptionModelOptions`
- Model selections: `SelectedRealtimeModel`, `SelectedChatModel`, `SelectedVisionModel`, `SelectedTranscriptionModel`
- Azure deployments: `AzureEndpoint`, `AzureApiVersion`, `AzureRealtimeDeployment`, `AzureChatDeployment`, `AzureVisionDeployment`
- API key: `ApiKeyDisplay`, `IsKeyVisible`, `KeyToggleText`, `ChangeApiKeyCommand`, `ClearApiKeyCommand`, `ToggleKeyVisibilityCommand`, `LoadApiKeyDisplay()`, `ChangeApiKeyAsync()`, `ClearApiKeyAsync()`, `MaskKey()`
- Test connection: `ConnectionStatus`, `IsTesting`, `RealtimeStatus`, `ChatStatus`, `VisionStatus`, `TestConnectionCommand`, `TestConnectionAsync()`, `ProbeOpenAiModel()`, `ProbeAzureDeployment()`

```csharp
namespace BodyCam.ViewModels.Settings;

public class ConnectionViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IApiKeyService _apiKeyService;
    private readonly Func<HttpClient> _httpClientFactory;
    private string? _fullKey;

    public ConnectionViewModel(ISettingsService settings, IApiKeyService apiKeyService, 
        Func<HttpClient>? httpClientFactory = null)
    {
        _settings = settings;
        _apiKeyService = apiKeyService;
        _httpClientFactory = httpClientFactory ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        Title = "Connection";

        ChangeApiKeyCommand = new AsyncRelayCommand(ChangeApiKeyAsync);
        ClearApiKeyCommand = new AsyncRelayCommand(ClearApiKeyAsync);
        ToggleKeyVisibilityCommand = new RelayCommand(ToggleKeyVisibility);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);

        LoadApiKeyDisplay();
    }

    // ... all Connection properties/commands moved here verbatim ...
}
```

**Update `ConnectionSettingsPage.xaml.cs`:**
```csharp
// BEFORE
public ConnectionSettingsPage(SettingsViewModel viewModel)

// AFTER
public ConnectionSettingsPage(ConnectionViewModel viewModel)
```

**Update `ConnectionSettingsPage.xaml`:**
```xml
<!-- BEFORE -->
xmlns:vm="clr-namespace:BodyCam.ViewModels"
x:DataType="vm:SettingsViewModel"

<!-- AFTER -->
xmlns:vm="clr-namespace:BodyCam.ViewModels.Settings"
x:DataType="vm:ConnectionViewModel"
```

**Update `ServiceExtensions.cs`:**
```csharp
// ADD
services.AddTransient<ViewModels.Settings.ConnectionViewModel>();
```

### Step 2b — Extract `VoiceViewModel`

**New file:** `src/BodyCam/ViewModels/Settings/VoiceViewModel.cs`

Move from `SettingsViewModel`:
- Properties: `VoiceOptions`, `TurnDetectionOptions`, `NoiseReductionOptions`
- Selections: `SelectedVoice`, `SelectedTurnDetection`, `SelectedNoiseReduction`
- `SystemInstructions`

```csharp
namespace BodyCam.ViewModels.Settings;

public class VoiceViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;

    public VoiceViewModel(ISettingsService settings)
    {
        _settings = settings;
        Title = "Voice & AI";
    }

    public string[] VoiceOptions => ModelOptions.Voices;
    public string[] TurnDetectionOptions => ModelOptions.TurnDetectionModes;
    public string[] NoiseReductionOptions => ModelOptions.NoiseReductionModes;

    public string SelectedVoice
    {
        get => _settings.Voice;
        set => SetProperty(_settings.Voice, value, v => _settings.Voice = v);
    }

    public string SelectedTurnDetection
    {
        get => _settings.TurnDetection;
        set => SetProperty(_settings.TurnDetection, value, v => _settings.TurnDetection = v);
    }

    public string SelectedNoiseReduction
    {
        get => _settings.NoiseReduction;
        set => SetProperty(_settings.NoiseReduction, value, v => _settings.NoiseReduction = v);
    }

    public string SystemInstructions
    {
        get => _settings.SystemInstructions;
        set => SetProperty(_settings.SystemInstructions, value, v => _settings.SystemInstructions = v);
    }
}
```

**Update `VoiceSettingsPage.xaml.cs`:**
```csharp
public VoiceSettingsPage(VoiceViewModel viewModel)
```

**Update `VoiceSettingsPage.xaml`:**
```xml
xmlns:vm="clr-namespace:BodyCam.ViewModels.Settings"
x:DataType="vm:VoiceViewModel"
```

### Step 2c — Extract `DeviceViewModel`

**New file:** `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs`

Move from `SettingsViewModel`:
- `CameraProviders`, `SelectedCameraProvider`
- `AudioInputProviders`, `SelectedAudioInputProvider`
- `AudioOutputProviders`, `SelectedAudioOutputProvider`
- Constructor deps: `CameraManager`, `AudioInputManager`, `AudioOutputManager`
- `ProvidersChanged` event subscriptions

```csharp
namespace BodyCam.ViewModels.Settings;

public class DeviceViewModel : ViewModelBase
{
    private readonly CameraManager _cameraManager;
    private readonly AudioInputManager _audioInputManager;
    private readonly AudioOutputManager _audioOutputManager;

    public DeviceViewModel(CameraManager cameraManager, AudioInputManager audioInputManager,
        AudioOutputManager audioOutputManager)
    {
        _cameraManager = cameraManager;
        _audioInputManager = audioInputManager;
        _audioOutputManager = audioOutputManager;
        Title = "Devices";

        _audioInputManager.ProvidersChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AudioInputProviders));
            OnPropertyChanged(nameof(SelectedAudioInputProvider));
        };

        _audioOutputManager.ProvidersChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AudioOutputProviders));
            OnPropertyChanged(nameof(SelectedAudioOutputProvider));
        };
    }

    // ... Camera, AudioInput, AudioOutput properties moved here ...
}
```

**Update `DeviceSettingsPage.xaml.cs`:**
```csharp
public DeviceSettingsPage(DeviceViewModel viewModel)
```

**Update `DeviceSettingsPage.xaml`:**
```xml
xmlns:vm="clr-namespace:BodyCam.ViewModels.Settings"
x:DataType="vm:DeviceViewModel"
```

### Step 2d — Extract `AdvancedViewModel`

**New file:** `src/BodyCam/ViewModels/Settings/AdvancedViewModel.cs`

Move from `SettingsViewModel`:
- Debug: `DebugMode`, `ShowTokenCounts`, `ShowCostEstimate`
- Diagnostics: `SendDiagnosticData`, `AzureMonitorConnectionString`, `SendCrashReports`, `SentryDsn`, `SendUsageData`
- Tool settings: `ToolSettingsSections`, `LoadToolSettings()`, `SaveToolSettings()`

```csharp
namespace BodyCam.ViewModels.Settings;

public class AdvancedViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;

    public AdvancedViewModel(ISettingsService settings, IEnumerable<ITool> tools)
    {
        _settings = settings;
        Title = "Advanced";
        LoadToolSettings(tools);
    }

    // ... Debug, Diagnostics, Tool Settings properties moved here ...
}
```

**Update `AdvancedSettingsPage.xaml.cs`:**
```csharp
public AdvancedSettingsPage(AdvancedViewModel viewModel)
```

**Update `AdvancedSettingsPage.xaml`:**
```xml
xmlns:vm="clr-namespace:BodyCam.ViewModels.Settings"
x:DataType="vm:AdvancedViewModel"
```

### Step 2e — Slim SettingsViewModel → SettingsHubViewModel

After all extractions, `SettingsViewModel` becomes:

```csharp
namespace BodyCam.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel()
    {
        Title = "Settings";
    }
}
```

Optionally rename to `SettingsHubViewModel` in a separate commit. The SettingsPage hub only needs a Title — the cards navigate via code-behind event handlers, not ViewModel commands.

---

## Files Changed

| File | Action |
|---|---|
| `src/BodyCam/ViewModels/Settings/ConnectionViewModel.cs` | **Create** — extracted from SettingsViewModel |
| `src/BodyCam/ViewModels/Settings/VoiceViewModel.cs` | **Create** — extracted from SettingsViewModel |
| `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs` | **Create** — extracted from SettingsViewModel |
| `src/BodyCam/ViewModels/Settings/AdvancedViewModel.cs` | **Create** — extracted from SettingsViewModel |
| `src/BodyCam/ViewModels/SettingsViewModel.cs` | **Slim** — remove extracted code (~460 → ~10 lines) |
| `src/BodyCam/Settings/ConnectionSettingsPage.xaml` | x:DataType → `ConnectionViewModel` |
| `src/BodyCam/Settings/ConnectionSettingsPage.xaml.cs` | Inject `ConnectionViewModel` |
| `src/BodyCam/Settings/VoiceSettingsPage.xaml` | x:DataType → `VoiceViewModel` |
| `src/BodyCam/Settings/VoiceSettingsPage.xaml.cs` | Inject `VoiceViewModel` |
| `src/BodyCam/Settings/DeviceSettingsPage.xaml` | x:DataType → `DeviceViewModel` |
| `src/BodyCam/Settings/DeviceSettingsPage.xaml.cs` | Inject `DeviceViewModel` |
| `src/BodyCam/Settings/AdvancedSettingsPage.xaml` | x:DataType → `AdvancedViewModel` |
| `src/BodyCam/Settings/AdvancedSettingsPage.xaml.cs` | Inject `AdvancedViewModel` |
| `src/BodyCam/ServiceExtensions.cs` | Add 4 new VM registrations |
| `src/BodyCam/SettingsPage.xaml` | x:DataType → `SettingsViewModel` (unchanged or remove x:DataType) |

## Unit Test Impact

- `ToolSettingsSectionTests` — unchanged (tests `ToolSettingItem` directly, no VM dependency)
- New unit tests should be added for each extracted ViewModel (see Wave 6)
- No existing test breakage expected

## UI Test Impact

- No PageObject changes — AutomationIds unchanged
- Sub-page XAML structure unchanged
- All 56 tests should remain green

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
dotnet test src/BodyCam.Tests -v q --no-restore
# Run UI tests class-by-class to verify no regressions
```
