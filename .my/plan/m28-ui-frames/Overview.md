# M28 — UI Frames Overview & Refactoring Proposal

## Current State

### File Inventory

| Layer | File | Lines | ViewModel |
|---|---|---|---|
| Shell | `AppShell.xaml` | 46 | — (code-behind) |
| Shell | `AppShell.xaml.cs` | 49 | — |
| Setup | `SetupPage.xaml` | 149 | `SetupViewModel` (286 lines) |
| Setup | `SetupPage.xaml.cs` | 17 | |
| Main | `MainPage.xaml` | 345 | `MainViewModel` (465 lines) |
| Main | `MainPage.xaml.cs` | 99 | |
| Settings hub | `SettingsPage.xaml` | 92 | `SettingsViewModel` (464 lines) |
| Settings hub | `SettingsPage.xaml.cs` | 18 | |
| Sub-settings | `Settings/ConnectionSettingsPage.xaml` | 168 | `SettingsViewModel` (shared) |
| Sub-settings | `Settings/ConnectionSettingsPage.xaml.cs` | 20 | |
| Sub-settings | `Settings/VoiceSettingsPage.xaml` | 39 | `SettingsViewModel` (shared) |
| Sub-settings | `Settings/VoiceSettingsPage.xaml.cs` | 10 | |
| Sub-settings | `Settings/DeviceSettingsPage.xaml` | 40 | `SettingsViewModel` (shared) |
| Sub-settings | `Settings/DeviceSettingsPage.xaml.cs` | 10 | |
| Sub-settings | `Settings/AdvancedSettingsPage.xaml` | 93 | `SettingsViewModel` (shared) |
| Sub-settings | `Settings/AdvancedSettingsPage.xaml.cs` | 10 | |

**Totals:** 8 pages, 3 ViewModels, ~1,200 lines ViewModel code, ~1,100 lines XAML

### Navigation Architecture

```
AppShell (TabBar, hidden tabs)
├── //SetupPage          ← first-launch wizard, redirects to MainPage on completion
├── //MainPage           ← primary view (transcript, camera, actions)
└── //SettingsPage       ← hub with 4 cards
    ├── ConnectionSettingsPage   ← pushed route (Shell.GoToAsync)
    ├── VoiceSettingsPage        ← pushed route
    ├── DeviceSettingsPage       ← pushed route
    └── AdvancedSettingsPage     ← pushed route
```

Navigation between MainPage ↔ SettingsPage uses a toggle NavIcon (`⚙`/`✕`) in the Shell TitleView that calls `GoToAsync("//MainPage")` or `GoToAsync("//SettingsPage")`.

Settings sub-pages are registered as named routes in `AppShell` constructor and navigated via `Shell.Current.GoToAsync(nameof(SubPage))`.

### DI Registration

```csharp
// ServiceExtensions.cs — AddViewModels()
services.AddTransient<SetupViewModel>();
services.AddTransient<MainViewModel>();
services.AddTransient<SettingsViewModel>();     // ONE VM for 5 pages
services.AddTransient<SetupPage>();
services.AddTransient<MainPage>();
services.AddTransient<SettingsPage>();
services.AddTransient<Settings.ConnectionSettingsPage>();
services.AddTransient<Settings.VoiceSettingsPage>();
services.AddTransient<Settings.DeviceSettingsPage>();
services.AddTransient<Settings.AdvancedSettingsPage>();
```

---

## Problems

### P1 — God ViewModel: `SettingsViewModel` (464 lines, 5 pages)

One ViewModel serves the settings hub AND all 4 sub-pages. It contains:
- Provider selection (Connection)
- API key management (Connection)
- Model picker options and selections (Connection)
- Azure deployment config (Connection)
- Test connection logic with HTTP probing (Connection)
- Voice/turn-detection/noise settings (Voice)
- System instructions (Voice)
- Camera/mic/speaker device selection (Device)
- Debug/cost/token switches (Advanced)
- Diagnostics/telemetry config (Advanced)
- Tool settings dynamic sections (Advanced)

