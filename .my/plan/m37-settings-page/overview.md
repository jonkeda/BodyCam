# M37 — Settings Page: Source Profiles & Smart Device Selection

**Status:** Proposed
**Goal:** Redesign the DeviceSettingsPage with a "Source Profile" concept that lets
users quickly select a bundled device preset (Phone, Laptop, HeyCyan Glasses, Meta
Glasses) or go Custom to pick Camera, Microphone, Speakers, and Buttons individually.
Support multiple simultaneously connected devices (multiple glasses, BT headsets,
USB cameras, etc.). Persist all device settings as a single JSON object instead of
scattered string keys. Add smart fallback logic that remembers preferences and
auto-selects the best available device.

**Depends on:** M27 (Settings UI Refactor), M33 (HeyCyan SDK), M36 (HeyCyan Windows)

---

## Why

Today the DeviceSettingsPage presents three flat pickers (Camera, Microphone, Speaker)
plus glasses connection. Users must understand provider IDs and manually reconfigure
every time their audio environment changes (e.g. plug in headphones, connect glasses,
switch from desk to mobile). This is especially painful on laptops where Bluetooth
headphones, wired earphones, and built-in devices all coexist.

A **Source Profile** collapses the common case into one dropdown while preserving
full control via a "Custom" mode. Smart fallback removes the need to reconfigure
when devices connect/disconnect.

---

## UX Design

### Page Layout

```
┌──────────────────────────────────────────┐
│  Connected Devices         [+ Connect]   │  ← always visible
│  ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄  │
│  ┌ HeyCyan Glasses ──────── 🔋 82% ──┐   │
│  │  MAC: D8:79:B8:7F:E6:C9          │   │
│  │  FW: 1.2.3   📷 12  🎬 3  🎙️ 5   │   │
│  │                     [Disconnect]  │   │
│  └───────────────────────────────────┘   │
│  ┌ AirPods Pro ──────── 🔋 65% ──────┐   │
│  │  Bluetooth A2DP + HFP             │   │
│  │                     [Disconnect]  │   │
│  └───────────────────────────────────┘   │
│  (each connected device gets a card)     │
├──────────────────────────────────────────┤
│  Source                                  │
│  ┌────────────────────────────────────┐  │
│  │ Phone             ▼               │  │  ← dropdown
│  └────────────────────────────────────┘  │
│                                          │
│  (Individual pickers hidden unless       │
│   Source = Custom)                       │
├──────────────────────────────────────────┤  ← only when Source = Custom
│  Camera Source                           │
│  ┌────────────────────────────────────┐  │
│  │ Phone Camera      ▼               │  │
│  └────────────────────────────────────┘  │
│  [Test Capture]                          │
│  ┌──────────────────────────────────┐    │
│  │  (captured image/video preview)  │    │  ← inline preview
│  └──────────────────────────────────┘    │
│                                          │
│  Microphone                              │
│  ┌────────────────────────────────────┐  │
│  │ Phone Microphone  ▼               │  │
│  └────────────────────────────────────┘  │
│  [Test Recording]                        │
│  Recording status: "Done — 47 chunks"    │
│                                          │
│  Speakers                                │
│  ┌────────────────────────────────────┐  │
│  │ Phone Speaker     ▼               │  │
│  └────────────────────────────────────┘  │
│  [Test Sound]                            │
│                                          │
│  Buttons                                 │
│  ┌────────────────────────────────────┐  │
│  │ (none)            ▼               │  │
│  └────────────────────────────────────┘  │
├──────────────────────────────────────────┤
│  Button Mappings                         │  ← per-device, dynamic
│  ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄  │
│  ▸ HeyCyan Glasses (1 button)            │  ← expandable per device
│    Tap:         [ToggleConversation ▼]   │
│    Double-Tap:  [Photo              ▼]   │
│    Long Press:  [EndSession         ▼]   │
│  ▸ BT Remote (3 buttons)                 │
│    Button 1 Tap:    [ToggleSession  ▼]   │
│    Button 1 Double: [Look           ▼]   │
│    Button 2 Tap:    [VolumeUp       ▼]   │
│    Button 3 Tap:    [VolumeDown     ▼]   │
│  (each connected button device gets      │
│   its own section with its own buttons)  │
└──────────────────────────────────────────┘
```

