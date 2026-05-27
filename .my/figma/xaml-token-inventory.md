# XAML Token Inventory

**Generated:** 2026-05-21  
**Updated:** 2026-05-21 — Hardcoded colors extracted into semantic resources in Colors.xaml

---

## 1. Resource Colors (Colors.xaml)

### Brand
| Name | Hex | Usage |
|------|-----|-------|
| `Primary` | `#512BD4` | Buttons, accents, progress bars, sliders |
| `PrimaryDark` | `#AC99EA` | Dark-mode buttons and accents |
| `PrimaryDarkText` | `#242424` | Dark-mode button text |
| `Secondary` | `#DFD8F7` | Light purple tint |
| `SecondaryDarkText` | `#9880E5` | Dark-mode secondary text |
| `Tertiary` | `#2B0B98` | Deep blue-purple |

### Achromatic
| Name | Hex |
|------|-----|
| `White` | `#FFFFFF` |
| `Black` | `#000000` |
| `OffBlack` | `#1F1F1F` |

### Accent
| Name | Hex |
|------|-----|
| `Magenta` | `#D600AA` |
| `MidnightBlue` | `#190649` |

### Gray Scale
| Name | Hex |
|------|-----|
| `Gray100` | `#E1E1E1` |
| `Gray200` | `#C8C8C8` |
| `Gray300` | `#ACACAC` |
| `Gray400` | `#919191` |
| `Gray500` | `#6E6E6E` |
| `Gray600` | `#404040` |
| `Gray900` | `#212121` |
| `Gray950` | `#141414` |

**Brush resources** exist for all of the above (e.g. `PrimaryBrush`, `Gray600Brush`).

---

## 2. Semantic Colors (added during cleanup)

All former hardcoded hex values have been extracted into Colors.xaml as named resources.
XAML files now use `{AppThemeBinding Light={StaticResource X}, Dark={StaticResource XDark}}`.

### Surfaces
| Key | Light | Dark | Usage |
|-----|-------|------|-------|
| `Surface` / `SurfaceDark` | `#F5F5F5` | `#1A1A1A` | Subtle page bg (Setup, Connection, StatusBar, Debug) |
| `SurfaceAlt` / `SurfaceAltDark` | `#F0F0F0` | `#222222` | Tab bar bg |
| `CardSurface` / `CardSurfaceDark` | White | `#2A2A2A` | Cards, modals, overlays |
| `MutedSurface` / `MutedSurfaceDark` | `#EEEEEE` | `#3A3A3A` | Secondary surfaces, action button bg |

### Borders
| Key | Light | Dark | Usage |
|-----|-------|------|-------|
| `BorderDefault` / `BorderDefaultDark` | `#E0E0E0` | `#333333` | Standard border/stroke |

### Text
| Key | Light | Dark | Usage |
|-----|-------|------|-------|
| `TextPrimary` / `TextPrimaryDark` | `#333333` | `#E0E0E0` | Headings, important text |
| `TextMuted` / `TextMutedDark` | `#666666` | `#999999` | Descriptions, secondary text |
| `TextFaint` / `TextFaintDark` | `#999999` | `#777777` | Build labels, minimal text |

### Info / Status
| Key | Light | Dark | Usage |
|-----|-------|------|-------|
| `InfoSurface` / `InfoSurfaceDark` | `#E3F2FD` | `#1A3A5C` | Info banners, inline action buttons |
| `InfoText` / `InfoTextDark` | `#1565C0` | `#90CAF9` | Info text |

### Button Variants
| Key | Light | Dark | Usage |
|-----|-------|------|-------|
| `PrimaryAlt` | — | `#7C4DFF` | Dark-mode primary buttons |
| `ButtonMuted` / `ButtonMutedDark` | `#E0E0E0` | `#333333` | Inactive/secondary button bg |

### Overlays
| Key | Value | Usage |
|-----|-------|-------|
| `Overlay` | `#80000000` | 50% black overlay |
| `SurfaceTranslucent` / `SurfaceTranslucentDark` | `#F5F5F5CC` / `#1A1A1ACC` | Translucent debug bg |

---

## 3. Remaining Hardcoded Colors

After cleanup, only these hardcoded colors remain:
- `Green`, `Red`, `Gray`, `Lime`, `White` — MAUI named colors used for status indicators and fixed-role elements
- `Transparent` — used for overlay buttons and backgrounds
- ViewModel-bound colors (`{Binding RoleColor}`, `{Binding GlassesBatteryColor}`, etc.)

---

## 3. Font Sizes

