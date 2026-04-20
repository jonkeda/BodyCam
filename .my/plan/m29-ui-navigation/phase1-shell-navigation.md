# M29 Phase 1 — Shell Navigation Refactor

**Status:** NOT STARTED  
**Depends on:** M28 (page folder structure)

---

## Goal

Remove the `TabBar` from `AppShell.xaml` and switch to push-based navigation
using Option C from the overview: single `ShellContent` root (MainPage), all
other pages pushed via `GoToAsync` with registered routes.

---

## Wave 1: AppShell.xaml — Remove TabBar

Replace the `<TabBar>` block with a single root `ShellContent`. Add
`FlyoutBehavior="Disabled"` to suppress the flyout hamburger menu.

Remove the `setupPages` and `settingsPages` xmlns declarations — those pages
are no longer referenced directly in XAML (they become registered routes).

### Before

```xml
<Shell ... >
    <TabBar>
        <Tab>
            <ShellContent Route="SetupPage"
                          ContentTemplate="{DataTemplate setupPages:SetupPage}" />
        </Tab>
        <Tab>
            <ShellContent Route="MainPage"
                          ContentTemplate="{DataTemplate mainPages:MainPage}" />
        </Tab>
        <Tab>
            <ShellContent Route="SettingsPage"
                          ContentTemplate="{DataTemplate settingsPages:SettingsPage}" />
        </Tab>
    </TabBar>
</Shell>
```

### After

```xml
<Shell FlyoutBehavior="Disabled" ... >
    <ShellContent Route="MainPage"
                  ContentTemplate="{DataTemplate mainPages:MainPage}" />
</Shell>
```

---

## Wave 2: AppShell.xaml.cs — Route Registration & Navigation Logic

### 2.1 Register SetupPage and SettingsPage as Routes

Add to the constructor alongside the existing sub-page registrations:

```csharp
Routing.RegisterRoute(nameof(Pages.Setup.SetupPage), typeof(Pages.Setup.SetupPage));
Routing.RegisterRoute(nameof(Pages.Settings.SettingsPage), typeof(Pages.Settings.SettingsPage));
```

### 2.2 Simplify OnShellNavigated

The current `OnShellNavigated` tracks `_onSettings` and toggles the NavIcon.
With push-based navigation, the ⚙ button should only appear on MainPage. On
Settings (and sub-pages), Shell's built-in back arrow handles return.

Replace the handler:

```csharp
private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
{
    var loc = e.Current?.Location?.OriginalString ?? "";

    // Show ⚙ only on MainPage; hide on pushed pages (Shell provides back arrow)
    NavIcon.IsVisible = !loc.Contains("/");

    if (!_checkedSetup)
    {
        _checkedSetup = true;
        var settings = Handler?.MauiContext?.Services.GetService<Services.ISettingsService>();
        if (settings is not null && !settings.SetupCompleted)
        {
            Dispatcher.Dispatch(async () =>
                await GoToAsync(nameof(Pages.Setup.SetupPage)));
            return;
        }
    }
}
```

Key changes:
- Remove `_onSettings` field entirely.
- `NavIcon.IsVisible` hides the gear on any pushed page (location contains `/`).
- Setup pushes onto MainPage stack via relative route (not absolute).
- Remove the `SetupPage` redirect — after setup completes, popping reveals MainPage.

### 2.3 Simplify OnNavIconTapped

```csharp
private async void OnNavIconTapped(object? sender, EventArgs e)
{
    await Current.GoToAsync(nameof(Pages.Settings.SettingsPage));
}
```

No toggle logic needed — back navigation is handled by the Shell back arrow.

---

## Wave 3: SetupPage — Navigate After Completion

Currently `SetupPage.xaml.cs` navigates to MainPage with `GoToAsync("//MainPage")`.
Since SetupPage is now pushed on top of MainPage, completion should pop back:

```csharp
// After setup completes
await Shell.Current.GoToAsync("..");
```

Or equivalently:

```csharp
await Shell.Current.Navigation.PopAsync();
```

Both pop the SetupPage off the stack, revealing MainPage underneath.

Set `settings.SetupCompleted = true` before navigating so the setup doesn't
re-trigger on `OnShellNavigated`.

---

## Wave 4: Shell.TitleView Adjustments

The current `Shell.TitleView` contains the ⚙ button globally. When Settings is
pushed, Shell shows a back arrow automatically in the navigation bar — but the
`TitleView` may conflict with it on some platforms.

Two options:

**Option A (preferred):** Keep the global `TitleView` but hide `NavIcon` on
pushed pages (already done in Wave 2 via `NavIcon.IsVisible`). The back arrow
and title view coexist — test on Windows and Android.

**Option B (fallback):** Move the ⚙ button out of `Shell.TitleView` and into
`MainPage.xaml` as a `Shell.TitleView` override local to that page only. Other
pages get Shell's default title bar with back arrow.

---

## Verification

- [ ] App launches → MainPage shown (no tab bar visible)
- [ ] First launch (setup incomplete) → SetupPage pushed on top
- [ ] Setup completes → pops back to MainPage
- [ ] Tap ⚙ → SettingsPage pushed, back arrow visible
- [ ] Tap back on SettingsPage → returns to MainPage
- [ ] Settings → sub-page → back → returns to SettingsPage
- [ ] Settings → sub-page → back → back → returns to MainPage
- [ ] `dotnet build` — 0 errors
- [ ] `dotnet test` — all unit tests pass