### Button Devices

Button input is decoupled from the Source profile. Any number of button
devices can be active simultaneously — a user might have glasses buttons
**and** a BT remote **and** keyboard shortcuts all active at once.

Each button device self-describes its capabilities:

| Device | Buttons | Gestures per button | Example |
|--------|---------|--------------------|---------|
| HeyCyan Glasses | 1 ("glasses-button") | Tap, DoubleTap, LongPress | Firmware gesture recognition |
| Meta Glasses | 1 ("meta-button") | Tap, DoubleTap, LongPress | Similar to HeyCyan |
| BT Remote (3-button) | 3 ("button-1", "button-2", "button-3") | Tap, DoubleTap, LongPress per button | BTHome or HID remote |
| BT Remote (volume) | 3 ("play", "vol-up", "vol-down") | Tap only | Media control remote |
| Keyboard | N (virtual) | Shortcut combos | Ctrl+Shift+L, etc. |
| Presentation clicker | 2 ("next", "prev") | Tap only | USB/BT presenter |

The mapping UI dynamically renders rows based on what buttons+gestures each
device reports — no hardcoded "3 rows for HeyCyan".

### Source Profile Dropdown Options

| Profile                   | Camera                 | Mic                 | Speaker                 | Buttons        | When Available                       |
| ------------------------- | ---------------------- | ------------------- | ----------------------- | -------------- | ------------------------------------ |
| **Phone**           | Phone Camera           | Phone/Platform Mic  | Phone/Platform Speaker  | —             | Always (Android/iOS)                 |
| **Laptop**          | Phone Camera           | System Mic          | System Speaker          | Keyboard       | Always (Windows)                     |
| **HeyCyan Glasses** | HeyCyan Glasses Camera | HeyCyan Glasses Mic | HeyCyan Glasses Speaker | HeyCyan Button | When glasses connected               |
| **Meta Glasses**    | Meta Glasses Camera    | Meta Glasses Mic    | Meta Glasses Speaker    | Meta Button    | When Meta glasses connected (future) |
| **Bluetooth Audio** | Phone/System Camera    | Bluetooth Mic       | Bluetooth Speaker       | —             | When BT audio device paired          |
| **Custom**          | (user picks)           | (user picks)        | (user picks)            | (user picks)   | Always                               |

**Platform-dependent labeling:**

- On Android/iOS: "Phone" shows as default
- On Windows: "Laptop" shows as default
- Profiles whose hardware is not connected are not shown.

### Bluetooth Audio Devices

Individual Bluetooth devices appear in the Custom picker lists alongside system
devices. Each connected BT device (headphones, earbuds, car audio, hearing aids)
shows as a selectable provider:

- Microphone picker: "AirPods Pro (Bluetooth)", "Jabra Elite (Bluetooth)", etc.
- Speaker picker: "AirPods Pro (Bluetooth)", "Jabra Elite (Bluetooth)", etc.
- Wired earphones also appear when plugged in (handled by platform audio routing)

### Selection behavior

- Switching the Source dropdown immediately activates the entire bundle
- Switching any individual picker automatically sets Source to "Custom"
- The selected profile and any custom selections are persisted across app restarts

---

## Smart Fallback Logic

### Priority Chain (highest to lowest)

```
1. User's saved preference (if device still available)
2. Connected smart glasses (HeyCyan/Meta) — if user has ever used them
3. Connected Bluetooth audio device — if previously selected
4. Wired headphones/earphones (detected via audio route change)
5. Platform default (Phone Mic + Phone Speaker / System Mic + System Speaker)
```

### Fallback Rules

