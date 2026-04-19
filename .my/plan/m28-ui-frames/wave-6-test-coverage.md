# Wave 6 — Test Coverage

**Prerequisite:** Wave 2 (ViewModels split — new VMs need tests)  
**Solves:** Missing unit tests for extracted ViewModels + UI test hardening  
**Target:** Full coverage for all new ViewModels, robust UI tests  

---

## Problem

Currently only `ToolSettingsSectionTests` exists in `BodyCam.Tests/ViewModels/`. None of the 3 original ViewModels have unit tests. After Wave 2 creates 4 new ViewModels (Connection, Voice, Device, Advanced), they need test coverage.

UI tests (56 total) should be audited for completeness after the refactoring waves.

---

## Part A — Unit Tests for Extracted Settings ViewModels

### Test Structure

```
src/BodyCam.Tests/ViewModels/
├── ToolSettingsSectionTests.cs        ← existing
├── Settings/
│   ├── ConnectionViewModelTests.cs    ← NEW
│   ├── VoiceViewModelTests.cs         ← NEW
│   ├── DeviceViewModelTests.cs        ← NEW
│   └── AdvancedViewModelTests.cs      ← NEW
```

All tests use xUnit + FluentAssertions + NSubstitute (per project conventions).

### `ConnectionViewModelTests.cs`

```csharp
using BodyCam.Services;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class ConnectionViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IApiKeyService _apiKeyService = Substitute.For<IApiKeyService>();

    private ConnectionViewModel CreateVm(Func<HttpClient>? httpFactory = null)
        => new(_settings, _apiKeyService, httpFactory);

    // --- Provider ---

    [Fact]
    public void SelectedProvider_SetToAzure_UpdatesIsAzure()
    {
        var vm = CreateVm();
        vm.SelectedProvider = OpenAiProvider.Azure;
        vm.IsAzure.Should().BeTrue();
        vm.IsOpenAi.Should().BeFalse();
    }

    [Fact]
    public void SelectedProvider_SetToOpenAi_UpdatesIsOpenAi()
    {
        _settings.Provider.Returns(OpenAiProvider.Azure);
        var vm = CreateVm();
        vm.SelectedProvider = OpenAiProvider.OpenAi;
        vm.IsOpenAi.Should().BeTrue();
        vm.IsAzure.Should().BeFalse();
    }

    // --- Model Selections ---

    [Fact]
    public void RealtimeModelOptions_ReturnsNonEmpty()
    {
        var vm = CreateVm();
        vm.RealtimeModelOptions.Should().NotBeEmpty();
    }

    [Fact]
    public void SelectedRealtimeModel_Set_PersistsToSettings()
    {
        var vm = CreateVm();
        var model = vm.RealtimeModelOptions[0];
        vm.SelectedRealtimeModel = model;
        _settings.RealtimeModel.Should().Be(model.Id);
    }

    // --- Azure Deployments ---

    [Fact]
    public void AzureEndpoint_SetValue_PersistsToSettings()
    {
        var vm = CreateVm();
        vm.AzureEndpoint = "https://test.cognitiveservices.azure.com";
        _settings.AzureEndpoint.Should().Be("https://test.cognitiveservices.azure.com");
    }

    [Fact]
    public void AzureEndpoint_SetEmpty_PersistsNull()
    {
        var vm = CreateVm();
        vm.AzureEndpoint = "";
        _settings.AzureEndpoint.Should().BeNull();
    }

    // --- API Key ---

    [Fact]
    public void ApiKeyDisplay_InitialLoad_ShowsMaskedKey()
    {
        _apiKeyService.GetApiKeyAsync().Returns("sk-proj-12345678");
        var vm = CreateVm();
        // Allow async load to complete
        Thread.Sleep(100);
        vm.ApiKeyDisplay.Should().Contain("****");
    }

    [Fact]
    public void IsKeyVisible_Toggle_ShowsFullKey()
    {
        _apiKeyService.GetApiKeyAsync().Returns("sk-proj-12345678");
        var vm = CreateVm();
        Thread.Sleep(100);
        vm.IsKeyVisible = true;
        vm.ApiKeyDisplay.Should().Be("sk-proj-12345678");
    }

    // --- Test Connection ---

    [Fact]
    public async Task TestConnectionCommand_NoApiKey_ShowsError()
    {
        _apiKeyService.GetApiKeyAsync().Returns((string?)null);
        var vm = CreateVm();
        await vm.TestConnectionCommand.ExecuteAsync(null);
        vm.ConnectionStatus.Should().Contain("No API key");
    }
}
```

### `VoiceViewModelTests.cs`

