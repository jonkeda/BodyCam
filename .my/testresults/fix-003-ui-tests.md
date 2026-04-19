# FIX-003 — UI Tests (BodyCam.UITests)

**Status:** Planned  
**Baseline:** 12 pass / 44 fail / 56 total

---

## Root Cause Analysis

### Bug 1 — Navigation helpers assume toggle state

`BodyCamFixture.NavigateToHome()` always clicks `_settingsPage.NavIcon`, assuming we're on the SettingsPage. But `NavIcon` is a toggle — if we're already on MainPage, clicking it navigates **to** SettingsPage, then `WaitReady(10000)` waits for MainPage which never comes → timeout.

Same problem with `NavigateToSettings()` — always clicks `_mainPage.NavIcon`, assuming we're on MainPage.

The result: test ordering within a shared fixture causes unpredictable navigation failures. Some tests pass because they happen to run when the fixture is in the expected state; others fail because a previous test left the app on a different page.

**Affected:** All 44 failures ultimately trace to navigation state confusion.

### Bug 2 — Settings sub-page navigation uses Shell.GoToAsync (pushed routes)

Settings sub-pages (ConnectionSettingsPage, VoiceSettingsPage, etc.) are pushed via `Shell.Current.GoToAsync(nameof(SubPage))` from the SettingsPage hub. This creates a Shell navigation stack:

```
//SettingsPage → ConnectionSettingsPage (pushed)
```

After testing a sub-page, clicking `NavIcon` calls `GoToAsync("//MainPage")` which pops the stack and navigates to the root route. But the fixture never pops back to the SettingsPage hub — subsequent test classes that also call `NavigateToSettings()` + `Card.Click()` may be clicking on a page that's already pushed.

**Affected:** All SettingsPage test classes (32 tests).

### Bug 3 — `MainPage.IsLoaded()` sentinel is fragile

`IsLoaded()` checks `SleepButton.IsExists()`. The SleepButton is inside a `Border > Grid` container. On WinUI3, MAUI `Border` sometimes doesn't create a UIA peer that propagates child discovery until layout completes. During fast test transitions, the SleepButton may not be in the UIA tree yet even though the page is visible.

**Affected:** StatusBarTests (SleepButton, ActiveButton), CameraViewTests, TabSwitchingTests.

### Bug 4 — `SettingsPage.IsLoaded()` sentinel: Frame with TapGestureRecognizer

`IsLoaded()` checks `ConnectionSettingsCard.IsExists()`. This is a `Frame` with a `TapGestureRecognizer` — FlaUI may not discover it by AutomationId because MAUI `Frame` creates a `GroupAutomationPeer` that doesn't always propagate the AutomationId to the UIA element.

**Affected:** All SettingsPage navigation paths.

---

## Fix Plan

### Fix 1 — Idempotent navigation helpers in `BodyCamFixture`

Replace the toggle-based navigation with explicit state detection:

```csharp
public void NavigateToHome()
{
    if (MainPage.IsLoaded(2000)) return; // already there

    // If on a settings sub-page, go back to settings hub first
    if (!SettingsPage.IsLoaded(1000))
        Shell.Current is not accessible from test — use NavIcon
    
    // Click NavIcon — if on settings, this goes to MainPage
    // If we're somehow stuck, try clicking NavIcon twice
    SettingsPage.NavIcon.ClickIfExists();
    if (!MainPage.WaitReady(5000))
    {
        // We were on MainPage and went to Settings — click again
        MainPage.NavIcon.ClickIfExists();
        MainPage.WaitReady(10000);
    }
}
```

Better approach — use the Shell tab items directly:

```csharp
public void NavigateToHome()
{
    if (MainPage.IsLoaded(2000)) return;
    // Use the NavIcon which is always visible in the title bar
    var navIcon = Context.Driver.FindElement(By.AutomationId("NavIcon"));
    navIcon.Click();
    if (!MainPage.IsLoaded(3000))
    {
        // We toggled wrong direction — click again
        navIcon = Context.Driver.FindElement(By.AutomationId("NavIcon"));
        navIcon.Click();
    }
    MainPage.WaitReady(10000);
}

public void NavigateToSettings()
{
    if (SettingsPage.IsLoaded(2000)) return;
    var navIcon = Context.Driver.FindElement(By.AutomationId("NavIcon"));
    navIcon.Click();
    if (!SettingsPage.IsLoaded(3000))
    {
        navIcon = Context.Driver.FindElement(By.AutomationId("NavIcon"));
        navIcon.Click();
    }
    SettingsPage.WaitReady(10000);
}
```

### Fix 2 — Settings sub-page back-navigation

Add a `NavigateBackToSettingsHub()` helper that pops Shell nav. Each settings sub-page test constructor should call this after its tests complete (or use `IDisposable` / `DisposeAsync`).

Alternative: Add a `BackButton` control to each settings sub-page PageObject and click it in the test teardown.