| Event                      | Action                                                                                        |
| -------------------------- | --------------------------------------------------------------------------------------------- |
| App starts                 | Restore saved profile. If saved devices unavailable, walk priority chain down.                |
| Glasses connect            | If profile is "HeyCyan Glasses" or was before disconnect, auto-switch. Otherwise notify user. |
| Glasses disconnect         | Fall back: saved BT → wired → platform default.                                             |
| BT device connects         | If profile is "Bluetooth Audio" or Custom with this device saved, auto-switch.                |
| BT device disconnects      | Fall back to next in chain.                                                                   |
| Wired headphones plugged   | If on platform default, route audio through wired (OS handles this on most platforms).        |
| Wired headphones unplugged | Revert to platform speaker.                                                                   |

### Persistence Model — JSON-based DeviceSettings

All device-related settings are stored as a **single JSON object** under one
Preferences key (`DeviceSettings`). This replaces the scattered string keys
(`ActiveCameraProvider`, `ActiveAudioInputProvider`, `ActiveAudioOutputProvider`,
`LastHeyCyanDeviceAddress`, etc.) and supports multiple devices, lists, and
nested objects cleanly.

```csharp
/// <summary>
/// Persisted as JSON in a single Preferences key. Replaces all flat device keys.
/// </summary>
public sealed class DeviceSettings
{
    /// <summary>Active source profile ID ("phone", "heycyan-glasses", "custom", etc.).</summary>
    public string ActiveProfileId { get; set; } = "phone";

    /// <summary>Per-slot provider IDs when profile = "custom".</summary>
    public CustomSelection Custom { get; set; } = new();

    /// <summary>Currently active provider IDs (runtime state, set by profile or custom).</summary>
    public ActiveProviders Active { get; set; } = new();

    /// <summary>Known devices that should auto-reconnect.</summary>
    public List<KnownDevice> KnownDevices { get; set; } = [];

    /// <summary>Per-profile overrides (e.g. which specific BT device for "bluetooth" profile).</summary>
    public Dictionary<string, ProfileOverrides> ProfileSettings { get; set; } = new();
}

public sealed class CustomSelection
{
    public string? CameraProviderId { get; set; }
    public string? AudioInputProviderId { get; set; }
    public string? AudioOutputProviderId { get; set; }
    // Buttons are independent of profiles — not stored here.
    // Each button device has its own mappings in IButtonMappingStore.
}

public sealed class ActiveProviders
{
    public string? CameraProviderId { get; set; }
    public string? AudioInputProviderId { get; set; }
    public string? AudioOutputProviderId { get; set; }
    // Buttons are managed separately by ButtonInputManager — multiple can be active.
}

/// <summary>
/// A device the user has connected before. Supports multiple glasses, headsets, etc.
/// </summary>
public sealed class KnownDevice
{
    /// <summary>Stable identifier (BLE MAC, BT address, USB VID:PID, etc.).</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>User-visible name ("HeyCyan Glasses", "AirPods Pro", etc.).</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Device type for icon/grouping.</summary>
    public string DeviceType { get; set; } = ""; // "heycyan-glasses", "bluetooth-headset", "usb-camera", etc.

    /// <summary>Auto-reconnect on app start.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>Last successful connection timestamp (for sort order / LRU).</summary>
    public DateTime? LastConnected { get; set; }

    /// <summary>Device-specific settings (e.g. button mappings for glasses).</summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}

public sealed class ProfileOverrides
{
    /// <summary>Preferred device ID within this profile (e.g. which BT device for "bluetooth" profile).</summary>
    public string? PreferredDeviceId { get; set; }
}
```

**Single Preferences key:**
```csharp
// In SettingsService / ISettingsService:
public DeviceSettings DeviceSettings
{
    get => JsonSerializer.Deserialize<DeviceSettings>(
               Preferences.Get("DeviceSettings", "{}")) ?? new();
    set { lock (_prefsLock) Preferences.Set("DeviceSettings",
               JsonSerializer.Serialize(value)); }
}
```

**Migration from legacy keys:** Phase 1 reads the old flat keys
(`ActiveCameraProvider`, `LastHeyCyanDeviceAddress`, etc.), populates the JSON
object, persists it, and removes the old keys.

