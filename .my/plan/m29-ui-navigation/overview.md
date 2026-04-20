# M29 — UI Navigation

## Goal

Replace the current `TabBar` navigation with a linear flow where:

1. **Setup → Home** — after setup completes, user lands on the Home page (no back to setup).
2. **Home ⇄ Settings** — the ⚙ button pushes Settings onto the stack; the back arrow returns to Home.

The tab bar should be removed entirely. Home is the root page after setup.

---

## Current Architecture

```
TabBar
├── Tab: SetupPage   (Route "SetupPage")
├── Tab: MainPage    (Route "MainPage")
└── Tab: SettingsPage (Route "SettingsPage")
```

- All three tabs are peers; any tab is reachable at any time.
- `AppShell.OnShellNavigated` hacks around this with first-run detection (`ISettingsService.SetupCompleted`) to redirect to SetupPage or MainPage.
- Settings sub-pages (Connection, Voice, Device, Advanced) are pushed via `Routing.RegisterRoute` + relative `GoToAsync`.
- The ⚙/✕ button in the title bar toggles between `//MainPage` and `//SettingsPage` using absolute routes.

**Problems with the current design:**

- TabBar is visible but the user should never freely switch between Setup/Home/Settings.
- Setup → Home is a one-way gate; tabs make it look reversible.
- Absolute-route toggling (`//SettingsPage`) resets the Settings page on every visit instead of preserving state.

---

## Option A — Single-Tab Shell + Pushed Pages

Keep `Shell` as the root but use a **single-item `ShellContent`** for the home page. Push other pages onto the Shell navigation stack.

### Structure

```xml
<Shell>
    <!-- No TabBar, single root -->
    <ShellContent Route="MainPage"
                  ContentTemplate="{DataTemplate mainPages:MainPage}" />
</Shell>
```

### Navigation Flow

| Action | Code |
|--------|------|
| App start (setup incomplete) | `GoToAsync("//SetupPage")` — SetupPage registered as a route |
| Setup complete | `GoToAsync("//MainPage")` — replaces stack, no back to setup |
| ⚙ tapped | `GoToAsync("SettingsPage")` — pushes onto stack |
| Back from Settings | Hardware/software back button pops automatically |
| Settings sub-page | `GoToAsync("ConnectionSettingsPage")` — pushes again |

### Pros

- Minimal change — Shell infrastructure stays, just remove `TabBar`.
- Built-in back-button and navigation-bar support.
- Settings sub-pages already work this way (pushed routes).
- `Shell.TitleView` customization continues to work.

### Cons

- Shell navigation has known quirks (e.g. back-button interception, page lifecycle timing).
- Setup page either needs its own `ShellContent` (two root items) or must be a pushed modal.
- `GoToAsync("//SetupPage")` as absolute route requires it to exist as a `ShellContent`.

### Variant A1 — Setup as Modal

Push SetupPage as a **modal** page instead of a Shell route:

```csharp
await Navigation.PushModalAsync(new SetupPage());
```

After setup, dismiss the modal and land on MainPage. No Shell route needed for Setup. This cleanly separates the first-run experience from normal navigation.

### Variant A2 — Two ShellContents, Hidden Tab Bar

```xml
<TabBar>
    <ShellContent Route="SetupPage" ... Shell.TabBarIsVisible="False" />
    <ShellContent Route="MainPage" ... Shell.TabBarIsVisible="False" />
</TabBar>
```

Keep both as root-level items with the tab bar hidden via `Shell.TabBarIsVisible="False"`. Navigate between them with absolute routes. Settings is a pushed page.

---

## Option B — NavigationPage (No Shell)

Remove `Shell` entirely and use `NavigationPage` as the root in `App.xaml.cs`.

### Structure

```csharp
// App.xaml.cs
MainPage = new NavigationPage(serviceProvider.GetRequiredService<MainPage>());
```

### Navigation Flow

| Action | Code |
|--------|------|
| App start (setup incomplete) | `Navigation.PushModalAsync(setupPage)` |
| Setup complete | `Navigation.PopModalAsync()` — reveals MainPage |
| ⚙ tapped | `Navigation.PushAsync(settingsPage)` |
| Back from Settings | `Navigation.PopAsync()` or hardware back |
| Settings sub-page | `Navigation.PushAsync(connectionPage)` |

### Pros

- Simple, predictable stack-based navigation.
- No Shell quirks — `PushAsync`/`PopAsync` just work.
- Full control over title bar, back button, animations.

### Cons

