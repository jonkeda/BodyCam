# Wave 3 — Move Pages to `Pages/` Folder

**Prerequisite:** Wave 2 (ViewModels split)  
**Solves:** P3 (flat file layout)  
**Target:** Build green, all tests green  

---

## Problem

Pages live in two locations with no consistency:
- Root: `SetupPage.xaml`, `MainPage.xaml`, `SettingsPage.xaml` → namespace `BodyCam`
- Sub-folder: `Settings/ConnectionSettingsPage.xaml` etc. → namespace `BodyCam.Settings`

As the app grows, root gets crowded. No organizational principle.

---

## New Layout

```
src/BodyCam/
├── Pages/
│   ├── SetupPage.xaml(.cs)
│   ├── MainPage.xaml(.cs)
│   ├── SettingsPage.xaml(.cs)
│   └── Settings/
│       ├── ConnectionSettingsPage.xaml(.cs)
│       ├── VoiceSettingsPage.xaml(.cs)
│       ├── DeviceSettingsPage.xaml(.cs)
│       └── AdvancedSettingsPage.xaml(.cs)
```

---

## Changes

### File Moves

| From | To | New Namespace |
|---|---|---|
| `src/BodyCam/SetupPage.xaml(.cs)` | `src/BodyCam/Pages/SetupPage.xaml(.cs)` | `BodyCam.Pages` |
| `src/BodyCam/MainPage.xaml(.cs)` | `src/BodyCam/Pages/MainPage.xaml(.cs)` | `BodyCam.Pages` |
| `src/BodyCam/SettingsPage.xaml(.cs)` | `src/BodyCam/Pages/SettingsPage.xaml(.cs)` | `BodyCam.Pages` |
| `src/BodyCam/Settings/ConnectionSettingsPage.xaml(.cs)` | `src/BodyCam/Pages/Settings/ConnectionSettingsPage.xaml(.cs)` | `BodyCam.Pages.Settings` |
| `src/BodyCam/Settings/VoiceSettingsPage.xaml(.cs)` | `src/BodyCam/Pages/Settings/VoiceSettingsPage.xaml(.cs)` | `BodyCam.Pages.Settings` |
| `src/BodyCam/Settings/DeviceSettingsPage.xaml(.cs)` | `src/BodyCam/Pages/Settings/DeviceSettingsPage.xaml(.cs)` | `BodyCam.Pages.Settings` |
| `src/BodyCam/Settings/AdvancedSettingsPage.xaml(.cs)` | `src/BodyCam/Pages/Settings/AdvancedSettingsPage.xaml(.cs)` | `BodyCam.Pages.Settings` |

### Namespace Updates

Each moved file needs:

**Code-behind (.cs):**
```csharp
// SetupPage, MainPage, SettingsPage:
namespace BodyCam.Pages;  // was: BodyCam

// Settings sub-pages:
namespace BodyCam.Pages.Settings;  // was: BodyCam.Settings
```

**XAML (.xaml):**
```xml
<!-- SetupPage, MainPage, SettingsPage -->
x:Class="BodyCam.Pages.SetupPage"  <!-- was BodyCam.SetupPage -->

<!-- Settings sub-pages -->
x:Class="BodyCam.Pages.Settings.ConnectionSettingsPage"  <!-- was BodyCam.Settings.ConnectionSettingsPage -->
```

### `AppShell.xaml` — Update DataTemplate references

```xml
<!-- BEFORE -->
xmlns:local="clr-namespace:BodyCam"
...
ContentTemplate="{DataTemplate local:SetupPage}"
ContentTemplate="{DataTemplate local:MainPage}"
ContentTemplate="{DataTemplate local:SettingsPage}"

<!-- AFTER -->
xmlns:pages="clr-namespace:BodyCam.Pages"
...
ContentTemplate="{DataTemplate pages:SetupPage}"
ContentTemplate="{DataTemplate pages:MainPage}"
ContentTemplate="{DataTemplate pages:SettingsPage}"
```