**Benefits over flat keys:**
- Multiple known devices stored as a list — no key-naming gymnastics
- Custom selection is a nested object — clear grouping
- Profile-specific overrides extensible via dictionary
- One atomic read/write instead of 6+ separate Preferences calls
- Easy to add fields for future device types without new ISettingsService members

---

## Software Architecture Changes

### New Types

#### `ISourceProfile` interface (plug-and-play)

No enum. Each source profile is a self-contained class that implements
`ISourceProfile`. Adding a new source (Meta Glasses, Xreal, etc.) means adding
one class and one DI registration — zero switches, zero enum values.

```csharp
/// <summary>
/// A bundled device configuration that maps to a single Source dropdown entry.
/// Implementations are discovered via DI (registered as IEnumerable<ISourceProfile>).
/// </summary>
public interface ISourceProfile
{
    /// <summary>Stable ID persisted in settings (e.g. "phone", "heycyan-glasses").</summary>
    string Id { get; }

    /// <summary>User-visible label (e.g. "Phone", "HeyCyan Glasses").</summary>
    string DisplayName { get; }

    /// <summary>Sort order in the dropdown (lower = higher).</summary>
    int Order { get; }

    /// <summary>True when all required devices for this profile are connected.</summary>
    bool IsAvailable { get; }

    /// <summary>Suffix shown when !IsAvailable (e.g. "(not connected)").</summary>
    string? UnavailableReason { get; }

    /// <summary>Priority for smart fallback (higher = preferred).</summary>
    int FallbackPriority { get; }

    /// <summary>Activate this profile's devices on the managers.</summary>
    Task ApplyAsync(CameraManager camera, AudioInputManager mic,
                    AudioOutputManager speaker, CancellationToken ct = default);

    /// <summary>Persist custom provider IDs (only meaningful for CustomSourceProfile).</summary>
    void SaveCustomSelections(ISettingsService settings) { }
}
```

#### Built-in implementations

| Class                      | Id                    | Registered on                        |
| -------------------------- | --------------------- | ------------------------------------ |
| `PhoneSourceProfile`     | `"phone"`           | Android, iOS                         |
| `LaptopSourceProfile`    | `"laptop"`          | Windows                              |
| `HeyCyanSourceProfile`   | `"heycyan-glasses"` | All (checks glasses connection)      |
| `BluetoothSourceProfile` | `"bluetooth"`       | All (checks BT device availability)  |
| `CustomSourceProfile`    | `"custom"`          | All (always available, lowest order) |

Future sources (Meta Glasses, Xreal, etc.) just add a new class + DI line.

#### `SourceProfileManager` service

Orchestrates the profile system. Sits between `DeviceViewModel` and the three
existing managers (`CameraManager`, `AudioInputManager`, `AudioOutputManager`).

```
DeviceViewModel
    └── SourceProfileManager (NEW)
            ├── IEnumerable<ISourceProfile>  (discovered via DI)
            ├── CameraManager     (existing)
            ├── AudioInputManager (existing)
            ├── AudioOutputManager(existing)
            └── IButtonProvider   (existing, optional)
```

Responsibilities:

- `ApplyProfileAsync(string profileId)` — find profile by ID → call `profile.ApplyAsync()`
- `AvailableProfiles` — all registered profiles, ordered, with availability status
- `HandleDeviceChanged()` — smart fallback: walk profiles by `FallbackPriority`
  descending, pick first `IsAvailable`
- Profile ↔ settings persistence (saves `profileId` string, not an enum)
- Raises `ProfileChanged` event for UI binding
- **No switches, no enum.** Profile resolution is polymorphic via `ISourceProfile`.

#### Example: adding Meta Glasses later