```csharp
using BodyCam.Services;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class VoiceViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    [Fact]
    public void SelectedVoice_Set_PersistsToSettings()
    {
        var vm = new VoiceViewModel(_settings);
        vm.SelectedVoice = "echo";
        _settings.Voice.Should().Be("echo");
    }

    [Fact]
    public void SelectedTurnDetection_Set_PersistsToSettings()
    {
        var vm = new VoiceViewModel(_settings);
        vm.SelectedTurnDetection = "server_vad";
        _settings.TurnDetection.Should().Be("server_vad");
    }

    [Fact]
    public void SystemInstructions_Set_PersistsToSettings()
    {
        var vm = new VoiceViewModel(_settings);
        vm.SystemInstructions = "You are a helpful assistant.";
        _settings.SystemInstructions.Should().Be("You are a helpful assistant.");
    }

    [Fact]
    public void VoiceOptions_ReturnsNonEmpty()
    {
        var vm = new VoiceViewModel(_settings);
        vm.VoiceOptions.Should().NotBeEmpty();
    }

    [Fact]
    public void SelectedVoice_RaisesPropertyChanged()
    {
        var vm = new VoiceViewModel(_settings);
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VoiceViewModel.SelectedVoice))
                raised = true;
        };
        vm.SelectedVoice = "nova";
        raised.Should().BeTrue();
    }
}
```

### `DeviceViewModelTests.cs`

```csharp
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class DeviceViewModelTests
{
    [Fact]
    public void CameraProviders_ReturnsCameraManagerProviders()
    {
        var camMgr = Substitute.For<CameraManager>();
        var audioIn = Substitute.For<AudioInputManager>();
        var audioOut = Substitute.For<AudioOutputManager>();

        var vm = new DeviceViewModel(camMgr, audioIn, audioOut);
        vm.CameraProviders.Should().BeSameAs(camMgr.Providers);
    }

    [Fact]
    public void AudioInputProviders_Changed_RaisesPropertyChanged()
    {
        var camMgr = Substitute.For<CameraManager>();
        var audioIn = Substitute.For<AudioInputManager>();
        var audioOut = Substitute.For<AudioOutputManager>();

        var vm = new DeviceViewModel(camMgr, audioIn, audioOut);
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeviceViewModel.AudioInputProviders))
                raised = true;
        };

        audioIn.ProvidersChanged += Raise.Event();
        raised.Should().BeTrue();
    }
}
```

### `AdvancedViewModelTests.cs`

```csharp
using BodyCam.Services;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class AdvancedViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    [Fact]
    public void DebugMode_Toggle_PersistsToSettings()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.DebugMode = true;
        _settings.DebugMode.Should().BeTrue();
    }

    [Fact]
    public void ShowTokenCounts_Toggle_PersistsToSettings()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.ShowTokenCounts = true;
        _settings.ShowTokenCounts.Should().BeTrue();
    }

    [Fact]
    public void ShowCostEstimate_Toggle_PersistsToSettings()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.ShowCostEstimate = true;
        _settings.ShowCostEstimate.Should().BeTrue();
    }

    [Fact]
    public void SendDiagnosticData_Toggle_PersistsToSettings()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.SendDiagnosticData = true;
        _settings.SendDiagnosticData.Should().BeTrue();
    }

    [Fact]
    public void ToolSettingsSections_NoTools_Empty()
    {
        var vm = new AdvancedViewModel(_settings, []);
        vm.ToolSettingsSections.Should().BeEmpty();
    }
}
```

---

## Part B — UI Test Hardening

### New UI test: Settings hub card existence

**New file:** `src/BodyCam.UITests/Tests/SettingsPage/SettingsHubTests.cs`

