# Plan: BodyCam UI Tests with Brinell

## Overview

Add automated UI tests for the BodyCam MAUI app using the Brinell UI test framework (FlaUI-based, Windows).

**Status**: Not started  
**Target**: Windows desktop (`net10.0-windows10.0.19041.0`)  
**Framework**: Brinell.Maui + Brinell.Maui.FlaUI (xUnit)

---

## Prerequisites

### 1. Add AutomationIds to BodyCam XAML

Neither `MainPage.xaml` nor `SettingsPage.xaml` currently have any `AutomationId` attributes. Brinell locates controls primarily by `AutomationId`, so every testable element needs one.

#### MainPage.xaml — Required AutomationIds

| Element | AutomationId | Type |
|---------|-------------|------|
| Status dot (Ellipse) | `StatusDot` | Ellipse |
| Sleep button | `SleepButton` | Button |
| Listen button | `ListenButton` | Button |
| Active button | `ActiveButton` | Button |
| Debug toggle | `DebugToggleButton` | Button |
| Clear button | `ClearButton` | Button |
| Transcript CollectionView | `TranscriptList` | CollectionView |
| Camera preview | `CameraPreview` | CameraView |
| Snapshot image | `SnapshotImage` | Image |
| Snapshot caption | `SnapshotCaption` | Label |
| Dismiss snapshot | `DismissSnapshotButton` | Button |
| Transcript tab button | `TranscriptTabButton` | Button |
| Camera tab button | `CameraTabButton` | Button |
| Look button | `LookButton` | Button |
| Read button | `ReadButton` | Button |
| Find button | `FindButton` | Button |
| Ask button | `AskButton` | Button |
| Photo button | `PhotoButton` | Button |
| Debug scroll | `DebugScroll` | ScrollView |
| Debug label | `DebugLabel` | Label |

#### SettingsPage.xaml — Required AutomationIds

| Element | AutomationId | Type |
|---------|-------------|------|
| Provider: OpenAI radio | `ProviderOpenAiRadio` | RadioButton |
| Provider: Azure radio | `ProviderAzureRadio` | RadioButton |
| Voice model picker | `VoiceModelPicker` | Picker |
| Chat model picker | `ChatModelPicker` | Picker |
| Vision model picker | `VisionModelPicker` | Picker |
| Transcription model picker | `TranscriptionModelPicker` | Picker |
| Azure endpoint entry | `AzureEndpointEntry` | Entry |
| Azure API version entry | `AzureApiVersionEntry` | Entry |
| Azure realtime deployment | `AzureRealtimeDeploymentEntry` | Entry |
| Azure chat deployment | `AzureChatDeploymentEntry` | Entry |
| Azure vision deployment | `AzureVisionDeploymentEntry` | Entry |
| Voice picker | `VoicePicker` | Picker |
| Turn detection picker | `TurnDetectionPicker` | Picker |
| Noise reduction picker | `NoiseReductionPicker` | Picker |
| System instructions editor | `SystemInstructionsEditor` | Editor |
| API key display | `ApiKeyDisplay` | Entry |
| Show/hide key button | `ToggleKeyVisibilityButton` | Button |
| Change API key button | `ChangeApiKeyButton` | Button |
| Clear API key button | `ClearApiKeyButton` | Button |
| Test connection button | `TestConnectionButton` | Button |
| Connection status label | `ConnectionStatusLabel` | Label |
| Realtime status label | `RealtimeStatusLabel` | Label |
| Chat status label | `ChatStatusLabel` | Label |
| Vision status label | `VisionStatusLabel` | Label |
| Debug mode switch | `DebugModeSwitch` | Switch |
| Show token counts switch | `ShowTokenCountsSwitch` | Switch |
| Show cost estimate switch | `ShowCostEstimateSwitch` | Switch |

#### AppShell.xaml — Required AutomationIds