```csharp
// 1. Create one file
public sealed class MetaGlassesSourceProfile : ISourceProfile
{
    private readonly MetaGlassesManager _meta;
    public MetaGlassesSourceProfile(MetaGlassesManager meta) => _meta = meta;

    public string Id => "meta-glasses";
    public string DisplayName => "Meta Glasses";
    public int Order => 25;
    public bool IsAvailable => _meta.IsConnected;
    public string? UnavailableReason => IsAvailable ? null : "(not connected)";
    public int FallbackPriority => 90;

    public async Task ApplyAsync(CameraManager camera, AudioInputManager mic,
                                  AudioOutputManager speaker, CancellationToken ct)
    {
        await camera.SetActiveAsync("meta-glasses", ct);
        await mic.SetActiveAsync("meta-glasses", ct);
        await speaker.SetActiveAsync("meta-glasses", ct);
    }
}

// 2. Register in MauiProgram.cs
builder.Services.AddSingleton<ISourceProfile, MetaGlassesSourceProfile>();
// Done. No enum change, no switch update, no other files touched.
```

#### `IButtonInputProvider` extension — self-describing buttons

The existing `IButtonInputProvider` already uses `providerId` + `buttonId` keys.
M37 adds a `Buttons` property so each provider self-describes its physical buttons
and supported gestures. The settings UI reads this to dynamically render mapping rows
— no hardcoded "3 rows for HeyCyan".

```csharp
/// <summary>
/// Describes a single physical button on a device and which gestures it supports.
/// </summary>
public sealed record ButtonDescriptor(
    string ButtonId,
    string DisplayName,
    IReadOnlyList<ButtonGesture> SupportedGestures);
```

Added to `IButtonInputProvider`:
```csharp
/// <summary>
/// The buttons this device has, and which gestures each supports.
/// Used by the settings UI to dynamically render mapping rows.
/// </summary>
IReadOnlyList<ButtonDescriptor> Buttons { get; }
```

**Examples:**
```csharp
// HeyCyanButtonProvider — 1 button, 3 gestures
public IReadOnlyList<ButtonDescriptor> Buttons { get; } = [
    new("glasses-button", "Glasses Button",
        [ButtonGesture.SingleTap, ButtonGesture.DoubleTap, ButtonGesture.LongPress])
];

// 3-button BT remote — 3 buttons with varying gestures
public IReadOnlyList<ButtonDescriptor> Buttons { get; } = [
    new("button-1", "Top Button",
        [ButtonGesture.SingleTap, ButtonGesture.DoubleTap, ButtonGesture.LongPress]),
    new("vol-up",   "Volume Up",   [ButtonGesture.SingleTap]),
    new("vol-down", "Volume Down", [ButtonGesture.SingleTap]),
];

// Presentation clicker — 2 buttons, tap only
public IReadOnlyList<ButtonDescriptor> Buttons { get; } = [
    new("next", "Next",     [ButtonGesture.SingleTap]),
    new("prev", "Previous", [ButtonGesture.SingleTap]),
];
```

#### Extended enums

```csharp
public enum ButtonGesture
{
    SingleTap,
    DoubleTap,
    TripleTap,           // NEW — some remotes support this
    LongPress,
    LongPressRelease,
}

public enum ButtonAction
{
    None,
    Look,
    Read,
    Find,
    ToggleSession,
    ToggleConversation,
    EndSession,
    Photo,
    ToggleSleepActive,
    PushToTalk,
    VolumeUp,            // NEW
    VolumeDown,          // NEW
    Mute,                // NEW
    NextTrack,           // NEW — for media remotes
    PreviousTrack,       // NEW
}
```

Button devices are **independent of Source profiles** — multiple can be active
simultaneously (glasses buttons + BT remote + keyboard shortcuts). They are not
part of the profile dropdown; they appear in a dedicated "Button Mappings" section
at the bottom of the Device Settings page.

### Modified Types

#### `DeviceViewModel`

- Replace individual provider pickers with profile-driven UI
- Add `SelectedProfile` / `AvailableProfiles` / `IsCustomMode` properties
- Individual pickers (`SelectedCameraProvider`, etc.) only visible when `IsCustomMode`
- When user changes an individual picker, auto-set profile to Custom
- Delegate auto-select/fallback to `SourceProfileManager` instead of inline logic
- **Test buttons inline with their picker** (not grouped at bottom):
  - Test Capture + capture preview image below Camera picker
  - Test Recording + status label below Microphone picker
  - Test Sound below Speaker picker