```csharp
namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class SettingsHubTests
{
    private readonly BodyCamFixture _fixture;

    public SettingsHubTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettings();
    }

    [Fact]
    public void ConnectionSettingsCard_Exists()
    {
        _fixture.SettingsPage.ConnectionSettingsCard.AssertExists();
    }

    [Fact]
    public void VoiceSettingsCard_Exists()
    {
        _fixture.SettingsPage.VoiceSettingsCard.AssertExists();
    }

    [Fact]
    public void DeviceSettingsCard_Exists()
    {
        _fixture.SettingsPage.DeviceSettingsCard.AssertExists();
    }

    [Fact]
    public void AdvancedSettingsCard_Exists()
    {
        _fixture.SettingsPage.AdvancedSettingsCard.AssertExists();
    }

    [Fact]
    public void ConnectionSettingsCard_Click_OpensConnectionPage()
    {
        _fixture.SettingsPage.ConnectionSettingsCard.Click();
        Assert.True(_fixture.ConnectionSettingsPage.IsLoaded(10000));
    }

    [Fact]
    public void VoiceSettingsCard_Click_OpensVoicePage()
    {
        _fixture.SettingsPage.VoiceSettingsCard.Click();
        Assert.True(_fixture.VoiceSettingsPage.IsLoaded(10000));
    }

    [Fact]
    public void DeviceSettingsCard_Click_OpensDevicePage()
    {
        _fixture.SettingsPage.DeviceSettingsCard.Click();
        Assert.True(_fixture.DeviceSettingsPage.IsLoaded(10000));
    }

    [Fact]
    public void AdvancedSettingsCard_Click_OpensAdvancedPage()
    {
        _fixture.SettingsPage.AdvancedSettingsCard.Click();
        Assert.True(_fixture.AdvancedSettingsPage.IsLoaded(10000));
    }
}
```

### New UI test: Device settings page

**New file:** `src/BodyCam.UITests/Tests/SettingsPage/DeviceSettingsTests.cs`

```csharp
namespace BodyCam.UITests.Tests.SettingsPage;

[Collection("BodyCam")]
[Trait("Category", "UITest")]
[Trait("Page", "SettingsPage")]
public class DeviceSettingsTests
{
    private readonly BodyCamFixture _fixture;
    private Pages.DeviceSettingsPage Page => _fixture.DeviceSettingsPage;

    public DeviceSettingsTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToSettingsSubPage(
            () => _fixture.SettingsPage.DeviceSettingsCard.Click(),
            _fixture.DeviceSettingsPage);
    }

    [Fact]
    public void CameraSourcePicker_Exists()
    {
        Page.CameraSourcePicker.AssertExists();
    }

    [Fact]
    public void AudioInputPicker_Exists()
    {
        Page.AudioInputPicker.AssertExists();
    }

    [Fact]
    public void AudioOutputPicker_Exists()
    {
        Page.AudioOutputPicker.AssertExists();
    }
}
```

### Audit: Missing coverage

| Area | Current Tests | Gap |
|---|---|---|
| Settings hub cards | None | Add `SettingsHubTests` (8 tests) |
| Device settings page | None | Add `DeviceSettingsTests` (3 tests) |
| System instructions editor | None | Add to VoiceSettingsTests |
| Snapshot overlay | Exists in PageObject, no test | Low priority (requires active session) |
| Setup page wizard | Exists in PageObject, no test | Low priority (dismissed by fixture) |

### Additional VoiceSettingsTests

```csharp
// Add to existing VoiceSettingsTests.cs
[Fact]
public void SystemInstructionsEditor_Exists()
{
    Page.SystemInstructionsEditor.AssertExists();
}
```

---

## Part C — Existing Unit Test Updates After Wave 2

### `ToolSettingsSectionTests.cs` — No changes

Tests `ToolSettingItem` directly, no ViewModel constructor dependency. Stays green.

### MainViewModel / SetupViewModel tests

These ViewModels are NOT split in Wave 2. If unit tests are added for them (not in scope for Wave 6), they test the existing API surface which is unchanged.

---

## Files Changed

| File | Action |
|---|---|
| `src/BodyCam.Tests/ViewModels/Settings/ConnectionViewModelTests.cs` | **Create** |
| `src/BodyCam.Tests/ViewModels/Settings/VoiceViewModelTests.cs` | **Create** |
| `src/BodyCam.Tests/ViewModels/Settings/DeviceViewModelTests.cs` | **Create** |
| `src/BodyCam.Tests/ViewModels/Settings/AdvancedViewModelTests.cs` | **Create** |
| `src/BodyCam.UITests/Tests/SettingsPage/SettingsHubTests.cs` | **Create** |
| `src/BodyCam.UITests/Tests/SettingsPage/DeviceSettingsTests.cs` | **Create** |
| `src/BodyCam.UITests/Tests/SettingsPage/VoiceSettingsTests.cs` | **Edit** — add SystemInstructions test |

## Verification

```powershell
# Unit tests
dotnet test src/BodyCam.Tests -v q --no-restore

# UI tests — new classes
Get-Process -Name "BodyCam*" -EA SilentlyContinue | Stop-Process -Force
dotnet test src/BodyCam.UITests --no-build --filter "FullyQualifiedName~SettingsHubTests"
Get-Process -Name "BodyCam*" -EA SilentlyContinue | Stop-Process -Force
dotnet test src/BodyCam.UITests --no-build --filter "FullyQualifiedName~DeviceSettingsTests"
```