| Element | AutomationId | Type |
|---------|-------------|------|
| Home tab | `HomeTab` | ShellContent/Tab |
| Settings tab | `SettingsTab` | ShellContent/Tab |

---

## Project Structure

```
src/
  BodyCam.UITests/
    BodyCam.UITests.csproj
    GlobalUsings.cs
    BodyCamFixture.cs          # Test fixture (launches app, creates pages)
    BodyCamCollection.cs       # xUnit collection definition
    TestConstants.cs           # Timeouts, app path
    Pages/
      MainPage.cs              # Page object for MainPage
      SettingsPage.cs          # Page object for SettingsPage
    Tests/
      MainPage/
        StatusBarTests.cs      # State switching, status dot
        TabSwitchingTests.cs   # Transcript/Camera tab toggle
        QuickActionTests.cs    # Look/Read/Find/Ask/Photo buttons
        DebugOverlayTests.cs   # Debug toggle, clear
        TranscriptTests.cs     # Transcript list behavior
      SettingsPage/
        ProviderTests.cs       # OpenAI/Azure radio switching
        ModelSelectionTests.cs # Picker selections
        AzureSettingsTests.cs  # Azure fields visibility/input
        VoiceSettingsTests.cs  # Voice/turn detection/noise pickers
        ApiKeyTests.cs         # API key show/hide/change/clear
        DebugSettingsTests.cs  # Debug switches
        SystemInstructionsTests.cs # Editor input
      Navigation/
        TabNavigationTests.cs  # Home/Settings tab switching
```

---

## Phase 1: Infrastructure Setup

### 1.1 Create `BodyCam.UITests` Project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Brinell\srcnew\Brinell.Core\Brinell.Core.csproj" />
    <ProjectReference Include="..\..\Brinell\srcnew\Brinell.Maui\Brinell.Maui.csproj" />
    <ProjectReference Include="..\..\Brinell\srcnew\Brinell.Maui.FlaUI\Brinell.Maui.FlaUI.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
</Project>
```

### 1.2 Add AutomationIds to BodyCam XAML

Add `AutomationId` attributes to all interactive controls in `MainPage.xaml`, `SettingsPage.xaml`, and `AppShell.xaml` per the tables above.

### 1.3 Create Fixture & Collection

```csharp
// BodyCamCollection.cs
[CollectionDefinition("BodyCam")]
public class BodyCamCollection : ICollectionFixture<BodyCamFixture> { }

// BodyCamFixture.cs
public class BodyCamFixture : MauiTestFixtureBase
{
    public MainPage MainPage { get; }
    public SettingsPage SettingsPage { get; }

    public BodyCamFixture()
    {
        MainPage = new MainPage(Context);
        SettingsPage = new SettingsPage(Context);
    }

    protected override string GetDefaultAppPath(string platform)
        => platform switch
        {
            "windows" => @"path\to\BodyCam.exe",
            _ => throw new NotSupportedException($"Platform {platform} not supported")
        };

    public void NavigateToHome() { /* tab navigation logic */ }
    public void NavigateToSettings() { /* tab navigation logic */ }
}
```

---

## Phase 2: Page Objects

### 2.1 MainPage Page Object

```csharp
public class MainPage : PageObjectBase<MainPage>
{
    public MainPage(IMauiTestContext context) : base(context) { }
    public override string Name => "MainPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => SleepButton.IsExists() == true;

    // Status bar
    public Button<MainPage> SleepButton => Button("SleepButton");
    public Button<MainPage> ListenButton => Button("ListenButton");
    public Button<MainPage> ActiveButton => Button("ActiveButton");
    public Button<MainPage> DebugToggleButton => Button("DebugToggleButton");
    public Button<MainPage> ClearButton => Button("ClearButton");

    // Tab switcher
    public Button<MainPage> TranscriptTabButton => Button("TranscriptTabButton");
    public Button<MainPage> CameraTabButton => Button("CameraTabButton");