- **Loses Shell features:** flyout, `Shell.TitleView`, URI-based routing, search handlers.
- Must manually manage the navigation bar (title, buttons).
- Existing `Shell.TitleView` (build label, ⚙ icon) must be recreated as a custom `NavigationPage.TitleView` or toolbar items.
- Settings sub-page routing (`nameof(...)` pattern) must change to `PushAsync(new Page())`.
- Deep-link/URI support lost (not currently used, but foreclosed).

---

## Option C — Shell with FlyoutBehavior.Disabled (Recommended)

Use `Shell` with `FlyoutBehavior="Disabled"` and **no `TabBar`**. MainPage is the sole `ShellContent`. All other pages are registered routes that get pushed.

### Structure

```xml
<Shell FlyoutBehavior="Disabled">
    <ShellContent Route="MainPage"
                  ContentTemplate="{DataTemplate mainPages:MainPage}" />
</Shell>
```

```csharp
// AppShell constructor
Routing.RegisterRoute(nameof(Pages.Setup.SetupPage), typeof(Pages.Setup.SetupPage));
Routing.RegisterRoute(nameof(Pages.Settings.SettingsPage), typeof(Pages.Settings.SettingsPage));
Routing.RegisterRoute(nameof(Pages.Settings.ConnectionSettingsPage), typeof(Pages.Settings.ConnectionSettingsPage));
// ... etc.
```

### Navigation Flow

| Action | Code |
|--------|------|
| App start (setup incomplete) | `GoToAsync(nameof(SetupPage))` — pushed on top of MainPage |
| Setup complete | `GoToAsync("//MainPage")` — pops back to root |
| ⚙ tapped | `GoToAsync(nameof(SettingsPage))` — pushed |
| Back from Settings | Back button pops, or `GoToAsync("..")` |
| Settings → sub-page | `GoToAsync(nameof(ConnectionSettingsPage))` — pushed |
| Back from sub-page | Back button pops to SettingsPage |

### Pros

- **Keeps all Shell infrastructure** — `TitleView`, URI routing, back navigation, page lifecycle.
- Tab bar disappears naturally (no tabs defined).
- Settings page is pushed (not swapped), so it preserves the natural back-stack.
- Settings sub-pages already work this way — **zero change** for them.
- Setup page pushed on top; after completion, popping back to root gives a clean one-way gate.
- Minimal diff from current code.

### Cons

- Setup is on the stack above MainPage — MainPage gets created even before setup completes. (Mitigated: `ContentTemplate` defers creation, and `SetupCompleted` check happens early.)
- Shell still has platform-specific edge cases (Android back-button behavior, iOS swipe-back).

---

## Comparison Matrix

| Criterion | A (Single-Tab) | A1 (Modal Setup) | A2 (Hidden TabBar) | B (NavigationPage) | **C (No TabBar, Pushed)** |
|---|---|---|---|---|---|
| Tab bar removed | ✓ | ✓ | ✓ | ✓ | **✓** |
| Shell.TitleView works | ✓ | ✓ | ✓ | ✗ | **✓** |
| Back button to Home | ✓ | ✓ | ✗ (absolute routes) | ✓ | **✓** |
| Settings state preserved | ✓ | ✓ | ✗ | ✓ | **✓** |
| Setup one-way gate | Partial | ✓ | ✓ | ✓ | **✓** |
| Settings sub-pages unchanged | ✓ | ✓ | ✓ | ✗ | **✓** |
| URI routing preserved | ✓ | ✓ | ✓ | ✗ | **✓** |
| Lines of code changed | ~30 | ~40 | ~15 | ~80+ | **~25** |
| Risk | Low | Low | Low | Medium | **Low** |

---

## Recommendation

**Option C — Shell with `FlyoutBehavior="Disabled"`, no TabBar, pushed pages.**

It gives us a clean linear flow (Setup → Home → Settings → Sub-pages) with natural back-navigation, keeps all Shell features intact, and requires the smallest diff. The settings sub-pages need zero changes since they already use `GoToAsync` with registered routes.

### Key Changes Required

1. **AppShell.xaml** — Remove `<TabBar>`, add single `<ShellContent>`, set `FlyoutBehavior="Disabled"`.
2. **AppShell.xaml.cs** — Register SetupPage and SettingsPage as routes. Simplify `OnShellNavigated` to push SetupPage on first load if setup incomplete. Change `OnNavIconTapped` to `GoToAsync(nameof(SettingsPage))`.
3. **SetupPage.xaml.cs** — After completion, `GoToAsync("//MainPage")` to pop back to root.
4. **Remove** the `_onSettings` toggle logic — back button handles return from Settings naturally.
5. **Title bar** — Keep ⚙ button visible only on MainPage; on Settings/sub-pages the Shell back arrow appears automatically.