- **Capture preview:** `LastCaptureImage` (ImageSource) displayed in an `Image`
  control below Test Capture. For video, show a thumbnail frame with a duration badge.

#### `DeviceSettingsPage.xaml`

- Add Source profile dropdown above individual pickers
- Wrap individual pickers in a section that is visible only when `IsCustomMode`
- **Each picker section includes its own test button + result display:**
  ```
  Camera Source [Picker]
  [Test Capture]
  [Captured image/video preview — AspectFit, HeightRequest=200]

  Microphone [Picker]
  [Test Recording]
  "Done — 47 chunks recorded"

  Speaker [Picker]
  [Test Sound]
  ```
- `IsVisible` bindings to `IsCustomMode`
- Capture preview: `Image` bound to `GlassesCameraSection.LastCaptureImage`,
  plus `MediaElement` or thumbnail for video captures
- **Button Mappings section:** dynamic per-device, replaces hardcoded HeyCyan mapping
  - Each connected button device gets an expandable section header
  - Inside: one row per button × gesture, each with a `ButtonAction` picker
  - Devices appear/disappear as they connect/disconnect

#### `SettingsService`

- Add single `DeviceSettings` JSON property (replaces flat device keys)
- Add `DeviceSettings` model classes (`DeviceSettings`, `CustomSelection`,
  `ActiveProviders`, `KnownDevice`, `ProfileOverrides`)
- Migration: read old flat keys → build `DeviceSettings` → persist → remove old keys

### Platform Considerations

| Platform          | Default Profile | BT Audio Discovery                                          | Wired Detection                     | Notes                                       |
| ----------------- | --------------- | ----------------------------------------------------------- | ----------------------------------- | ------------------------------------------- |
| **Windows** | Laptop          | NAudio device enumeration                                   | NAudio device change events         | System Mic/Speaker = default audio endpoint |
| **Android** | Phone           | `AudioManager.getDevices()` + `AudioDeviceCallback`     | `ACTION_HEADSET_PLUG` broadcast   | Phone Mic/Speaker = built-in                |
| **iOS**     | Phone           | `AVAudioSession.currentRoute` + route-change notification | `AVAudioSession` route monitoring | iPhone Mic/Speaker = built-in               |

BT device enumeration is already partially handled by `BluetoothAudioInputProvider`
and `BluetoothAudioOutputProvider`. M37 extends this to:

1. Enumerate *all* connected BT audio devices (not just a single generic one)
2. Show device names in pickers (AirPods, Jabra, etc.)
3. Track preferred BT device for auto-reconnect

---

## Phases

### Phase 1 — DeviceSettings Model, JSON Persistence & Migration

**Status:** Proposed

- [ ] Define `DeviceSettings`, `CustomSelection`, `ActiveProviders`, `KnownDevice`, `ProfileOverrides` in `Models/`
- [ ] Add `DeviceSettings` JSON property to `ISettingsService` / `SettingsService`
- [ ] Migration: read old flat keys (`ActiveCameraProvider`, `ActiveAudioInputProvider`, `ActiveAudioOutputProvider`, `LastHeyCyanDeviceAddress`, `LastHeyCyanDeviceName`, `HeyCyanAutoReconnect`) → populate `DeviceSettings` → persist → remove old keys
- [ ] Define `ISourceProfile` interface in `Services/`
- [ ] Implement `PhoneSourceProfile` (Android/iOS default)
- [ ] Implement `LaptopSourceProfile` (Windows default)
- [ ] Implement `HeyCyanSourceProfile` (checks glasses connection state)
- [ ] Implement `BluetoothSourceProfile` (checks BT device availability)
- [ ] Implement `CustomSourceProfile` (always available, delegates to per-device pickers)
- [ ] Unit tests for JSON round-trip, migration, and each profile's `IsAvailable` / `ApplyAsync`

### Phase 2 — SourceProfileManager Service

**Status:** Proposed