    // Quick actions
    public Button<MainPage> LookButton => Button("LookButton");
    public Button<MainPage> ReadButton => Button("ReadButton");
    public Button<MainPage> FindButton => Button("FindButton");
    public Button<MainPage> AskButton => Button("AskButton");
    public Button<MainPage> PhotoButton => Button("PhotoButton");

    // Debug overlay
    public Label<MainPage> DebugLabel => Label("DebugLabel");

    // Snapshot overlay
    public Image<MainPage> SnapshotImage => Image("SnapshotImage");
    public Label<MainPage> SnapshotCaption => Label("SnapshotCaption");
    public Button<MainPage> DismissSnapshotButton => Button("DismissSnapshotButton");
}
```

### 2.2 SettingsPage Page Object

```csharp
public class SettingsPage : PageObjectBase<SettingsPage>
{
    public SettingsPage(IMauiTestContext context) : base(context) { }
    public override string Name => "SettingsPage";

    public override bool IsLoaded(int? timeoutMs = null)
        => ProviderOpenAiRadio.IsExists() == true;

    // Provider selection
    public RadioButton<SettingsPage> ProviderOpenAiRadio => RadioButton("ProviderOpenAiRadio");
    public RadioButton<SettingsPage> ProviderAzureRadio => RadioButton("ProviderAzureRadio");

    // Model pickers (OpenAI)
    public Picker<SettingsPage> VoiceModelPicker => Picker("VoiceModelPicker");
    public Picker<SettingsPage> ChatModelPicker => Picker("ChatModelPicker");
    public Picker<SettingsPage> VisionModelPicker => Picker("VisionModelPicker");
    public Picker<SettingsPage> TranscriptionModelPicker => Picker("TranscriptionModelPicker");

    // Azure settings
    public Entry<SettingsPage> AzureEndpointEntry => Entry("AzureEndpointEntry");
    public Entry<SettingsPage> AzureApiVersionEntry => Entry("AzureApiVersionEntry");
    public Entry<SettingsPage> AzureRealtimeDeploymentEntry => Entry("AzureRealtimeDeploymentEntry");
    public Entry<SettingsPage> AzureChatDeploymentEntry => Entry("AzureChatDeploymentEntry");
    public Entry<SettingsPage> AzureVisionDeploymentEntry => Entry("AzureVisionDeploymentEntry");

    // Voice settings
    public Picker<SettingsPage> VoicePicker => Picker("VoicePicker");
    public Picker<SettingsPage> TurnDetectionPicker => Picker("TurnDetectionPicker");
    public Picker<SettingsPage> NoiseReductionPicker => Picker("NoiseReductionPicker");

    // System instructions
    public Editor<SettingsPage> SystemInstructionsEditor => Editor("SystemInstructionsEditor");

    // API key
    public Entry<SettingsPage> ApiKeyDisplay => Entry("ApiKeyDisplay");
    public Button<SettingsPage> ToggleKeyVisibilityButton => Button("ToggleKeyVisibilityButton");
    public Button<SettingsPage> ChangeApiKeyButton => Button("ChangeApiKeyButton");
    public Button<SettingsPage> ClearApiKeyButton => Button("ClearApiKeyButton");

    // Connection test
    public Button<SettingsPage> TestConnectionButton => Button("TestConnectionButton");
    public Label<SettingsPage> ConnectionStatusLabel => Label("ConnectionStatusLabel");