The MAUI Shell on WinUI3 shows a back button (←) in the navigation bar when a route is pushed. Add an `AutomationId` to capture it, or find it by UIA name.

### Fix 3 — Better `IsLoaded` sentinels

**MainPage:** Use `TranscriptTabButton` or `DebugToggleButton` as the sentinel — these are top-level Buttons that always exist and don't depend on Border/Grid container layout.

```csharp
public override bool IsLoaded(int? timeoutMs = null)
    => TranscriptTabButton.IsExists(timeoutMs);
```

**SettingsPage:** Replace the Frame-based sentinel with a Label that has an AutomationId, or add a dedicated sentinel element to the XAML.

### Fix 4 — Page object corrections using proper control types

**NavIcon** — Currently `Button<MainPage>` / `Button<SettingsPage>` but XAML is a `Label` with `TapGestureRecognizer`. FlaUI's coordinate-click fallback works, but semantically this should be `Label<T>` with an explicit click. Or better: change the XAML to use `ImageButton` instead of `Label + TapGestureRecognizer`.

**ConnectionSettingsCard etc.** — Currently `Button<SettingsPage>` but XAML is `Frame + TapGestureRecognizer`. Same issue. Consider changing XAML to use a `Button` styled as a card, or accepting the coordinate-click approach.

---

## Detailed Changes

### 1. `AppShell.xaml` — Change NavIcon from Label to ImageButton

```xml
<!-- Old -->
<Label x:Name="NavIcon" AutomationId="NavIcon" Grid.Column="3" Text="⚙" FontSize="20"
       VerticalOptions="Center"
       TextColor="{AppThemeBinding Light=#333333, Dark=#E0E0E0}">
    <Label.GestureRecognizers>
        <TapGestureRecognizer Tapped="OnNavIconTapped" />
    </Label.GestureRecognizers>
</Label>

<!-- New -->
<Button x:Name="NavIcon" AutomationId="NavIcon" Grid.Column="3" Text="⚙" FontSize="20"
        Clicked="OnNavIconTapped"
        VerticalOptions="Center"
        BackgroundColor="Transparent"
        TextColor="{AppThemeBinding Light=#333333, Dark=#E0E0E0}"
        Padding="8,0"
        MinimumHeightRequest="36" MinimumWidthRequest="36"
        SemanticProperties.Description="Navigation toggle"
        SemanticProperties.Hint="Switches between home and settings" />
```

Update `AppShell.xaml.cs`:
```csharp
// OnNavIconTapped already takes (object?, EventArgs) — compatible with Clicked handler
NavIcon.Text = _onSettings ? "✕" : "⚙"; // still works — Button.Text
```

### 2. `SettingsPage.xaml` — Change card Frames to Buttons

Replace each `Frame + TapGestureRecognizer` card with a styled `Button` that FlaUI can Invoke:

```xml
<!-- Old -->
<Frame AutomationId="ConnectionSettingsCard" ...>
    <Frame.GestureRecognizers>
        <TapGestureRecognizer Tapped="OnConnectionTapped" />
    </Frame.GestureRecognizers>
    <Grid>...</Grid>
</Frame>

<!-- New — use Button with ContentLayout or keep visual structure -->
<Button AutomationId="ConnectionSettingsCard"
        Text="🔗 Connection — Provider, API key, models"
        Clicked="OnConnectionTapped"
        ... card styles ... />
```

**Or** (preserving card visual structure): wrap the Frame in a container and add an invisible overlay Button:

```xml
<Grid>
    <Frame ...> <!-- visual card --> </Frame>
    <Button AutomationId="ConnectionSettingsCard"
            Clicked="OnConnectionTapped"
            BackgroundColor="Transparent"
            Opacity="0.01" />
</Grid>
```

### 3. `BodyCamFixture.cs` — Idempotent navigation

```csharp
public void NavigateToHome()
{
    if (_mainPage.IsLoaded(2000)) return;
    
    // NavIcon toggles between MainPage and SettingsPage.
    // Click it; if we end up on the wrong page, click again.
    ClickNavIcon();
    if (_mainPage.IsLoaded(3000)) return;
    
    // We went the wrong direction — click again
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

public void NavigateToSettingsSubPage(Action clickCard, PageObjectBase subPage)
{
    NavigateToSettings();
    clickCard();
    subPage.WaitReady(10000);
}

private void ClickNavIcon()
{
    // NavIcon exists on both pages — find it regardless of current page
    var nav = _mainPage.NavIcon.IsExists() ? _mainPage.NavIcon : _settingsPage.NavIcon;
    nav.Click();
}
```

### 4. `MainPage.cs` (PageObject) — Fix IsLoaded sentinel

```csharp
public override bool IsLoaded(int? timeoutMs = null)
    => TranscriptTabButton.IsExists(timeoutMs);
```

### 5. `SettingsPage.cs` (PageObject) — Fix card control types

