# RCA-01: Page Layout Issues

## Problem

The Device Settings page layout does not match expected UX. Specific issues:

### 1a. Connected glasses info should be in an expandable panel

**Current:** The glasses connection info (battery, MAC, firmware, hardware, media counts, disconnect button) is always fully visible when glasses are connected.

**Expected:** This section should be collapsible/expandable — users don't need to see all details all the time. The panel should include:
- Battery/charging status (header, always visible)
- MAC, Firmware, Hardware (expandable detail)
- Photo/Video/Audio counts (expandable detail)
- Disconnect button (inside the panel)
- HeyCyan audio endpoint controls (inside the panel)

### 1b. Button mappings should be at the bottom in expandable panels

**Current:** Button mappings (`ButtonDeviceMappings` CollectionView) are rendered inline with no expand/collapse, positioned after the audio endpoint section.

**Expected:** 
- Button mappings should be at the **bottom** of the page
- Each button group (e.g. "HeyCyan Glasses Button", "Keyboard Shortcuts") should be its own expandable panel
- Collapsed by default — user opens the one they want to configure

### 1c. Keyboard shortcuts should only show Single Tap gesture

**Current:** `KeyboardShortcutProvider.Buttons` declares 3 gestures per button (`SingleTap`, `DoubleTap`, `LongPress`), but keyboard keys only meaningfully support single-tap (key press = action).

**Expected:** Keyboard shortcut buttons should only expose `SingleTap` in their `SupportedGestures` list. Double-tap and long-press are not meaningful for keyboard shortcuts.

## Root Cause

1. No expandable/collapsible panel control used in the XAML — everything is statically visible
2. Button mappings section placed mid-page rather than bottom
3. `KeyboardShortcutProvider.Buttons` hardcodes 3 gestures per key without considering that keyboard keys don't support multi-gesture

## Fix

1. Wrap glasses info and button groups in `Expander` controls (MAUI Community Toolkit or custom)
2. Move button mappings `CollectionView` to the bottom of the `VerticalStackLayout`
3. Change `KeyboardShortcutProvider.Buttons` to return only `ButtonGesture.SingleTap` in `SupportedGestures`

## Files to Change

- `src/BodyCam/Pages/Settings/DeviceSettingsPage.xaml` — restructure layout with expanders
- `src/BodyCam/Platforms/Windows/Input/KeyboardShortcutProvider.cs` — reduce gestures to SingleTap only
- Possibly add `CommunityToolkit.Maui` Expander or implement a simple collapsible panel