    // Debug switches
    public Switch<SettingsPage> DebugModeSwitch => Switch("DebugModeSwitch");
    public Switch<SettingsPage> ShowTokenCountsSwitch => Switch("ShowTokenCountsSwitch");
    public Switch<SettingsPage> ShowCostEstimateSwitch => Switch("ShowCostEstimateSwitch");
}
```

---

## Phase 3: Test Cases

### 3.1 Navigation Tests

| Test | Description |
|------|-------------|
| `NavigateToSettings_ShowsSettingsPage` | Click Settings tab → SettingsPage loads |
| `NavigateToHome_ShowsMainPage` | Click Home tab → MainPage loads |
| `TabSwitching_RoundTrip` | Home → Settings → Home, all pages load correctly |

### 3.2 MainPage — Status Bar Tests

| Test | Description |
|------|-------------|
| `SleepButton_IsVisibleOnLoad` | Sleep button exists and is clickable |
| `ListenButton_IsVisibleOnLoad` | Listen button exists and is clickable |
| `ActiveButton_IsVisibleOnLoad` | Active button exists and is clickable |
| `SleepButton_Click_SetsState` | Click Sleep → state changes (check visual feedback) |
| `ListenButton_Click_SetsState` | Click Listen → state changes |
| `ActiveButton_Click_SetsState` | Click Active → state changes |
| `DebugToggle_Click_ShowsDebugOverlay` | Click debug toggle → debug area becomes visible |
| `ClearButton_Click_ClearsTranscript` | Click clear → transcript list is empty |

### 3.3 MainPage — Tab Switching Tests

| Test | Description |
|------|-------------|
| `TranscriptTab_IsDefaultTab` | Transcript tab is selected on load |
| `CameraTab_Click_SwitchesToCameraView` | Click Camera tab → camera area visible |
| `TranscriptTab_Click_SwitchesBackToTranscript` | Click Transcript tab → transcript list visible |

### 3.4 MainPage — Quick Action Tests

| Test | Description |
|------|-------------|
| `LookButton_IsVisible` | Look button exists |
| `ReadButton_IsVisible` | Read button exists |
| `FindButton_IsVisible` | Find button exists |
| `AskButton_IsVisible` | Ask button exists |
| `PhotoButton_IsVisible` | Photo button exists |
| `AllQuickActions_AreClickable` | All 5 buttons are enabled and clickable |

### 3.5 SettingsPage — Provider Tests

| Test | Description |
|------|-------------|
| `OpenAiRadio_IsSelectedByDefault` | OpenAI radio is selected on load |
| `SelectAzure_ShowsAzureFields` | Select Azure → Azure entries become visible |
| `SelectAzure_HidesOpenAiModels` | Select Azure → model pickers hidden |
| `SelectOpenAi_ShowsModelPickers` | Select OpenAI → model pickers visible |
| `SelectOpenAi_HidesAzureFields` | Select OpenAI → Azure entries hidden |

### 3.6 SettingsPage — Model Selection Tests

| Test | Description |
|------|-------------|
| `VoiceModelPicker_HasOptions` | Voice model picker has selectable items |
| `ChatModelPicker_HasOptions` | Chat model picker has selectable items |
| `VisionModelPicker_HasOptions` | Vision model picker has selectable items |
| `TranscriptionModelPicker_HasOptions` | Transcription picker has items |
| `VoiceModelPicker_SelectModel_Persists` | Select a model → value stays after tab switch |

### 3.7 SettingsPage — Azure Settings Tests

| Test | Description |
|------|-------------|
| `AzureEndpoint_EnterUrl_SetsValue` | Type URL → entry displays it |
| `AzureApiVersion_EnterVersion_SetsValue` | Type version → entry displays it |
| `AzureRealtimeDeployment_EnterName_SetsValue` | Type name → entry displays it |
| `AzureChatDeployment_EnterName_SetsValue` | Type name → entry displays it |
| `AzureVisionDeployment_EnterName_SetsValue` | Type name → entry displays it |
| `AzureFields_NotVisible_WhenOpenAiSelected` | Switch to OpenAI → fields hidden |

### 3.8 SettingsPage — Voice Settings Tests

| Test | Description |
|------|-------------|
| `VoicePicker_HasOptions` | Voice picker has selectable items |
| `TurnDetectionPicker_HasOptions` | Turn detection picker has items |
| `NoiseReductionPicker_HasOptions` | Noise reduction picker has items |
| `VoicePicker_SelectVoice_Persists` | Select voice → value persists |

### 3.9 SettingsPage — API Key Tests

| Test | Description |
|------|-------------|
| `ApiKeyDisplay_IsMaskedByDefault` | Key display shows masked text |
| `ToggleKeyVisibility_ShowsKey` | Click show → key text visible (button text changes) |
| `ToggleKeyVisibility_HidesKey` | Click hide → key masked again |
| `ClearApiKey_ClearsDisplay` | Click clear → key display empty |

### 3.10 SettingsPage — Debug Settings Tests

| Test | Description |
|------|-------------|
| `DebugModeSwitch_CanToggle` | Turn on/off works |
| `ShowTokenCountsSwitch_CanToggle` | Turn on/off works |
| `ShowCostEstimateSwitch_CanToggle` | Turn on/off works |
| `DebugModeSwitch_DefaultState` | Check default on/off state |

### 3.11 SettingsPage — System Instructions Tests

| Test | Description |
|------|-------------|
| `SystemInstructionsEditor_EnterText_SetsValue` | Type instructions → editor displays them |
| `SystemInstructionsEditor_Clear_EmptiesEditor` | Clear → editor is empty |
| `SystemInstructionsEditor_MultiLine_Supported` | Enter multi-line text → preserved |

---

## Phase 4: Execution & CI

### 4.1 Running Tests Locally

```powershell
# Build BodyCam for Windows (unpackaged)
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None

