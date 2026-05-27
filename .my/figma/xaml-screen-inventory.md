# XAML Screen Inventory

**Generated:** 2026-05-21

---

## Navigation Structure

- **AppShell.xaml** — Shell with custom TitleView, single ShellContent routing to MainPage
- No flyout, no tabs at shell level. Settings navigation via code-behind push.

---

## Screen Summary

| Screen | File | ViewModel | Root | Purpose |
|--------|------|-----------|------|---------|
| Home | Pages/Main/MainPage.xaml | MainViewModel | Grid (4 rows) | Main tabbed hub (Transcript / Camera) |
| Setup | Pages/Setup/SetupPage.xaml | SetupViewModel | ScrollView → VStack | Permission onboarding wizard |
| Settings Hub | Pages/Settings/SettingsPage.xaml | SettingsViewModel | ScrollView → VStack | 4 settings category cards |
| Connection | Pages/Settings/ConnectionSettingsPage.xaml | ConnectionViewModel | ScrollView → VStack | Provider, API key, models |
| Voice | Pages/Settings/VoiceSettingsPage.xaml | VoiceViewModel | ScrollView → VStack | Voice, turn detection, system prompt |
| Devices | Pages/Settings/DeviceSettingsPage.xaml | DeviceViewModel | ScrollView → VStack | Camera, mic, speaker, glasses |
| Advanced | Pages/Settings/AdvancedSettingsPage.xaml | AdvancedViewModel | ScrollView → VStack | Debug, diagnostics, telemetry, tools |
| Glasses | Pages/GlassesPage.xaml | GlassesViewModel | ScrollView → VStack | BLE scan, connect, status |
| Media Gallery | Pages/MediaGalleryPage.xaml | MediaGalleryViewModel | Grid (3 rows) | 3-column grid with filters |
| Audio Player | Pages/AudioPlayerPage.xaml | (code-behind) | VStack | Simple file player |
| Image Viewer | Pages/ImageViewerPage.xaml | (code-behind) | ScrollView → Image | Fullscreen image |

---

## Detailed Per-Screen Breakdown

### Home — MainPage.xaml

**Layout:** Grid with 4 rows: StatusBar / Content / TabSelector / QuickActions

**Child views (all bind MainViewModel):**

| View | File | Purpose |
|------|------|---------|
| StatusBarView | Pages/Main/Views/StatusBarView.xaml | State selector (Off/On/Listening) + clear |
| TranscriptView | Pages/Main/Views/TranscriptView.xaml | CollectionView of TranscriptEntry items |
| CameraTabView | Pages/Main/Views/CameraTabView.xaml | CameraView + snapshot overlay |
| QuickActionsView | Pages/Main/Views/QuickActionsView.xaml | 3×2 grid of action buttons |
| ScanResultOverlay | Pages/Main/Views/ScanResultOverlay.xaml | Modal card with scan results + actions |
| DebugOverlayView | Pages/Main/Views/DebugOverlayView.xaml | AEC metrics + debug log |

**AutomationIds:** `TranscriptTabButton`, `CameraTabButton`, `OffButton`, `OnButton`, `ListeningButton`, `StateLabel`, `ClearButton`, `TranscriptList`, `CameraPreview`, `CameraPlaceholder`, `SnapshotImage`, `SnapshotCaption`, `DismissSnapshotButton`, `LookButton`, `ReadButton`, `FindButton`, `AskButton`, `PhotoButton`, `ScanButton`, `ScanResultContent`, `AecDebugLabel`, `DebugScroll`, `DebugLabel`

---

### Setup — SetupPage.xaml

**Layout:** ScrollView → centered VStack (MaxWidth 480). Single card with step icon, title, description, status, and conditional permission buttons.

**AutomationIds:** `SetupProgressLabel`, `SetupStepIcon`, `SetupStepTitle`, `SetupStepDescription`, `SetupStatusLabel`, `GrantPermissionButton`, `OpenSettingsButton`

**DataTriggers:** Status label changes color/text for granted/denied/skipped states.

---

### Settings Hub — SettingsPage.xaml

**Layout:** ScrollView → VStack with title + 4 SettingsCardView instances.

**Cards:**

| Card | Icon | AutomationId |
|------|------|--------------|
| Connection | 🔌 | `ConnectionSettingsCard` |
| Voice & AI | 🎙️ | `VoiceSettingsCard` |
| Devices | 📷 | `DeviceSettingsCard` |
| Advanced | ⚙️ | `AdvancedSettingsCard` |

---

### Connection — ConnectionSettingsPage.xaml

**Layout:** ScrollView → VStack (MaxWidth 500)