If keeping Frame XAML, change page object to use `Label` for click (coordinate-based):
```csharp
// Frame doesn't support Invoke pattern — use Control for generic element
public Control<SettingsPage> ConnectionSettingsCard => Control("ConnectionSettingsCard");
```

Or if XAML is changed to use Buttons, keep `Button<SettingsPage>`.

### 6. Settings sub-page PageObjects — Add back navigation

Each sub-page needs a way to go back to the settings hub. MAUI Shell on WinUI3 provides a back button. Expose it:

```csharp
// In each settings sub-page PageObject
public Button<T> BackButton => Button("BackButton");
// Or find Shell's back button by UIA navigation
```

Add `AutomationId="BackButton"` to a "← Back" button in each sub-page XAML, or use Shell's built-in back button.

### 7. Settings test constructors — Use NavigateToSettingsSubPage

```csharp
// Old
public ApiKeyTests(BodyCamFixture fixture)
{
    _fixture = fixture;
    _fixture.NavigateToSettings();
    _fixture.SettingsPage.ConnectionSettingsCard.Click();
    _fixture.ConnectionSettingsPage.WaitReady(10000);
}

// New
public ApiKeyTests(BodyCamFixture fixture)
{
    _fixture = fixture;
    _fixture.NavigateToSettingsSubPage(
        () => _fixture.SettingsPage.ConnectionSettingsCard.Click(),
        _fixture.ConnectionSettingsPage);
}
```

---

## Verification

After all changes:
```powershell
# Build app
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q

# Build tests
dotnet build src/BodyCam.UITests/BodyCam.UITests.csproj -v q

# Run one class at a time (per uitests memory note)
$classes = @(
    "StatusBarTests", "CameraViewTests", "DebugOverlayTests",
    "QuickActionTests", "TabSwitchingTests",
    "TabNavigationTests",
    "ApiKeyTests", "ProviderTests", "ModelSelectionTests",
    "AzureSettingsTests", "VoiceSettingsTests", "DebugSettingsTests"
)
foreach ($c in $classes) {
    Get-Process -Name "BodyCam*" -EA SilentlyContinue | Stop-Process -Force
    dotnet test src/BodyCam.UITests/BodyCam.UITests.csproj -v q --no-restore --filter "FullyQualifiedName~$c"
}
```

Target: 56/56 pass (or 52+ with model-dependent tolerance).

---

## Failure Catalog

### MainPage (12 failures)

| Test | Error | Root Cause |
|---|---|---|
| `StatusBarTests.SleepButton_Exists` | Element not found: `SleepButton` | Navigation state — `NavigateToHome()` navigated away |
| `StatusBarTests.ActiveButton_Exists` | Element not found: `ActiveButton` | Same |
| `StatusBarTests.SleepButton_IsClickable` | Element not found | Same |
| `StatusBarTests.ActiveButton_IsClickable` | Element not found | Same |
| `CameraViewTests.Default_CameraContentNotVisible` | Assertion failed | `CameraTabButton` click in prior test changed state |
| `CameraViewTests.ClickCameraTab_TranscriptContentDisappears` | Element not found: `CameraTabButton` | Navigation state |
| `DebugOverlayTests.ClearButton_Click_DoesNotThrow` | Element not found | Navigation state |
| `QuickActionTests.LookButton_Exists` | Element not found | Navigation state |
| `QuickActionTests.AskButton_Exists` | Element not found | Navigation state |
| `QuickActionTests.ReadButton_Exists` | Element not found | Navigation state |
| `TabSwitchingTests.CameraTabButton_Exists` | Element not found | Navigation state |
| `TabSwitchingTests.CameraTabButton_Click_DoesNotThrow` | Element not found | Navigation state |

### Navigation (2 failures)

| Test | Error | Root Cause |
|---|---|---|
| `TabNavigationTests.NavigateToSettings_ShowsSettingsPage` | `IsLoaded` timeout | `NavigateToSettings()` toggled wrong direction |
| `TabNavigationTests.TabSwitching_RoundTrip_AllPagesLoad` | `IsLoaded` timeout | Cascading nav failure |

### SettingsPage (30 failures)

All 30 settings tests fail with element-not-found or timeout. Root cause chain:
1. `NavigateToSettings()` → navigation toggle mismatch → never reaches SettingsPage
2. `SettingsPage.ConnectionSettingsCard.Click()` → Frame TapGestureRecognizer not invokable → sub-page never opens
3. `SubPage.WaitReady(10000)` → times out → all assertions fail

| Class | Tests | All Fail |
|---|---|---|
| `ApiKeyTests` | 6 | ✓ |
| `ProviderTests` | 4 | ✓ |
| `ModelSelectionTests` | 5 | ✓ |
| `AzureSettingsTests` | 5 | ✓ |
| `VoiceSettingsTests` | 4 | ✓ |
| `DebugSettingsTests` | 6 | ✓ |