# Set environment variables
$env:APPIUM_PLATFORM = "windows"
$env:APPIUM_APP_PATH = "src\BodyCam\bin\Debug\net10.0-windows10.0.19041.0\BodyCam.exe"

# Run tests (one class at a time to avoid orphan processes)
dotnet test src/BodyCam.UITests --filter "ClassName~TabNavigationTests"
dotnet test src/BodyCam.UITests --filter "ClassName~StatusBarTests"
```

### 4.2 Test Ordering Considerations

- All tests share a single app instance via `ICollectionFixture<BodyCamFixture>`
- Each test class navigates to its target page in the constructor
- Tests must not depend on other tests' side effects
- API key tests may need special handling (avoid clearing a real key during testing)

---

## Scope Boundaries

### In Scope
- UI element presence and visibility assertions
- Navigation between tabs
- Control state changes (toggles, radio buttons, text input)
- Provider switching (OpenAI ↔ Azure) and conditional visibility
- Settings persistence (enter value → navigate away → come back → value persists)

### Out of Scope (requires real API keys / hardware)
- Test Connection functionality (requires valid API keys)
- Camera preview (requires hardware camera)
- Audio input/output (requires microphone/speaker)
- Transcript streaming from real API
- Tool execution (vision, OCR, navigation)
- Wake word detection

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| FlaUI cannot traverse TabbedPage content | BodyCam uses Shell TabBar, not TabbedPage — should work. Verify early. |
| CameraView (CommunityToolkit) not in UIA tree | Skip camera tests; test camera tab button visibility only |
| Picker dropdown interaction flaky on WinUI3 | Use `IExpandCollapsePatternElement` from Brinell.Maui.FlaUI |
| App startup slow (API initialization) | Use generous `PageLoad` timeout (15-30s) |
| API key tests destructive | Create isolated test API key or skip destructive key tests in CI |
| `IsVisible="False"` removes elements from tree | Test conditional visibility with `AssertExists()`/`AssertNotExists()` instead of `AssertVisible()` |

---

## Implementation Order

1. **Phase 1**: Infrastructure — create project, add AutomationIds, fixture setup
2. **Phase 2**: Page objects — MainPage and SettingsPage page objects
3. **Phase 3a**: Navigation + MainPage tests (simplest, validates infrastructure works)
4. **Phase 3b**: SettingsPage tests (more controls, provider switching)
5. **Phase 4**: CI integration and test runner scripts