Every sub-page gets the entire ViewModel injected even though it only uses ~20% of its properties.

### P2 — Navigation toggle is fragile

The NavIcon toggle (`⚙`/`✕`) maintains `_onSettings` state via the `Navigated` event. This has issues:
- Clicking from a pushed sub-page (e.g., ConnectionSettingsPage) → `GoToAsync("//MainPage")` pops the entire stack. Correct behavior but state tracking is fragile.
- UI tests can't reliably navigate because the toggle direction depends on current state.
- No visual affordance for "back" from settings sub-pages beyond the Shell back button.

### P3 — Flat file layout

Pages live in two locations with no consistency:
- Root: `SetupPage`, `MainPage`, `SettingsPage` (root namespace `BodyCam`)
- Sub-folder: `Settings/ConnectionSettingsPage` etc. (namespace `BodyCam.Settings`)

As more pages are added (e.g., History, Profile, Help, About), root will get crowded.

### P4 — No page base class or conventions

Each page code-behind is ad-hoc:
- `MainPage.xaml.cs` has 99 lines of scroll logic, animation, keyboard shortcuts
- `SettingsPage.xaml.cs` has 4 navigation event handlers
- Sub-settings code-behinds are 10 lines each (just `BindingContext = viewModel`)
- `SetupPage.xaml.cs` has an event subscription for `SetupFinished`

No shared base class. No consistent pattern for navigation, lifecycle, or cleanup.

### P5 — MainPage.xaml is monolithic (345 lines)

Contains 4 visual sections inline: status bar, content area (transcript + camera + debug overlay + snapshot overlay), tab selector, quick action bar. Adding features (e.g., map view, history panel) means MainPage.xaml keeps growing.

### P6 — UI tests can't navigate reliably

See fix-003. The toggle nav, Frame-based cards, and Label-based NavIcon create FlaUI blind spots.

---

## Proposed Architecture

### Principle: 1 Page = 1 ViewModel, pages in folders, ContentViews for composition

### New File Layout

```
src/BodyCam/
├── App.xaml(.cs)
├── AppShell.xaml(.cs)
├── MauiProgram.cs
├── ServiceExtensions.cs
│
├── Pages/
│   ├── Setup/
│   │   └── SetupPage.xaml(.cs)
│   ├── Main/
│   │   ├── MainPage.xaml(.cs)
│   │   └── Views/                         ← ContentView fragments
│   │       ├── StatusBarView.xaml(.cs)
│   │       ├── TranscriptView.xaml(.cs)
│   │       ├── CameraView.xaml(.cs)
│   │       ├── QuickActionsView.xaml(.cs)
│   │       └── DebugOverlayView.xaml(.cs)
│   └── Settings/
│       ├── SettingsPage.xaml(.cs)          ← hub (unchanged)
│       ├── ConnectionPage.xaml(.cs)
│       ├── VoicePage.xaml(.cs)
│       ├── DevicePage.xaml(.cs)
│       └── AdvancedPage.xaml(.cs)
│
├── ViewModels/
│   ├── SetupViewModel.cs                   ← unchanged
│   ├── MainViewModel.cs                    ← slimmed: delegates to sub-VMs
│   ├── Settings/
│   │   ├── SettingsHubViewModel.cs         ← just the hub card navigation
│   │   ├── ConnectionViewModel.cs          ← provider, API key, models, test connection
│   │   ├── VoiceViewModel.cs               ← voice, turn detection, system instructions
│   │   ├── DeviceViewModel.cs              ← camera, mic, speaker
│   │   └── AdvancedViewModel.cs            ← debug, diagnostics, tool settings
│   └── Components/
│       ├── StatusBarViewModel.cs           ← state pill, debug toggle
│       └── TranscriptViewModel.cs          ← entries, clear, scroll
│
└── Views/                                  ← shared ContentViews (reusable)
    └── SettingsCardView.xaml(.cs)           ← styled card template
```

### ViewModel Split

**Current:** 3 ViewModels (SetupViewModel, MainViewModel, SettingsViewModel)

**Proposed:** 8 ViewModels