### Observed Scale
| Size | Role (inferred) | Where used |
|------|-----------------|------------|
| `10` | Tiny / caption | DebugOverlay, MediaGallery duration |
| `11` | Debug monospace | DebugOverlay |
| `12` | Detail / caption | AppShell build, SettingsCard desc, device details, transcript actions, audio player |
| `13` | Settings controls | Styles: SettingsButton, SettingsPicker, SettingsSectionHeader, StatusBar |
| `14` | Body (default) | Styles: implicit Button/Entry/Editor/Label/Picker, transcript, camera, MainPage tabs, QuickActions |
| `16` | Subtitle / card title | AppShell title, SettingsCard title, device headers, gallery filter |
| `18` | Section header | ScanResult title, GlassesPage header |
| `20` | Page title | AppShell app name, SettingsPage title, AudioPlayer icon |
| `24` | Large title | SettingsCard icon, SubHeadline style, ScanResult icon |
| `28` | Hero / setup | SetupPage step title, MediaGallery empty label |
| `32` | Headline | Styles: Headline, SetupPage title, MediaGallery empty emoji |
| `48` | Setup icon | SetupPage step icon |
| `64` | Audio emoji | AudioPlayer 🎙 |

### Proposed Type Scale for Figma
| Token | Size | Maps to |
|-------|------|---------|
| `Caption` | 10–11 | Debug, tiny labels |
| `Small` | 12 | Details, captions |
| `Body` | 14 | Default text |
| `Subtitle` | 16 | Card titles, subtitles |
| `Title` | 20 | Page titles |
| `Headline` | 28–32 | Hero text, setup |

---

## 4. Spacing

### Common Padding Values
| Value | Usage pattern |
|-------|--------------|
| `4` | Tight inner padding |
| `8` | Standard inner padding (cards, frames) |
| `12` | Medium padding (buttons, gallery tiles) |
| `16` | Page-level padding, card padding |
| `24` | Setup page padding |

### Common Margin Values
| Value | Usage pattern |
|-------|--------------|
| `0,8,0,0` | Top margin between sections |
| `0,12,0,0` | Settings section spacing |
| `0,16,0,0` | Large section gap |
| `0,16,0,4` | Section header (top 16, bottom 4) |

### Common Gap / Spacing Values
| Value | Usage pattern |
|-------|--------------|
| `2` | Tight (icon + label) |
| `4` | Small gap |
| `8` | Standard gap (grids, stacks) |
| `12` | Section gap (settings, setup) |
| `16` | Large gap |

### Proposed Spacing Scale for Figma
`2`, `4`, `8`, `12`, `16`, `24`

---

## 5. Corner Radii

| Value | Usage |
|-------|-------|
| `4` | Small (transcript inline button) |
| `8` | Standard (setup cards, modals, action buttons, settings sections, debug panel) |
| `10` | Medium (glasses page frames) |
| `14` | Pill (status bar segmented button group) |

### Proposed Radius Scale for Figma
`4`, `8`, `10` (or merge with 8), `14` (pill)

---

## 6. Named Styles (Styles.xaml)

### Label Styles
| Key | FontSize | FontAttributes | TextColor | Other |
|-----|----------|---------------|-----------|-------|
| `Headline` | 32 | — | MidnightBlue / White | HorizontalOptions=Center |
| `SubHeadline` | 24 | — | MidnightBlue / White | HorizontalOptions=Center |
| `SettingsSectionHeader` | 13 | Bold | Gray600 / Gray300 | TextTransform=Uppercase, Margin=0,16,0,4 |
| `SettingsStatusText` | 12 | — | Gray600 / Gray300 | — |
| `SettingsDeviceHeader` | 13 | Bold | — | — |
| `SettingsDetailLabel` | 12 | — | Gray | — |
| `SettingsDetailValue` | 12 | — | — | — |

### Button Styles
| Key | FontSize | Padding | Background | TextColor | Other |
|-----|----------|---------|------------|-----------|-------|
| (implicit) | 14 | 14,10 | Primary / PrimaryDark | White / PrimaryDarkText | CornerRadius=8, MinHeight=44 |
| `SettingsButton` | 13 | 12,8 | (inherit) | (inherit) | MinHeight=38 |
| `SettingsOutlineButton` | 13 | 12,8 | Transparent | Primary / PrimaryDark | BorderWidth=1, BorderColor=Primary/PrimaryDark |
| `SettingsDangerButton` | 13 | 12,8 | Red | White | — |

### Other Implicit Defaults
- **Entry/Editor:** FontSize=14, TextColor=Black/White
- **Picker:** FontSize=14, TextColor=Gray900/White
- **Switch:** OnColor=Primary/Secondary, ThumbColor=Primary/Gray200
- **Page bg:** White (light) / OffBlack (dark)
- **Border:** Stroke=Gray200/Gray500, StrokeThickness=1
