# M31 — State Redesign

## Goal

Redesign the status bar to be cleaner and more intuitive:

1. **Rename buttons:** Sleep → Off, Listen → On, Active → Listening
2. **Show state text** next to the selected segment button
3. **Remove** the status dot (color blob)
4. **Hide** the ActivityIndicator (busy spinner)
5. **Remove** the debug toggle button (🐛)
6. **Replace** "Clear" text button with a trash icon

## Icons

Four SVG vector icons in `Resources/Images/`:

| File | Purpose | Style |
|------|---------|-------|
| `mic_off.svg` | Off state | Grey mic + red diagonal slash |
| `mic_on.svg` | On state (passive) | Purple mic + soft halo ring |
| `mic_active.svg` | Listening state | Purple mic + radiating sound waves |
| `trash.svg` | Clear button | Grey trash can outline |

## Changes

### StatusBarView.xaml
- Remove `StatusDot` ellipse
- Remove `BusyIndicator` ActivityIndicator
- Remove `DebugToggleButton`
- Rename button AutomationIds: `SleepButton` → `OffButton`, `ListenButton` → `OnButton`, `ActiveButton` → `ListeningButton`
- Replace emoji text with `Image` source from SVG icons
- Add label showing state text next to the selected button
- Replace `ClearButton` text "Clear" with trash icon image

### MainViewModel.cs
- Rename segment color properties: `SleepSegment*` → `OffSegment*`, `ListenSegment*` → `OnSegment*`, `ActiveSegment*` → `ListeningSegment*`
- Add `SelectedStateText` property showing "Off" / "On" / "Listening"
- Update `SetStateCommand` parameter strings
- Remove `ToggleDebugCommand` (keep debug functionality accessible from settings)

### UI Tests
- Update `MainPage.cs` page object: rename button properties
- Update `StatusBarTests.cs`: rename test methods
- Remove `DebugToggleButton` tests from `DebugOverlayTests.cs`