### `AppShell.xaml.cs` — Update route registrations

```csharp
// BEFORE
Routing.RegisterRoute(nameof(Settings.ConnectionSettingsPage), typeof(Settings.ConnectionSettingsPage));
Routing.RegisterRoute(nameof(Settings.VoiceSettingsPage), typeof(Settings.VoiceSettingsPage));
Routing.RegisterRoute(nameof(Settings.DeviceSettingsPage), typeof(Settings.DeviceSettingsPage));
Routing.RegisterRoute(nameof(Settings.AdvancedSettingsPage), typeof(Settings.AdvancedSettingsPage));

// AFTER
Routing.RegisterRoute(nameof(Pages.Settings.ConnectionSettingsPage), typeof(Pages.Settings.ConnectionSettingsPage));
Routing.RegisterRoute(nameof(Pages.Settings.VoiceSettingsPage), typeof(Pages.Settings.VoiceSettingsPage));
Routing.RegisterRoute(nameof(Pages.Settings.DeviceSettingsPage), typeof(Pages.Settings.DeviceSettingsPage));
Routing.RegisterRoute(nameof(Pages.Settings.AdvancedSettingsPage), typeof(Pages.Settings.AdvancedSettingsPage));
```

### `SettingsPage.xaml.cs` — Update GoToAsync names

```csharp
// BEFORE
using BodyCam.ViewModels;
namespace BodyCam;
...
await Shell.Current.GoToAsync(nameof(Settings.ConnectionSettingsPage));

// AFTER
using BodyCam.ViewModels;
namespace BodyCam.Pages;
...
await Shell.Current.GoToAsync(nameof(Settings.ConnectionSettingsPage));
// This still works because Settings sub-folder is relative to BodyCam.Pages
```

### `ServiceExtensions.cs` — Update DI registrations

```csharp
// BEFORE
services.AddTransient<SetupPage>();
services.AddTransient<MainPage>();
services.AddTransient<SettingsPage>();
services.AddTransient<Settings.ConnectionSettingsPage>();
services.AddTransient<Settings.VoiceSettingsPage>();
services.AddTransient<Settings.DeviceSettingsPage>();
services.AddTransient<Settings.AdvancedSettingsPage>();

// AFTER
services.AddTransient<Pages.SetupPage>();
services.AddTransient<Pages.MainPage>();
services.AddTransient<Pages.SettingsPage>();
services.AddTransient<Pages.Settings.ConnectionSettingsPage>();
services.AddTransient<Pages.Settings.VoiceSettingsPage>();
services.AddTransient<Pages.Settings.DeviceSettingsPage>();
services.AddTransient<Pages.Settings.AdvancedSettingsPage>();
```

Add `using BodyCam.Pages;` to ServiceExtensions.cs.

---

## Files Changed

| File | Action |
|---|---|
| `src/BodyCam/Pages/SetupPage.xaml(.cs)` | **Move** from root |
| `src/BodyCam/Pages/MainPage.xaml(.cs)` | **Move** from root |
| `src/BodyCam/Pages/SettingsPage.xaml(.cs)` | **Move** from root |
| `src/BodyCam/Pages/Settings/*.xaml(.cs)` | **Move** from `Settings/` |
| `src/BodyCam/AppShell.xaml` | Update xmlns + DataTemplate refs |
| `src/BodyCam/AppShell.xaml.cs` | Update route type references |
| `src/BodyCam/ServiceExtensions.cs` | Update DI type references |

## UI Test Impact

- AutomationIds unchanged → PageObjects unchanged
- Shell routes stay the same (string-based) → navigation works
- **Zero test changes expected**

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android -v q
dotnet test src/BodyCam.Tests -v q --no-restore
# Spot-check one UI test class
Get-Process -Name "BodyCam*" -EA SilentlyContinue | Stop-Process -Force
dotnet test src/BodyCam.UITests --no-build --filter "FullyQualifiedName~TabNavigationTests"
```
