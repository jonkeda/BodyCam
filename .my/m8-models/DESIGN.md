# M8 — Model Selection, Settings Persistence & Debug

**Status:** NOT STARTED
**Goal:** Let users pick GPT models per use-case, persist all settings, and expose a debug panel for diagnostics.

---

## Scope

| # | Task | Description |
|---|------|-------------|
| 8.1 | `ISettingsService` + `SettingsService` | Read/write all user settings via MAUI `Preferences` |
| 8.2 | Model picker constants | Static `ModelOptions` class with valid model IDs per category |
| 8.3 | Fix incorrect model IDs | Correct `RealtimeModel` and transcription model from bad rename |
| 8.4 | `SettingsPage` + `SettingsViewModel` | Full settings UI with model pickers, voice, debug toggle |
| 8.5 | Debug panel enhancements | Token counts, model in use, connection state, cost estimate |
| 8.6 | Wire settings into pipeline | `AppSettings` re-reads from `ISettingsService` on each session start |
| 8.7 | Shell navigation | Add `AppShell` with flyout/tab for Main + Settings pages |
| 8.8 | Tests | Unit tests for `SettingsService`, `ModelOptions`, settings round-trip |

---

## Design

### 8.1 — ISettingsService

Thin wrapper over `Preferences` (MAUI's key/value store — persisted per-app, no encryption needed for non-secret data). API key stays in `IApiKeyService` / `SecureStorage`.

```csharp
namespace BodyCam.Services;

public interface ISettingsService
{
    // Models
    string RealtimeModel { get; set; }
    string ChatModel { get; set; }
    string VisionModel { get; set; }
    string TranscriptionModel { get; set; }

    // Voice
    string Voice { get; set; }
    string TurnDetection { get; set; }
    string NoiseReduction { get; set; }

    // Provider
    string Provider { get; set; }          // "openai" | "azure"
    string? AzureResourceName { get; set; }
    string? AzureDeploymentName { get; set; }

    // Debug
    bool DebugMode { get; set; }
    bool ShowTokenCounts { get; set; }
    bool ShowCostEstimate { get; set; }

    // System
    string SystemInstructions { get; set; }
}
```

**Implementation:** Each property maps to `Preferences.Get<T>(key, default)` / `Preferences.Set(key, value)`. No caching needed — `Preferences` is fast (backed by `SharedPreferences` on Android, `NSUserDefaults` on iOS, registry on Windows).

```csharp
public class SettingsService : ISettingsService
{
    public string RealtimeModel
    {
        get => Preferences.Get(nameof(RealtimeModel), ModelOptions.DefaultRealtime);
        set => Preferences.Set(nameof(RealtimeModel), value);
    }
    // ... same pattern for all properties
}
```

### 8.2 — ModelOptions

Static class providing the valid choices for each picker, plus defaults. Sourced from M8 RESEARCH.md.

```csharp
namespace BodyCam;

public static class ModelOptions
{
    // --- Realtime (voice) ---
    public const string DefaultRealtime = "gpt-realtime-1.5";
    public static readonly string[] RealtimeModels =
    [
        "gpt-realtime-1.5",   // Best voice quality, $32/$64 audio
        "gpt-realtime-mini",  // Budget voice, ~$0.60/$2.40 text
    ];

    // --- Chat (text reasoning) ---
    public const string DefaultChat = "gpt-5.4-mini";
    public static readonly string[] ChatModels =
    [
        "gpt-5.4",       // Flagship, $2.50/$15
        "gpt-5.4-mini",  // Fast/cheap, $0.75/$4.50
        "gpt-5.4-nano",  // Cheapest, $0.20/$1.25
    ];

    // --- Vision ---
    public const string DefaultVision = "gpt-5.4";
    public static readonly string[] VisionModels =
    [
        "gpt-5.4",       // Best quality
        "gpt-5.4-mini",  // Cheaper, still good
    ];

    // --- Transcription (inside Realtime session) ---
    public const string DefaultTranscription = "gpt-4o-mini-transcribe";
    public static readonly string[] TranscriptionModels =
    [
        "gpt-4o-mini-transcribe",  // Budget, $1.25/1M
        "gpt-4o-transcribe",       // Best, $2.50/1M
    ];

    // --- Voice presets ---
    public const string DefaultVoice = "marin";
    public static readonly string[] Voices =
    [
        "alloy", "ash", "ballad", "coral", "echo",
        "fable", "marin", "sage", "shimmer", "verse",
    ];

    // --- Turn detection ---
    public static readonly string[] TurnDetectionModes =
    [
        "semantic_vad",  // Smart silence detection
        "server_vad",    // Basic server-side VAD
    ];

    // --- Noise reduction ---
    public static readonly string[] NoiseReductionModes =
    [
        "near_field",    // Close mic (glasses)
        "far_field",     // Distant mic
    ];

    // Returns display label for a model ID
    public static string Label(string modelId) => modelId switch
    {
        "gpt-realtime-1.5"       => "Realtime 1.5 (Premium)",
        "gpt-realtime-mini"      => "Realtime Mini (Budget)",
        "gpt-5.4"                => "GPT-5.4 (Flagship)",
        "gpt-5.4-mini"           => "GPT-5.4 Mini",
        "gpt-5.4-nano"           => "GPT-5.4 Nano (Cheapest)",
        "gpt-4o-mini-transcribe" => "GPT-4o Mini Transcribe",
        "gpt-4o-transcribe"      => "GPT-4o Transcribe (Best)",
        _ => modelId,
    };
}
```

### 8.3 — Fix Incorrect Model IDs

The previous bulk rename created two invalid model IDs. Fixes required:

| File | Current (WRONG) | Correct |
|------|-----------------|---------|
| `AppSettings.cs` → `RealtimeModel` | `gpt-5.4-realtime` | `gpt-realtime-1.5` |
| `RealtimeModels.cs` → `Model` | `gpt-5.4-realtime` | `gpt-realtime-1.5` |
| `RealtimeMessages.cs` → `InputAudioTranscription.Model` | `gpt-5.4-mini-transcribe` | `gpt-4o-mini-transcribe` |
| `RealtimeClient.cs` → hardcoded transcription | `gpt-5.4-mini-transcribe` | `gpt-4o-mini-transcribe` |
| `.env.example` → `AZURE_OPENAI_DEPLOYMENT` | `gpt-5.4-realtime` | `gpt-realtime-1.5` |
| Unit test assertion | `gpt-5.4-realtime` | `gpt-realtime-1.5` |

### 8.4 — SettingsPage UI

**Navigation:** Add `AppShell.xaml` with two tabs: "Home" (MainPage) and "Settings" (SettingsPage).

```xml
<!-- SettingsPage.xaml — key sections -->
<ContentPage Title="Settings">
  <ScrollView>
    <VerticalStackLayout Padding="16" Spacing="16">

      <!-- SECTION: Models -->
      <Label Text="Models" FontSize="18" FontAttributes="Bold" />

      <Label Text="Voice Model" />
      <Picker ItemsSource="{Binding RealtimeModelOptions}"
              SelectedItem="{Binding SelectedRealtimeModel}" />

      <Label Text="Chat Model" />
      <Picker ItemsSource="{Binding ChatModelOptions}"
              SelectedItem="{Binding SelectedChatModel}" />

      <Label Text="Vision Model" />
      <Picker ItemsSource="{Binding VisionModelOptions}"
              SelectedItem="{Binding SelectedVisionModel}" />

      <Label Text="Transcription Model" />
      <Picker ItemsSource="{Binding TranscriptionModelOptions}"
              SelectedItem="{Binding SelectedTranscriptionModel}" />

      <!-- SECTION: Voice -->
      <Label Text="Voice Settings" FontSize="18" FontAttributes="Bold" />

      <Label Text="Voice" />
      <Picker ItemsSource="{Binding VoiceOptions}"
              SelectedItem="{Binding SelectedVoice}" />

      <Label Text="Turn Detection" />
      <Picker ItemsSource="{Binding TurnDetectionOptions}"
              SelectedItem="{Binding SelectedTurnDetection}" />

      <Label Text="Noise Reduction" />
      <Picker ItemsSource="{Binding NoiseReductionOptions}"
              SelectedItem="{Binding SelectedNoiseReduction}" />

      <!-- SECTION: System Prompt -->
      <Label Text="System Instructions" FontSize="18" FontAttributes="Bold" />
      <Editor Text="{Binding SystemInstructions}"
              HeightRequest="100" />

      <!-- SECTION: API Key -->
      <Label Text="API Configuration" FontSize="18" FontAttributes="Bold" />
      <Label Text="API Key" />
      <HorizontalStackLayout Spacing="8">
        <Entry Text="{Binding ApiKeyDisplay}" IsPassword="True"
               IsReadOnly="True" HorizontalOptions="FillAndExpand" />
        <Button Text="Change" Command="{Binding ChangeApiKeyCommand}" />
        <Button Text="Clear" Command="{Binding ClearApiKeyCommand}" />
      </HorizontalStackLayout>

      <!-- SECTION: Debug -->
      <Label Text="Debug" FontSize="18" FontAttributes="Bold" />
      <HorizontalStackLayout Spacing="8">
        <Label Text="Debug Mode" VerticalOptions="Center" />
        <Switch IsToggled="{Binding DebugMode}" />
      </HorizontalStackLayout>
      <HorizontalStackLayout Spacing="8">
        <Label Text="Show Token Counts" VerticalOptions="Center" />
        <Switch IsToggled="{Binding ShowTokenCounts}" />
      </HorizontalStackLayout>
      <HorizontalStackLayout Spacing="8">
        <Label Text="Show Cost Estimate" VerticalOptions="Center" />
        <Switch IsToggled="{Binding ShowCostEstimate}" />
      </HorizontalStackLayout>

      <!-- Current session info (read-only) -->
      <Label Text="Session Info" FontSize="18" FontAttributes="Bold" />
      <Label Text="{Binding SessionInfoText}" FontSize="12" TextColor="Gray" />

    </VerticalStackLayout>
  </ScrollView>
</ContentPage>
```

### 8.5 — Debug Panel Enhancements

The existing `MainPage.xaml` already has a Debug Console (Row 3). Enhance it to show structured info when `DebugMode` is on:

```
[14:32:01] Connected: gpt-realtime-1.5 via wss://api.openai.com/v1/realtime
[14:32:01] Transcription: gpt-4o-mini-transcribe
[14:32:05] ◄ audio.delta (1,024 bytes)
[14:32:06] Session tokens — in: 1,247  out: 892  cost: ~$0.08
[14:32:10] ► speech detected → interruption → truncate
```

**Token tracking** lives in `AgentOrchestrator`:

```csharp
// New properties on AgentOrchestrator
public int SessionInputTokens { get; private set; }
public int SessionOutputTokens { get; private set; }
public int SessionAudioInputTokens { get; private set; }
public int SessionAudioOutputTokens { get; private set; }

// Updated on response.done event (already subscribed)
private void OnResponseDone(object? sender, RealtimeResponseInfo info)
{
    if (info.Usage is not null)
    {
        SessionInputTokens += info.Usage.InputTokens;
        SessionOutputTokens += info.Usage.OutputTokens;
        SessionAudioInputTokens += info.Usage.AudioInputTokens;
        SessionAudioOutputTokens += info.Usage.AudioOutputTokens;
    }
    // ... existing logic
}
```

**Cost estimation:**

```csharp
public decimal EstimatedCostUsd => CalculateCost(_settings.RealtimeModel);

private decimal CalculateCost(string model) => model switch
{
    "gpt-realtime-1.5" =>
        (SessionAudioInputTokens * 32m + SessionAudioOutputTokens * 64m
       + SessionInputTokens * 4m + SessionOutputTokens * 16m) / 1_000_000m,
    "gpt-realtime-mini" =>
        (SessionInputTokens * 0.6m + SessionOutputTokens * 2.4m) / 1_000_000m,
    _ => 0m,
};
```

Show in the MainPage header when `ShowCostEstimate` is enabled:

```
[Listening...] | Tokens: 2,139 in / 892 out | ~$0.08
```

### 8.6 — Wire Settings into Pipeline

`AppSettings` is currently a singleton created once in `MauiProgram.cs`. To support runtime changes:

1. **`ISettingsService` is the source of truth** — `Preferences` are persisted instantly on each property set.
2. **On session start** (`AgentOrchestrator.StartAsync`), read current settings into `AppSettings`:

```csharp
// In AgentOrchestrator.StartAsync()
public async Task StartAsync()
{
    // Refresh settings before connecting
    _settings.RealtimeModel = _settingsService.RealtimeModel;
    _settings.ChatModel = _settingsService.ChatModel;
    _settings.VisionModel = _settingsService.VisionModel;
    _settings.Voice = _settingsService.Voice;
    _settings.TurnDetection = _settingsService.TurnDetection;
    _settings.NoiseReduction = _settingsService.NoiseReduction;
    _settings.SystemInstructions = _settingsService.SystemInstructions;

    // ... existing connect logic
}
```

3. **Settings changes while running** require stop → restart. The Settings page shows a warning label: "Changes take effect on next session start."

### 8.7 — Shell Navigation

Replace the current single-page app with `AppShell`:

```xml
<!-- AppShell.xaml -->
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:local="clr-namespace:BodyCam"
       x:Class="BodyCam.AppShell">

    <TabBar>
        <ShellContent Title="Home" Icon="home.png"
                      ContentTemplate="{DataTemplate local:MainPage}" />
        <ShellContent Title="Settings" Icon="settings.png"
                      ContentTemplate="{DataTemplate local:SettingsPage}" />
    </TabBar>
</Shell>
```

`App.xaml.cs` changes from `MainPage = new MainPage(...)` to `MainPage = new AppShell()`.

---

## Data Flow

```
┌─────────────┐    write     ┌──────────────────┐    persist    ┌──────────────┐
│ SettingsPage │ ──────────► │  ISettingsService │ ───────────► │  Preferences │
│  (Pickers)  │              │ (get/set wrapper) │              │ (per-app KV) │
└─────────────┘              └──────────────────┘              └──────────────┘
                                     │ read
                                     ▼
                              ┌─────────────┐    StartAsync()    ┌──────────────┐
                              │  AppSettings │ ◄──────────────── │ Orchestrator │
                              │  (runtime)   │                   │              │
                              └─────────────┘                    └──────────────┘
                                     │
                          ┌──────────┼──────────┐
                          ▼          ▼          ▼
                   RealtimeClient  Agents    VisionAgent
```

**Secrets** (API key) flow separately through `IApiKeyService` → `SecureStorage`. Never stored in `Preferences`.

---

## DI Registration

```csharp
// MauiProgram.cs additions
builder.Services.AddSingleton<ISettingsService, SettingsService>();

// AppSettings is still singleton but now gets refreshed from ISettingsService
builder.Services.AddSingleton<AppSettings>(sp =>
{
    var ss = sp.GetRequiredService<ISettingsService>();
    var settings = new AppSettings
    {
        RealtimeModel = ss.RealtimeModel,
        ChatModel = ss.ChatModel,
        VisionModel = ss.VisionModel,
        Voice = ss.Voice,
        TurnDetection = ss.TurnDetection,
        NoiseReduction = ss.NoiseReduction,
        SystemInstructions = ss.SystemInstructions,
        Provider = Enum.TryParse<OpenAiProvider>(ss.Provider, true, out var p)
                   ? p : OpenAiProvider.OpenAi,
    };
    // .env overrides still apply for provider/Azure settings if not set
    return settings;
});

// New registrations
builder.Services.AddTransient<SettingsViewModel>();
builder.Services.AddTransient<SettingsPage>();
```

---

## Debug Mode Detail

When `DebugMode = true`:

| Feature | Description |
|---------|-------------|
| Debug Console visible | Row 3 on MainPage (already exists, but hide when debug off) |
| Verbose WebSocket logging | Log every event type + size |
| Token counter in header | "in: 1,247 / out: 892" next to status |
| Cost estimate in header | "~$0.08" next to token counter |
| Model badge | Show current realtime model name in header |
| Audio level meter | Mic input level indicator (future) |

When `DebugMode = false`:
- Debug Console row collapses (`IsVisible="False"`)
- Header shows only status text ("Listening...", "Ready", etc.)
- No token/cost overhead

---

## Tests (8.8)

| Test | Validates |
|------|-----------|
| `SettingsService_DefaultValues` | All defaults match `ModelOptions.DefaultXxx` |
| `SettingsService_RoundTrip` | Set → Get returns same value for each property |
| `SettingsService_PersistsAcrossInstances` | Create service, set value, create new service, read back |
| `ModelOptions_AllArraysNonEmpty` | Every model array has at least one entry |
| `ModelOptions_DefaultsExistInArrays` | Default value is contained in its array |
| `ModelOptions_Label_ReturnsNonEmpty` | Every model ID has a human-readable label |
| `AppSettings_RefreshFromSettings` | Orchestrator reads ISettingsService values into AppSettings |
| `SettingsViewModel_PickerChanges_PersistImmediately` | Changing picker writes to ISettingsService |

---

## Implementation Order

```
8.3 (fix model IDs)  ─► 8.2 (ModelOptions)  ─► 8.1 (ISettingsService)
                                                       │
                                                       ▼
                                              8.6 (wire into pipeline)
                                                       │
                                                       ▼
                                              8.7 (Shell navigation)
                                                       │
                                                       ▼
                                              8.4 (SettingsPage UI)
                                                       │
                                                       ▼
                                              8.5 (debug enhancements)
                                                       │
                                                       ▼
                                              8.8 (tests throughout)
```

Steps 8.3 → 8.2 → 8.1 can ship as a standalone PR (no UI changes, just plumbing). Steps 8.4–8.7 are the UI milestone.
