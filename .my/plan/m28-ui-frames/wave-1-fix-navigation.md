# Wave 1 — Fix Navigation for UI Tests

**Prerequisite:** None  
**Solves:** P2 (fragile toggle), P6 (UI test nav failures)  
**Target:** 56/56 UI tests green  

---

## Problem

SettingsPage.xaml is the OLD monolithic inline layout (Provider/Models/Azure all on one page). The code-behind (`SettingsPage.xaml.cs`) has navigation handlers (`OnConnectionTapped` etc.) that are **unused** because the XAML has no cards. The XAML references `OnOpenAiChecked` which doesn't exist in the code-behind → **build error**.

Additionally, `NavIcon` is a `Label` with `TapGestureRecognizer` — FlaUI can't Invoke it. UI tests use toggle-based navigation that fails due to unpredictable state.

---

## Changes

### 1. `AppShell.xaml` — NavIcon: Label → Button

```xml
<!-- DELETE -->
<Label x:Name="NavIcon" AutomationId="NavIcon" Grid.Column="3" Text="⚙" FontSize="20"
       VerticalOptions="Center"
       TextColor="{AppThemeBinding Light=#333333, Dark=#E0E0E0}">
    <Label.GestureRecognizers>
        <TapGestureRecognizer Tapped="OnNavIconTapped" />
    </Label.GestureRecognizers>
</Label>

<!-- REPLACE WITH -->
<Button x:Name="NavIcon" AutomationId="NavIcon" Grid.Column="3" Text="⚙" FontSize="20"
        Clicked="OnNavIconTapped"
        VerticalOptions="Center"
        BackgroundColor="Transparent"
        TextColor="{AppThemeBinding Light=#333333, Dark=#E0E0E0}"
        Padding="8,0"
        MinimumHeightRequest="36" MinimumWidthRequest="36" />
```

No changes to `AppShell.xaml.cs` — `OnNavIconTapped(object?, EventArgs)` is already compatible with `Clicked`.

### 2. `SettingsPage.xaml` — Replace inline settings with hub cards

Replace the entire `<ScrollView>` body. The old inline content is already in `ConnectionSettingsPage.xaml`.

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:BodyCam.ViewModels"
             x:Class="BodyCam.SettingsPage"
             x:DataType="vm:SettingsViewModel"
             Title="{Binding Title}">

    <ScrollView>
        <VerticalStackLayout Padding="16" Spacing="12">

            <Label Text="Changes take effect on next session start."
                   FontSize="12" TextColor="Gray" FontAttributes="Italic" />

            <Button AutomationId="ConnectionSettingsCard"
                    Text="🔗  Connection — Provider, API key, models"
                    Clicked="OnConnectionTapped"
                    BackgroundColor="{AppThemeBinding Light=#FFFFFF, Dark=#2A2A2A}"
                    TextColor="{AppThemeBinding Light=#333333, Dark=#E0E0E0}"
                    FontSize="14"
                    Padding="16"
                    CornerRadius="8"
                    HorizontalOptions="Fill"
                    HeightRequest="56" />

            <Button AutomationId="VoiceSettingsCard"
                    Text="🎙  Voice &amp; AI — Voice, turn detection, instructions"
                    Clicked="OnVoiceTapped"
                    BackgroundColor="{AppThemeBinding Light=#FFFFFF, Dark=#2A2A2A}"
                    TextColor="{AppThemeBinding Light=#333333, Dark=#E0E0E0}"
                    FontSize="14"
                    Padding="16"
                    CornerRadius="8"
                    HorizontalOptions="Fill"
                    HeightRequest="56" />

            <Button AutomationId="DeviceSettingsCard"
                    Text="📱  Devices — Camera, microphone, speaker"
                    Clicked="OnDevicesTapped"
                    BackgroundColor="{AppThemeBinding Light=#FFFFFF, Dark=#2A2A2A}"
                    TextColor="{AppThemeBinding Light=#333333, Dark=#E0E0E0}"
                    FontSize="14"
                    Padding="16"
                    CornerRadius="8"
                    HorizontalOptions="Fill"
                    HeightRequest="56" />

            <Button AutomationId="AdvancedSettingsCard"
                    Text="⚙  Advanced — Debug, diagnostics, tools"
                    Clicked="OnAdvancedTapped"
                    BackgroundColor="{AppThemeBinding Light=#FFFFFF, Dark=#2A2A2A}"
                    TextColor="{AppThemeBinding Light=#333333, Dark=#E0E0E0}"
                    FontSize="14"
                    Padding="16"
                    CornerRadius="8"
                    HorizontalOptions="Fill"
                    HeightRequest="56" />

        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

### 3. `SettingsPage.xaml.cs` — Remove dead OnOpenAiChecked/OnAzureChecked references

No changes needed — the code-behind already has the correct navigation handlers and does NOT have `OnOpenAiChecked`. The problem was the XAML referencing a non-existent handler. The new XAML fixes this.