| ViewModel | Page(s) | Responsibilities | Est. Lines |
|---|---|---|---|
| `SetupViewModel` | SetupPage | Wizard steps, permissions, API key validation | ~280 (unchanged) |
| `MainViewModel` | MainPage | State machine, orchestrator lifecycle, snapshot | ~250 (slimmed) |
| `StatusBarViewModel` | StatusBarView | State pill, debug toggle, clear | ~60 |
| `TranscriptViewModel` | TranscriptView | Entries collection, scroll-to-bottom | ~80 |
| `SettingsHubViewModel` | SettingsPage | Title only (cards are pure navigation) | ~10 |
| `ConnectionViewModel` | ConnectionPage | Provider, API key, models, Azure config, test connection | ~200 |
| `VoiceViewModel` | VoicePage | Voice, turn detection, noise reduction, system instructions | ~60 |
| `DeviceViewModel` | DevicePage | Camera, mic, speaker pickers | ~60 |
| `AdvancedViewModel` | AdvancedPage | Debug switches, diagnostics, tool settings | ~120 |

Total: ~1,120 lines across 9 files vs 1,215 lines across 3 files. Similar total but each file is focused and testable.

### Navigation Refactoring

**Option A — Keep Shell TabBar toggle (minimal change)**

Fix the NavIcon to be a `Button` (not Label + TapGestureRecognizer) for UIA support. Keep the `⚙`/`✕` toggle but make navigation idempotent by checking current location before navigating.

**Option B — Shell FlyoutItem sidebar (medium change)**

Replace the hidden TabBar + toggle with a proper Shell Flyout:
```xml
<Shell FlyoutBehavior="Flyout">
    <FlyoutItem Title="Home" Icon="home.png">
        <ShellContent Route="MainPage" ContentTemplate="{DataTemplate ...}" />
    </FlyoutItem>
    <FlyoutItem Title="Settings" Icon="settings.png">
        <ShellContent Route="SettingsPage" ContentTemplate="{DataTemplate ...}" />
    </FlyoutItem>
</Shell>
```
Pros: Standard MAUI pattern, scales to more top-level pages.  
Cons: Flyout takes screen space, mobile layout changes.

**Option C — Keep toggle but add explicit back navigation (recommended)**

Keep the `⚙`/`✕` toggle for top-level nav. Add an explicit "← Back" button to each settings sub-page header instead of relying on Shell's back button. This gives UI tests a reliable AutomationId to click.

```xml
<!-- In each settings sub-page -->
<Grid ColumnDefinitions="Auto,*" Padding="16,8">
    <Button AutomationId="BackButton" Text="←" Clicked="OnBackClicked"
            BackgroundColor="Transparent" FontSize="20" />
    <Label Grid.Column="1" Text="Connection" FontSize="Title" VerticalOptions="Center" />
</Grid>
```

### MainPage Decomposition

Extract inline XAML sections into `ContentView` components:

```xml
<!-- MainPage.xaml — after refactoring -->
<Grid RowDefinitions="Auto,*,Auto,Auto">
    <views:StatusBarView Grid.Row="0" BindingContext="{Binding StatusBar}" />
    
    <Grid Grid.Row="1">
        <views:TranscriptView IsVisible="{Binding ShowTranscriptTab}" BindingContext="{Binding Transcript}" />
        <views:CameraView IsVisible="{Binding ShowCameraTab}" />
        <views:DebugOverlayView IsVisible="{Binding DebugVisible}" VerticalOptions="End" />
    </Grid>
    
    <views:TabSelectorView Grid.Row="2" />
    <views:QuickActionsView Grid.Row="3" />
</Grid>
```

**MainPage.xaml drops from 345 → ~30 lines.** Each ContentView is 30–80 lines and independently testable.

### Settings Card Pattern

Replace Frame + TapGestureRecognizer with a reusable `SettingsCardView`:

```xml
<!-- Views/SettingsCardView.xaml -->
<ContentView>
    <Button AutomationId="{Binding AutomationId}"
            Clicked="OnClicked"
            BackgroundColor="{AppThemeBinding Light=#FFFFFF, Dark=#1E1E1E}"
            ...card styling... >
        <Button.ContentLayout>
            <ButtonContentLayout Position="Left" Spacing="12" />
        </Button.ContentLayout>
    </Button>
</ContentView>
```

This gives FlaUI a proper `Invoke` pattern on every card.

### DI Registration (proposed)

```csharp
public static IServiceCollection AddViewModels(this IServiceCollection services)
{
    // ViewModels
    services.AddTransient<SetupViewModel>();
    services.AddTransient<MainViewModel>();
    services.AddTransient<StatusBarViewModel>();
    services.AddTransient<TranscriptViewModel>();
    services.AddTransient<SettingsHubViewModel>();
    services.AddTransient<ConnectionViewModel>();
    services.AddTransient<VoiceViewModel>();
    services.AddTransient<DeviceViewModel>();
    services.AddTransient<AdvancedViewModel>();

    // Pages
    services.AddTransient<Pages.Setup.SetupPage>();
    services.AddTransient<Pages.Main.MainPage>();
    services.AddTransient<Pages.Settings.SettingsPage>();
    services.AddTransient<Pages.Settings.ConnectionPage>();
    services.AddTransient<Pages.Settings.VoicePage>();
    services.AddTransient<Pages.Settings.DevicePage>();
    services.AddTransient<Pages.Settings.AdvancedPage>();

    return services;
}
```

---

## Migration Path

### Wave 1 — Fix navigation for UI tests (fix-003)

- NavIcon: Label → Button
- Settings cards: Frame+Tap → Button
- Fixture: idempotent navigation
- **No structural refactoring — just make tests green.**

### Wave 2 — Split SettingsViewModel

- Extract `ConnectionViewModel` (provider, API key, models, Azure, test connection)
- Extract `VoiceViewModel` (voice, turn detection, noise, system instructions)
- Extract `DeviceViewModel` (camera, mic, speaker)
- Extract `AdvancedViewModel` (debug, diagnostics, tool settings)
- `SettingsHubViewModel` remains as a thin shell
- Each sub-page gets its own ViewModel injected
- **Each extraction is one commit. Tests stay green throughout.**

### Wave 3 — Move pages to `Pages/` folder

- `SetupPage` → `Pages/Setup/SetupPage`
- `MainPage` → `Pages/Main/MainPage`
- `SettingsPage` → `Pages/Settings/SettingsPage`
- Sub-settings follow into `Pages/Settings/`
- Update namespaces, DI registrations, Shell routes
- **Mechanical move. One commit.**

### Wave 4 — Extract MainPage ContentViews

- `StatusBarView` ← status bar XAML + `StatusBarViewModel`
- `TranscriptView` ← transcript list XAML + `TranscriptViewModel`
- `QuickActionsView` ← action bar XAML (binds to MainViewModel commands)
- `DebugOverlayView` ← debug panel XAML
- MainPage.xaml becomes a composition shell
- **Each extraction is one commit.**

### Wave 5 — Settings card template

- Create `SettingsCardView` reusable component
- Replace 4 inline Frame cards in SettingsPage.xaml
- **One commit.**

### Wave 6 — Test coverage

- Unit tests for all 4 extracted settings ViewModels (Connection, Voice, Device, Advanced)
- New UI tests: `SettingsHubTests` (card existence + navigation), `DeviceSettingsTests`
- Augment `VoiceSettingsTests` with `SystemInstructionsEditor` test
- **Test-only wave. No production code changes.**

---

## UI Test Impact

Each wave should keep UI tests green (or improve them):

| Wave | UI Test Impact |
|---|---|
| 1 | Fixes 44 failures → all green |
| 2 | No page changes — tests unchanged |
| 3 | Page AutomationIds unchanged — update PageObject namespaces if needed |
| 4 | AutomationIds preserved on ContentViews — tests unchanged |
| 5 | Card AutomationIds preserved — tests unchanged |
| 6 | Adds ~25 new unit tests + ~12 new UI tests |