- [ ] Implement `SourceProfileManager` class
- [ ] Accept `IEnumerable<ISourceProfile>` via DI — no hardcoded list
- [ ] `ApplyProfileAsync(string profileId)` — find by ID, call `ApplyAsync()`
- [ ] `AvailableProfiles` — ordered, with availability + unavailable reason
- [ ] `HandleDeviceChanged()` — walk by `FallbackPriority` descending
- [ ] `ProfileChanged` event for ViewModel binding
- [ ] DI registration on all platforms (each platform registers its applicable profiles)
- [ ] Unit tests for profile resolution, fallback chain, and dynamic profile addition

### Phase 3 — DeviceViewModel Refactor

**Status:** Proposed

- [ ] Add `SelectedProfile` / `AvailableProfiles` / `IsCustomMode` properties
- [ ] Bind profile dropdown to `SourceProfileManager`
- [ ] When profile ≠ Custom: hide individual pickers, apply profile bundle
- [ ] When profile = Custom: show individual pickers with inline test buttons
- [ ] When individual picker changes: auto-set profile to Custom
- [ ] Remove inline auto-select/fallback logic (delegate to `SourceProfileManager`)
- [ ] Capture preview: expose `LastCaptureImage` (ImageSource) and `LastCaptureIsVideo` (bool)
- [ ] Unit tests for ViewModel state transitions

### Phase 4 — DeviceSettingsPage XAML Redesign

**Status:** Proposed

- [ ] Add Source profile `Picker` below Connected Devices panel
- [ ] Wrap Camera/Mic/Speaker/Button pickers in section visible only when `IsCustomMode`
- [ ] **Test buttons below their respective pickers:**
  - Test Capture + captured image/video preview below Camera picker
  - Test Recording + status label below Microphone picker
  - Test Sound below Speaker picker
- [ ] Capture preview: `Image` control (AspectFit, HeightRequest=200) bound to `LastCaptureImage`
- [ ] Video capture: show thumbnail frame with duration badge
- [ ] Gray out unavailable profiles in dropdown (suffix with `UnavailableReason`)
- [ ] **Dynamic Button Mappings section:**
  - Replace hardcoded `HeyCyanButtonMappingsViewModel` with generic `ButtonDeviceMappingsViewModel`
  - One expandable section per connected `IButtonInputProvider`
  - Dynamically render rows from `provider.Buttons` × `button.SupportedGestures`
  - Each row: "{DeviceName} → {ButtonName} → {Gesture}: [{ActionPicker}]"
  - Works for any device: 1-button glasses, 3-button remote, media clicker, keyboard
- [ ] Verify layout on Windows, Android, iOS
- [ ] Update UITest page objects

### Phase 5 — Generalized Button Input Architecture

**Status:** Proposed

- [ ] Add `ButtonDescriptor` record and `Buttons` property to `IButtonInputProvider`
- [ ] Extend `ButtonGesture` enum: add `TripleTap`
- [ ] Extend `ButtonAction` enum: add `VolumeUp`, `VolumeDown`, `Mute`, `NextTrack`, `PreviousTrack`
- [ ] Implement `ButtonDescriptor` on `HeyCyanButtonProvider` (1 button, 3 gestures)
- [ ] Implement `ButtonDescriptor` on `KeyboardShortcutProvider`
- [ ] Create generic `ButtonDeviceMappingsViewModel` that reads `provider.Buttons` dynamically
- [ ] Rename/retire `HeyCyanGestureRowViewModel` → generic `GestureRowViewModel`
- [ ] Rename/retire `HeyCyanButtonMappingsViewModel` → generic `ButtonDeviceMappingsViewModel`
- [ ] `HeyCyanButtonDefaults.SeedDefaults` stays as seed logic, but UI is now generic
- [ ] Support standalone BT remote providers (button-only devices, no camera/mic/speaker)
- [ ] Unit tests for dynamic button descriptor rendering
- [ ] Unit tests for new gesture/action types

### Phase 6 — Multi-Device Support & Enhanced Enumeration

**Status:** Proposed