### 4. `BodyCamFixture.cs` — Idempotent navigation

```csharp
// REPLACE NavigateToHome() and NavigateToSettings()

public void NavigateToHome()
{
    if (_mainPage.IsLoaded(2000)) return;

    ClickNavIcon();
    if (_mainPage.IsLoaded(3000)) return;

    // Toggled wrong direction — click again
    ClickNavIcon();
    _mainPage.WaitReady(10000);
}

public void NavigateToSettings()
{
    if (_settingsPage.IsLoaded(2000)) return;

    ClickNavIcon();
    if (_settingsPage.IsLoaded(3000)) return;

    ClickNavIcon();
    _settingsPage.WaitReady(10000);
}

public void NavigateToSettingsSubPage(Action clickCard, IPageObject subPage)
{
    NavigateToSettings();
    clickCard();
    subPage.WaitReady(10000);
}

private void ClickNavIcon()
{
    // NavIcon exists on all pages via Shell.TitleView — use raw AutomationId lookup
    var navIcon = Context.FindElement(By.AutomationId("NavIcon"));
    navIcon.Click();
}
```

### 5. `MainPage.cs` (PageObject) — Fix IsLoaded sentinel

```csharp
// CHANGE: use TranscriptTabButton (always-present Button) instead of SleepButton (inside Border)
public override bool IsLoaded(int? timeoutMs = null)
    => TranscriptTabButton.IsExists(timeoutMs);
```

### 6. `SettingsPage.cs` (PageObject) — Fix IsLoaded sentinel

```csharp
// CHANGE: ConnectionSettingsCard is now a Button — IsExists will work
// Keep the existing sentinel, it will now work because Button has UIA Invoke peer
public override bool IsLoaded(int? timeoutMs = null)
    => ConnectionSettingsCard.IsExists(timeoutMs);
```

### 7. Settings test constructors — Use NavigateToSettingsSubPage

Update all 6 settings test classes to use `NavigateToSettingsSubPage`:

**Before (all settings test classes):**
```csharp
public ApiKeyTests(BodyCamFixture fixture)
{
    _fixture = fixture;
    _fixture.NavigateToSettings();
    _fixture.SettingsPage.ConnectionSettingsCard.Click();
    _fixture.ConnectionSettingsPage.WaitReady(10000);
}
```

**After:**
```csharp
public ApiKeyTests(BodyCamFixture fixture)
{
    _fixture = fixture;
    _fixture.NavigateToSettingsSubPage(
        () => _fixture.SettingsPage.ConnectionSettingsCard.Click(),
        _fixture.ConnectionSettingsPage);
}
```

Apply to: `ApiKeyTests`, `ProviderTests`, `ModelSelectionTests`, `AzureSettingsTests`, `VoiceSettingsTests`, `DebugSettingsTests`.

---

## Files Changed

| File | Action |
|---|---|
| `src/BodyCam/AppShell.xaml` | NavIcon Label → Button |
| `src/BodyCam/SettingsPage.xaml` | Replace inline settings with 4 Button cards |
| `src/BodyCam.UITests/BodyCamFixture.cs` | Idempotent navigation + `NavigateToSettingsSubPage` |
| `src/BodyCam.UITests/Pages/MainPage.cs` | Fix `IsLoaded` sentinel |
| `src/BodyCam.UITests/Tests/SettingsPage/ApiKeyTests.cs` | Use `NavigateToSettingsSubPage` |
| `src/BodyCam.UITests/Tests/SettingsPage/ProviderTests.cs` | Use `NavigateToSettingsSubPage` |
| `src/BodyCam.UITests/Tests/SettingsPage/ModelSelectionTests.cs` | Use `NavigateToSettingsSubPage` |
| `src/BodyCam.UITests/Tests/SettingsPage/AzureSettingsTests.cs` | Use `NavigateToSettingsSubPage` |
| `src/BodyCam.UITests/Tests/SettingsPage/VoiceSettingsTests.cs` | Use `NavigateToSettingsSubPage` |
| `src/BodyCam.UITests/Tests/SettingsPage/DebugSettingsTests.cs` | Use `NavigateToSettingsSubPage` |

## Verification

```powershell
# Build app
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q

# Build tests
dotnet build src/BodyCam.UITests/BodyCam.UITests.csproj -v q

# Run each test class (one at a time, kill orphans between runs)
$classes = @(
    "StatusBarTests", "CameraViewTests", "DebugOverlayTests",
    "QuickActionTests", "TabSwitchingTests",
    "TabNavigationTests",
    "ApiKeyTests", "ProviderTests", "ModelSelectionTests",
    "AzureSettingsTests", "VoiceSettingsTests", "DebugSettingsTests"
)
foreach ($c in $classes) {
    Get-Process -Name "BodyCam*" -EA SilentlyContinue | Stop-Process -Force
    dotnet test src/BodyCam.UITests/BodyCam.UITests.csproj --no-build --filter "FullyQualifiedName~$c"
}
```

**Target:** 56/56 pass
