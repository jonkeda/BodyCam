# M27 — Settings UI Refactor

**Status:** Complete
**Goal:** Split the monolithic SettingsPage into grouped sub-pages for mobile-friendly navigation, move test connection to top, and create per-page UITest page objects.

---

## What Changed

The single `SettingsPage` (12 sections in one long scroll) was replaced with a category-list hub that navigates to 4 dedicated sub-pages via Shell routes.

### Before

One `SettingsPage.xaml` with all settings in a single `ScrollView` — Provider, Models, Azure, Voice, System Instructions, API Key, Test Connection, Camera, Audio Input, Audio Output, Debug, Diagnostics, Tool Settings.

### After

| Page | Route | Contents |
|---|---|---|
| `SettingsPage` | `//SettingsPage` (tab) | Category cards — tappable list navigating to sub-pages |
| `ConnectionSettingsPage` | `ConnectionSettingsPage` (push) | Test Connection (top), Provider radio, API Key, Models (OpenAI) / Azure Deployments |
| `VoiceSettingsPage` | `VoiceSettingsPage` (push) | Voice picker, Turn Detection, Noise Reduction, System Instructions |
| `DeviceSettingsPage` | `DeviceSettingsPage` (push) | Camera source, Audio Input, Audio Output |
| `AdvancedSettingsPage` | `AdvancedSettingsPage` (push) | Debug switches, Diagnostics & Telemetry, Tool Settings |

---

## Design Decisions

1. **Category card layout** — standard mobile settings pattern (iOS Settings / Android Settings style). Each card shows icon + title + subtitle + chevron.
2. **MaximumWidthRequest="500"** on all sub-pages — prevents controls from stretching too wide on tablets/desktop.
3. **Test Connection moved to top** of ConnectionSettingsPage in a highlighted `Frame` card — most-used action is immediately accessible.
4. **FlexLayout Wrap** for API key buttons — wraps gracefully on narrow screens instead of `HorizontalStackLayout` overflow.
5. **Grid layout** for model/deployment + status label pairs — proper fill behavior vs `HorizontalStackLayout`.
6. **Single ViewModel** — all sub-pages share `SettingsViewModel` (registered as Transient). No new ViewModels needed.
7. **Shell push navigation** — sub-pages push onto the navigation stack from SettingsPage. Back button returns to the category list.

---

## Files

### New Files

| File | Purpose |
|---|---|
| `src/BodyCam/Settings/ConnectionSettingsPage.xaml` | Provider, API key, models, test connection |
| `src/BodyCam/Settings/ConnectionSettingsPage.xaml.cs` | Code-behind with provider radio handlers |
| `src/BodyCam/Settings/VoiceSettingsPage.xaml` | Voice, turn detection, noise reduction, system prompt |
| `src/BodyCam/Settings/VoiceSettingsPage.xaml.cs` | Code-behind |
| `src/BodyCam/Settings/DeviceSettingsPage.xaml` | Camera, audio input, audio output pickers |
| `src/BodyCam/Settings/DeviceSettingsPage.xaml.cs` | Code-behind |
| `src/BodyCam/Settings/AdvancedSettingsPage.xaml` | Debug, diagnostics, tool settings |
| `src/BodyCam/Settings/AdvancedSettingsPage.xaml.cs` | Code-behind |
| `src/BodyCam.UITests/Pages/ConnectionSettingsPage.cs` | Page object for connection settings |
| `src/BodyCam.UITests/Pages/VoiceSettingsPage.cs` | Page object for voice settings |
| `src/BodyCam.UITests/Pages/DeviceSettingsPage.cs` | Page object for device settings |
| `src/BodyCam.UITests/Pages/AdvancedSettingsPage.cs` | Page object for advanced settings |

### Modified Files

| File | Change |
|---|---|
| `src/BodyCam/SettingsPage.xaml` | Replaced all settings with 4 category cards |
| `src/BodyCam/SettingsPage.xaml.cs` | Replaced provider radio handlers with navigation tap handlers |
| `src/BodyCam/AppShell.xaml.cs` | Registered 4 Shell routes for sub-pages |
| `src/BodyCam/ServiceExtensions.cs` | Added DI registrations for 4 sub-pages |
| `src/BodyCam.UITests/Pages/SettingsPage.cs` | Replaced control references with category card `Button<T>` references |
| `src/BodyCam.UITests/BodyCamFixture.cs` | Added 4 sub-page object properties |
| `src/BodyCam.UITests/Tests/SettingsPage/ApiKeyTests.cs` | Navigate via ConnectionSettingsCard |
| `src/BodyCam.UITests/Tests/SettingsPage/AzureSettingsTests.cs` | Navigate via ConnectionSettingsCard |
| `src/BodyCam.UITests/Tests/SettingsPage/DebugSettingsTests.cs` | Navigate via AdvancedSettingsCard |
| `src/BodyCam.UITests/Tests/SettingsPage/ModelSelectionTests.cs` | Navigate via ConnectionSettingsCard |
| `src/BodyCam.UITests/Tests/SettingsPage/ProviderTests.cs` | Navigate via ConnectionSettingsCard |
| `src/BodyCam.UITests/Tests/SettingsPage/VoiceSettingsTests.cs` | Navigate via VoiceSettingsCard |

---

## UITest Navigation Pattern

All settings UI tests now follow a two-step navigation:

```csharp
_fixture.NavigateToSettings();                    // go to category list
_fixture.SettingsPage.ConnectionSettingsCard.Click(); // click category card
_fixture.ConnectionSettingsPage.WaitReady(10000); // wait for sub-page
```
