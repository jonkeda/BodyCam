# Phase 4 — Color Contrast & High Contrast Mode

Ensure all text/background pairs meet WCAG 2.1 AA contrast ratios and add a high
contrast theme for Windows High Contrast mode.

---

## Why

Several text colors in the app fail WCAG AA (4.5:1 for normal text, 3:1 for large
text). The MAUI literal `Gray` (#808080) against white or near-white backgrounds
gives only ~3.9:1 contrast, insufficient for caption-sized text. Users with low
vision or color blindness cannot read these elements.

---

## Files Changed

| File | Change |
|------|--------|
| `MainPage.xaml` | Replace `TextColor="Gray"` with accessible alternatives |
| `SettingsPage.xaml` | Replace `TextColor="Gray"` with accessible alternatives |
| `Colors.xaml` | Add accessible gray tokens if needed |
| `TranscriptEntry.cs` | Audit `RoleColor` values for contrast |
| `MainViewModel.cs` | Audit `StateColor` values for contrast |
| `Resources/Styles/HighContrast.xaml` | New — high contrast resource dictionary |
| `App.xaml` | Conditional merge of high contrast resources |

---

## Current Contrast Audit

### Passing Pairs ✅

| Text Color | Background | Ratio | WCAG AA |
|------------|------------|-------|---------|
| `#333333` | `#EEEEEE` (light ActionButton) | 10.1:1 | ✅ Pass |
| `#E0E0E0` | `#2A2A2A` (dark ActionButton) | 9.5:1 | ✅ Pass |
| `#333` | `#E0E0E0` (light Clear/Toggle) | 8.5:1 | ✅ Pass |
| `#E0E0E0` | `#333` (dark Clear/Toggle) | 8.5:1 | ✅ Pass |
| `White` | `#512BD4` (primary buttons) | 8.6:1 | ✅ Pass |
| `White` | `#7C4DFF` (dark primary) | 4.6:1 | ✅ Pass (barely) |
| `White` | `#2196F3` (active tab bg) | 3.3:1 | ⚠️ Passes for large text only |

### Failing Pairs ❌

| Element | Text Color | Background | Ratio | Issue |
|---------|------------|------------|-------|-------|
| Image caption (MainPage) | `Gray` (#808080) | White (#FFFFFF) | 3.9:1 | ❌ Fails normal text |
| Camera placeholder | `Gray` | White | 3.9:1 | ❌ Fails |
| Debug log | `Gray` | `#F5F5F5CC` (light) | ~3.7:1 | ❌ Fails |
| Debug log | `Gray` | `#1A1A1ACC` (dark) | ~3.3:1 | ❌ Fails |
| Settings note | `Gray` | White | 3.9:1 | ❌ Fails |
| Settings field labels | `Gray` | White | 3.9:1 | ❌ Fails |
| Settings helper text | `Gray` | White | 3.9:1 | ❌ Fails |
| Tool description | `Gray` | White | 3.9:1 | ❌ Fails |
| Inactive tab text | `#333` | `#F0F0F0` bg | 9.1:1 | ✅ OK |

### Needs Audit ❓

| Element | Color | Notes |
|---------|-------|-------|
| `RoleColor` "You" | `#4CAF50` | Green on white = 3.0:1 ❌ |
| `RoleColor` "AI" | `#2196F3` | Blue on white = 3.0:1 ❌ |
| `RoleColor` "System" | `#999999` | Gray on white = 2.8:1 ❌ |
| `StateColor` Sleep | `#666666` | On `#FAFAFA` = 5.7:1 ✅ |
| `StateColor` WakeWord | `#4CAF50` | On `#FAFAFA` = 3.0:1 ❌ dot only (graphical) |
| `StateColor` Active | `#2196F3` | On `#FAFAFA` = 3.1:1 ❌ dot only (graphical) |

---

## Fixes

### TextColor="Gray" → Accessible Gray

Replace all `TextColor="Gray"` with an `AppThemeBinding` using accessible
alternatives:

```xml
<!-- Before -->
<Label TextColor="Gray" ... />

<!-- After — light needs darker gray, dark needs lighter gray -->
<Label TextColor="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray300}}" ... />
```

- `Gray600` = `#404040` → on White = 9.7:1 ✅
- `Gray300` = `#ACACAC` → on `#1A1A1A` = 6.1:1 ✅

Both pass WCAG AA comfortably, even for small (caption) text.

### Affected Controls — MainPage

| Control | Line (approx) |
|---------|---------------|
| Image caption label | `TextColor="Gray"` in transcript DataTemplate |
| Camera placeholder label | `TextColor="Gray"` |
| Debug label | `TextColor="Gray"` |

### Affected Controls — SettingsPage

| Control | Line (approx) |
|---------|---------------|
| Note label | `TextColor="Gray"` |
| All field labels (15+) | `TextColor="Gray"` |
| Tool description labels | `TextColor="Gray"` |
| Helper text | `TextColor="Gray"` |

### Transcript RoleColor Fix

The transcript text uses `RoleColor` from `TranscriptEntry.cs`:

```csharp
// Before
public Color RoleColor => Role switch
{
    "You" => Color.FromArgb("#4CAF50"),    // 3.0:1 on white ❌
    "AI" => Color.FromArgb("#2196F3"),     // 3.0:1 on white ❌
    _ => Color.FromArgb("#999999")         // 2.8:1 on white ❌
};

// After — darkened for light theme, but RoleColor used on both themes
// Use darker variants that pass on both white and dark backgrounds
public Color RoleColor => Role switch
{
    "You" => Color.FromArgb("#2E7D32"),    // Dark green: 5.9:1 on white ✅
    "AI" => Color.FromArgb("#1565C0"),     // Dark blue: 6.4:1 on white ✅
    _ => Color.FromArgb("#616161")         // Dark gray: 5.3:1 on white ✅
};
```

**Caveat:** These darker colors on dark theme backgrounds (`#1A1A1A`-ish) need
checking too:
- `#2E7D32` on `#1A1A1A` ≈ 3.6:1 — borderline for body text
- `#1565C0` on `#1A1A1A` ≈ 2.9:1 — fails

**Better approach:** Make `RoleColor` theme-aware. Add an `Application.Current`
theme check, or expose two colors and use `AppThemeBinding` in XAML instead of
the C# property:

```csharp
public Color RoleColor => (Role, IsLightTheme) switch
{
    ("You", true)  => Color.FromArgb("#2E7D32"),   // dark green on light
    ("You", false) => Color.FromArgb("#81C784"),   // light green on dark
    ("AI", true)   => Color.FromArgb("#1565C0"),   // dark blue on light
    ("AI", false)  => Color.FromArgb("#64B5F6"),   // light blue on dark
    (_, true)      => Color.FromArgb("#616161"),   // dark gray on light
    (_, false)     => Color.FromArgb("#BDBDBD"),   // light gray on dark
};

private static bool IsLightTheme =>
    Application.Current?.RequestedTheme == AppTheme.Light;
```

Contrast checks:
| Color | On White | On #1A1A1A | 
|-------|----------|------------|
| `#2E7D32` | 5.9:1 ✅ | — |
| `#81C784` | — | 5.5:1 ✅ |
| `#1565C0` | 6.4:1 ✅ | — |
| `#64B5F6` | — | 5.1:1 ✅ |
| `#616161` | 5.3:1 ✅ | — |
| `#BDBDBD` | — | 7.2:1 ✅ |

### State Dot Colors

The state dot (`StateColor`) is a graphical element (12×12 colored circle), not
text. WCAG requires 3:1 for non-text graphical elements. Current values:

- Sleep `#666666` on `#FAFAFA` = 5.7:1 ✅
- WakeWord `#4CAF50` on `#FAFAFA` = 3.0:1 — borderline
- Active `#2196F3` on `#FAFAFA` = 3.1:1 — borderline

These pass the 3:1 non-text threshold. Leave as-is unless testing reveals they're
hard to distinguish.

### Active Tab Background

White text on `#2196F3` = 3.3:1 — passes for large text (the tab buttons use
`FontSize="Body"` ≈ 14pt, which is not large). Options:
- Darken to `#1976D2` → 4.6:1 ✅
- Use bold text on active tab (bold 14pt = large text per WCAG)

Recommend darkening:

```csharp
// MainViewModel.cs
private static readonly Color TabActiveBg = Color.FromArgb("#1976D2");
```

---

## High Contrast Theme

### Windows High Contrast Detection

MAUI on Windows doesn't automatically respond to High Contrast mode. We need to
detect it and apply alternative resources.

In `App.xaml.cs` or `MauiProgram.cs`:

```csharp
#if WINDOWS
var uiSettings = new Windows.UI.ViewManagement.UISettings();
var isHighContrast = uiSettings.AdvancedEffectsEnabled == false; // approximation

// Better: check AccessibilitySettings
var a11y = new Windows.UI.ViewManagement.AccessibilitySettings();
if (a11y.HighContrast)
{
    Resources.MergedDictionaries.Add(new Resources.Styles.HighContrast());
}
#endif
```

### HighContrast.xaml

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">

    <!-- Override key colors for maximum contrast -->
    <Color x:Key="Primary">Yellow</Color>
    <Color x:Key="PrimaryDark">Yellow</Color>

    <!-- Text: white on black -->
    <Color x:Key="Gray100">#FFFFFF</Color>
    <Color x:Key="Gray200">#FFFFFF</Color>
    <Color x:Key="Gray300">#CCCCCC</Color>
    <Color x:Key="Gray400">#CCCCCC</Color>
    <Color x:Key="Gray500">#FFFFFF</Color>
    <Color x:Key="Gray600">#FFFFFF</Color>
    <Color x:Key="Gray900">#000000</Color>
    <Color x:Key="Gray950">#000000</Color>
</ResourceDictionary>
```

This is a **minimal** high contrast dictionary. Full implementation depends on
how many controls use `StaticResource` vs inline colors. The inline colors
(`AppThemeBinding Light=#333, Dark=#E0E0E0`) won't be overridden by this
dictionary — those would need to be migrated to `StaticResource` references in a
future pass.

### Android High Contrast Text

Android's "High contrast text" adds stroke outlines to text. This works
automatically with no code changes. **Verify only.**

---

## Testing

### Automated Contrast Verification

No automated tooling in the build pipeline. Rely on manual checks with a contrast
calculator (e.g. WebAIM Contrast Checker) for each color pair.

### Manual Testing

1. **Windows Light theme:** Verify all caption/label text is readable
2. **Windows Dark theme:** Verify all caption/label text is readable
3. **Windows High Contrast:** Enable High Contrast Black → verify text visible
4. **Android default:** Verify field labels are readable
5. **Android High Contrast text:** Enable → verify text has visible outlines
6. **Transcript entries:** Verify user (green) and AI (blue) text readable on both themes

---

## Exit Criteria

- Zero `TextColor="Gray"` in XAML — all replaced with `Gray600`/`Gray300` AppThemeBinding
- `RoleColor` passes 4.5:1 on both light and dark backgrounds
- `TabActiveBg` darkened to `#1976D2` (4.6:1 with white text)
- `HighContrast.xaml` exists and is conditionally merged on Windows
- All text/background pairs documented with contrast ratios
- Manual testing passed on Windows (light + dark + high contrast) and Android