**Sections:**
1. Test Connection — button + status label
2. Provider — RadioButtons (OpenAI / Azure)
3. API Key — masked display, toggle/change/clear buttons
4. OpenAI Models (conditional) — 4 Pickers (Voice, Chat, Vision, Transcription) with status labels
5. Azure Deployments (conditional) — Endpoint, API Version, 3 deployment Entry fields

**AutomationIds:** `TestConnectionButton`, `ConnectionStatusLabel`, `ProviderOpenAiRadio`, `ProviderAzureRadio`, `ApiKeyDisplay`, `ToggleKeyVisibilityButton`, `ChangeApiKeyButton`, `ClearApiKeyButton`, `VoiceModelPicker`, `ChatModelPicker`, `VisionModelPicker`, `TranscriptionModelPicker`, `RealtimeStatusLabel`, `ChatStatusLabel`, `VisionStatusLabel`, `TranscriptionStatusLabel`, `AzureEndpointEntry`, `AzureApiVersionEntry`, `AzureRealtimeDeploymentEntry`, `AzureChatDeploymentEntry`, `AzureVisionDeploymentEntry`

---

### Voice — VoiceSettingsPage.xaml

**Layout:** ScrollView → VStack

**Controls:** VoicePicker, TurnDetectionPicker, NoiseReductionPicker, SystemInstructionsEditor (HeightRequest=150)

**AutomationIds:** `VoicePicker`, `TurnDetectionPicker`, `NoiseReductionPicker`, `SystemInstructionsEditor`

---

### Devices — DeviceSettingsPage.xaml

**Layout:** ScrollView → VStack (MaxWidth 500)

**Sections:**
1. Auto-switch notification (info banner)
2. Connect Device button
3. Connected Devices — glasses expandable panel + other devices CollectionView
4. Source Profile Picker
5. Camera Source (conditional) — Picker, Take Picture, Record Video, status, image preview
6. Microphone — Audio Input Picker, Test Recording button
7. Speaker — Audio Output Picker

**AutomationIds:** `AutoSwitchNotification`, `ConnectDeviceButton`, `GlassesInfoPanel`, `ConnectedDevicesPanel`, `SourceProfilePicker`, `CameraSourcePicker`, `TakePictureButton`, `RecordVideoButton`, `LastPictureImage`, `AudioInputPicker`, `TestRecordingButton`, `AudioOutputPicker`

---

### Advanced — AdvancedSettingsPage.xaml

**Layout:** ScrollView → VStack

**Sections:**
1. Debug — 3 switches (Debug Mode, Show Token Counts, Show Cost Estimate)
2. Diagnostics & Telemetry — switches + Entry fields (Azure Monitor, Sentry DSN, Usage Data)
3. Tool Settings — CollectionView of ToolSettingsSections with dynamic controls

**AutomationIds:** `DebugModeSwitch`, `ShowTokenCountsSwitch`, `ShowCostEstimateSwitch`, `SendDiagnosticDataSwitch`, `AzureMonitorConnectionStringEntry`, `SendCrashReportsSwitch`, `SentryDsnEntry`, `SendUsageDataSwitch`

---

### Glasses — GlassesPage.xaml

**Layout:** ScrollView → VStack (Padding 16)

**Sections (state-dependent visibility):**
1. Status frame — connection indicator dot + label
2. Scan UI (disconnected) — Scan button, scanning indicator, Stop button
3. Device list (disconnected) — CollectionView with Name/Address/RSSI
4. Connect button (disconnected)
5. Audio activation status (Windows, connecting)
6. Connected panel — battery bar, MAC/Firmware/Hardware grid, media counts
7. Disconnect button (connected)

---

### Media Gallery — MediaGalleryPage.xaml

**Layout:** Grid (3 rows): filter bar / progress / 3-column CollectionView

**Filter buttons:** All, Photos, Videos, Audio, Refresh

**AutomationIds:** `FilterAllButton`, `FilterPhotosButton`, `FilterVideosButton`, `FilterAudioButton`, `RefreshButton`, `ImportProgressBar`, `MediaCollectionView`, `EmptyViewLabel`

---

### Audio Player — AudioPlayerPage.xaml

**Layout:** VStack (centered). Filename label, 🎙 emoji, Play button.

**AutomationIds:** `FileNameLabel`, `PlayButton`

---

### Image Viewer — ImageViewerPage.xaml

**Layout:** ScrollView → Image (AspectFit).

**AutomationIds:** `ViewerImage`

---

## Reusable Views

| View | File | Properties |
|------|------|------------|
| SettingsCardView | Views/SettingsCardView.xaml | `CardAutomationId`, `Icon`, `CardTitle`, `Description`, `CardClicked` |

---

## App-Level Resources (App.xaml)

**Merged dictionaries:** Colors.xaml, Styles.xaml

**Converters:** `InvertBoolConverter`, `IsNotNullConverter`, `BoolToColorConverter`, `PercentConverter`
