# M23 Phase 1 — SetupPage & ViewModel

**Goal:** Create a step-by-step setup wizard that handles permissions and API key configuration.

---

## Files to Create

### `ViewModels/SetupViewModel.cs`

ViewModel for the setup wizard. Manages a list of setup steps and tracks progress.

**Properties:**
- `CurrentStep` (int) — 0-based index into steps
- `Steps` (ObservableCollection\<SetupStep\>) — all setup steps with status
- `CurrentTitle` (string) — heading for current step
- `CurrentDescription` (string) — explanation text for current step
- `CurrentIcon` (string) — emoji icon for current step
- `CurrentStatus` (string) — "pending" / "granted" / "denied" / "skipped"
- `IsLastStep` (bool) — computed, true when on final step
- `ShowApiKeyEntry` (bool) — true when current step is API key
- `ShowProviderPicker` (bool) — true when current step is API key
- `ApiKey` (string) — bound to entry field
- `IsOpenAi` / `IsAzure` (bool) — provider radio buttons
- `StatusMessage` (string) — feedback text after validation
- `IsValidating` (bool) — shows activity indicator during API check

**Commands:**
- `RequestPermissionCommand` — requests the current permission, updates step status
- `NextCommand` — advances to next step (or finishes)
- `SkipCommand` — skips current step with warning
- `ValidateKeyCommand` — tests API key, shows result
- `OpenSettingsCommand` — opens OS app settings (for "Don't ask again" scenario)

**Setup Steps (Android):**
1. Microphone — `Permissions.Microphone` — "BodyCam needs your microphone for voice conversations with the AI assistant."
2. Camera — `Permissions.Camera` — "BodyCam uses your camera to see and describe what's around you."  
3. Bluetooth — `Permissions.Bluetooth` — "BodyCam can connect to Bluetooth audio devices like smart glasses."
4. API Key — no permission, shows key entry UI — "Enter your OpenAI or Azure OpenAI API key to enable AI features."

**Setup Steps (Windows):**
1. API Key only — permissions are not needed on Windows.

**Logic:**
- On construction, check which permissions are already granted → mark those steps as "granted" and auto-skip
- `RequestPermissionCommand`: call `Permissions.RequestAsync`, update step status
- `NextCommand`: if all remaining steps are granted/skipped, finish. Otherwise advance.
- `ValidateKeyCommand`: save key via `IApiKeyService.SetApiKeyAsync`, then make a lightweight HTTP call to verify.
- On finish: set `ISettingsService.SetupCompleted = true`, raise `SetupFinished` event.

**Constructor deps:** `IApiKeyService`, `ISettingsService`, `AppSettings`

---

### `Models/SetupStep.cs`

Simple model for a setup step.

```csharp
public class SetupStep : ObservableObject
{
    public string Title { get; init; }
    public string Description { get; init; }
    public string Icon { get; init; }
    public SetupStepKind Kind { get; init; }  // Permission or ApiKey
    public string Status { get; set; }         // "pending", "granted", "denied", "skipped"
}

public enum SetupStepKind { Permission, ApiKey }
```

---

### `SetupPage.xaml` + `SetupPage.xaml.cs`

Single-page wizard with a card showing the current step.

**XAML Structure:**
```
ScrollView
  VerticalStackLayout (centered, padded)
    Label "BodyCam Setup" (title)
    Label "{CurrentStep + 1} of {Steps.Count}" (progress)
    
    Frame/Border (card, rounded corners)
      VerticalStackLayout
        Label {CurrentIcon} (large emoji)
        Label {CurrentTitle} (heading)
        Label {CurrentDescription} (body text)
        
        <!-- Permission step -->
        Button "Grant Permission" (bound to RequestPermissionCommand, visible when Kind=Permission)
        Label {CurrentStatus} (✓ Granted / ✕ Denied)
        Button "Open Settings" (visible when denied + can't re-ask)
        
        <!-- API Key step -->
        VerticalStackLayout (visible when Kind=ApiKey)
          RadioButton "OpenAI" / "Azure"
          Entry placeholder="sk-..." (bound to ApiKey)
          Button "Validate" (bound to ValidateKeyCommand)
          ActivityIndicator (bound to IsValidating)
          Label {StatusMessage}
    
    HorizontalStackLayout (buttons)
      Button "Skip" (bound to SkipCommand)
      Button "Next" / "Get Started" (bound to NextCommand)
```

**Code-behind:** Minimal — just constructor with DI injection of `SetupViewModel`, sets `BindingContext`.

---

## Patterns to Follow

- Inherit `SetupViewModel` from `ViewModelBase`
- Use `SetProperty(ref _field, value)` for all property setters
- Use `AsyncRelayCommand` for async commands
- Use `RelayCommand` for sync commands
- No CommunityToolkit.Mvvm
- `SetupStep` inherits from `ObservableObject` for bindable Status