- [ ] **Connected Devices panel:** list all connected devices (glasses, BT headsets, USB cameras) as cards
- [ ] **[+ Connect] button:** opens device-type chooser (Glasses, Bluetooth Audio, USB Camera, etc.)
- [ ] **KnownDevices list:** persist all previously connected devices; auto-reconnect on app start
- [ ] **Multiple glasses:** support 2+ glasses connected simultaneously (user picks which for which slot)
- [ ] **Multiple BT devices:** each connected BT device registers its own providers (not one generic)
- [ ] **Windows:** Enumerate BT audio devices via NAudio/WinRT, register as individual providers
- [ ] **Android:** Use `AudioManager.getDevices(GET_DEVICES_OUTPUTS)` + `AudioDeviceCallback` for hot-plug
- [ ] **iOS:** Use `AVAudioSession` port descriptions for connected BT devices
- [ ] Show device-specific names in pickers (not generic "Bluetooth")
- [ ] Auto-switch when preferred device (from `KnownDevices`) reconnects
- [ ] Handle wired earphone detection (platform-specific)
- [ ] Integration tests with mock multi-device scenarios

### Phase 7 — Smart Fallback & Auto-Selection

**Status:** Proposed

- [ ] Implement priority-chain fallback in `SourceProfileManager`
- [ ] On app start: restore profile → verify devices → fallback if needed
- [ ] On glasses connect: auto-upgrade if user has glasses profile history
- [ ] On glasses disconnect: cascade down priority chain
- [ ] On BT connect/disconnect: smart switch or maintain current
- [ ] Toast/notification when auto-switching (inform user what happened)
- [ ] Edge case: multiple BT devices → prefer last-used
- [ ] Integration tests for all connect/disconnect scenarios

---

## Design Principles

1. **One-tap common case.** Most users pick a source profile once and forget.
2. **Full control available.** Power users switch to Custom for per-device control.
3. **No silent confusion.** When the app auto-switches devices, show a brief toast.
4. **Remember everything.** Profile + custom selections + preferred BT device all persist.
5. **Graceful degradation.** If nothing matches, platform default always works.
6. **Cross-platform parity.** Same UX on Windows, Android, iOS — only labels differ.
7. **Plug-and-play extensibility.** New source = one `ISourceProfile` class + one DI
   registration. No enum values, no switch statements, no touching existing code.

---

## Current Provider Inventory

| Provider ID           | Display Name            | Type      | Platform    |
| --------------------- | ----------------------- | --------- | ----------- |
| `phone`             | Phone Camera            | Camera    | All         |
| `heycyan-glasses`   | HeyCyan Glasses Camera  | Camera    | All         |
| `platform`          | Phone/System Microphone | Audio In  | All         |
| `heycyan-glasses`   | HeyCyan Glasses Mic     | Audio In  | All         |
| `bluetooth-generic` | Bluetooth (device name) | Audio In  | All         |
| `windows-speaker`   | System Speaker          | Audio Out | Windows     |
| `phone-speaker`     | Phone Speaker           | Audio Out | Android/iOS |
| `heycyan-glasses`   | HeyCyan Glasses Speaker | Audio Out | All         |
| `bluetooth-generic` | Bluetooth (device name) | Audio Out | All         |
| `keyboard`          | Keyboard Shortcuts      | Buttons   | Windows     |
| `heycyan-button`    | HeyCyan Glasses Button  | Buttons   | All         |

### Button Provider Capabilities

Existing `IButtonInputProvider` already supports multi-provider and multi-button
via `providerId` + `buttonId` composite keys in `ButtonGestureEvent` and
`IButtonMappingStore`. M37 extends this by:

1. Adding `ButtonDescriptor` — each provider self-describes its buttons + supported gestures
2. The settings UI reads `provider.Buttons` to dynamically render mapping rows
3. No more hardcoded "3 rows for HeyCyan" — any device, any button count, any gesture set
4. Button devices are independent of Source profiles — multiple can be active simultaneously
5. A standalone BT remote (no camera/mic/speaker) is just another `IButtonInputProvider` + DI registration
