# Phase 3 — Dynamic Type & Text Scaling

Replace all hardcoded `FontSize` values with MAUI named sizes so the app respects
OS text scaling preferences (Windows text scaling, Android font size, iOS Dynamic
Type).

---

## Why

Users with low vision rely on OS-level text scaling. The app currently uses fixed
pixel sizes (`FontSize="11"` through `FontSize="18"`), which are completely
ignored when the user sets 150% or 200% text scaling on Windows, or "Largest" font
size on Android. MAUI's named font sizes (`Caption`, `Body`, `Title`, etc.)
automatically respond to platform scaling.

---

## Files Changed

| File | Change |
|------|--------|
| `MainPage.xaml` | Replace fixed `FontSize` with named sizes |
| `SettingsPage.xaml` | Replace fixed `FontSize` with named sizes |
| `MainPage.xaml` (ActionButton style) | Update `FontSize` in `ActionButton` style |
| `MainPage.xaml` | Remove hardcoded `HeightRequest` on buttons that clip at large text |

---

## Font Size Mapping

Map every current fixed size to the closest MAUI named size:

| Current | MAUI Named Size | Approximate Size | Used For |
|---------|-----------------|-------------------|----------|
| `11` | `Micro` | ~10pt | Debug log text |
| `12` | `Caption` | ~12pt | Captions, hints, helper text |
| `13` | `Small` | ~13pt | Field labels ("Voice Model"), status text |
| `14` | `Body` | ~14pt | Transcript text, button text, default |
| `15` | `Body` | ~14pt | Tool section headers (close enough to Body) |
| `16` | `Subtitle` | ~16pt | Debug toggle emoji |
| `18` | `Title` | ~18pt | Settings section headers |

---

## MainPage Changes

### ActionButton Style

```xml
<!-- Before -->
<Style x:Key="ActionButton" TargetType="Button">
    <Setter Property="HeightRequest" Value="44" />
    <Setter Property="FontSize" Value="14" />

<!-- After -->
<Style x:Key="ActionButton" TargetType="Button">
    <Setter Property="MinimumHeightRequest" Value="44" />
    <Setter Property="FontSize" Value="Body" />
```

Note: `HeightRequest` → `MinimumHeightRequest` so the button can grow with
larger text.

### State Pill Buttons

```xml
<!-- Before -->
<Border ... HeightRequest="36">
    <Button ... HeightRequest="32" FontSize="14" />

<!-- After -->
<Border ... MinimumHeightRequest="36">
    <Button ... MinimumHeightRequest="32" FontSize="Body" />
```

### Transcript Text

```xml
<!-- Before -->
<Label Text="{Binding DisplayText}" FontSize="14" ... />

<!-- After -->
<Label Text="{Binding DisplayText}" FontSize="Body" ... />
```

### Image Caption

```xml
<!-- Before -->
<Label Text="{Binding ImageCaption}" FontSize="12" TextColor="Gray" ... />

<!-- After -->
<Label Text="{Binding ImageCaption}" FontSize="Caption" TextColor="Gray" ... />
```

### Clear Button

```xml
<!-- Before -->
<Button AutomationId="ClearButton" FontSize="12" HeightRequest="32" ... />

<!-- After -->
<Button AutomationId="ClearButton" FontSize="Caption" MinimumHeightRequest="32" ... />
```

### Debug Log

```xml
<!-- Before -->
<Label AutomationId="DebugLabel" FontSize="11" ... />

<!-- After -->
<Label AutomationId="DebugLabel" FontSize="Micro" ... />
```

### Debug Toggle

```xml
<!-- Before -->
<Button AutomationId="DebugToggleButton" FontSize="16" HeightRequest="32" ... />

<!-- After -->
<Button AutomationId="DebugToggleButton" FontSize="Subtitle" MinimumHeightRequest="32" ... />
```

### Tab Buttons

```xml
<!-- Before -->
<Grid Grid.Row="2" ColumnDefinitions="*,*" HeightRequest="40" ...>
    <Button ... FontSize="14" />

<!-- After -->
<Grid Grid.Row="2" ColumnDefinitions="*,*" MinimumHeightRequest="40" ...>
    <Button ... FontSize="Body" />
```

### Snapshot Caption

```xml
<!-- Before -->
<Label AutomationId="SnapshotCaption" FontSize="14" ... />

<!-- After -->
<Label AutomationId="SnapshotCaption" FontSize="Body" ... />
```

### Camera Placeholder

```xml
<!-- Before (no FontSize set — uses default) -->
<Label AutomationId="CameraPlaceholder" Text="Camera initializing..." ... />

<!-- After — explicit for consistency -->
<Label AutomationId="CameraPlaceholder" Text="Camera initializing..." FontSize="Body" ... />
```

---

## SettingsPage Changes

### Section Headers

```xml
<!-- Before -->
<Label Text="Provider" FontSize="18" FontAttributes="Bold" ... />

<!-- After -->
<Label Text="Provider" FontSize="Title" FontAttributes="Bold" ... />
```

Apply to all 11 section headers.

### Field Labels

```xml
<!-- Before -->
<Label Text="Voice Model" FontSize="13" TextColor="Gray" />

<!-- After -->
<Label Text="Voice Model" FontSize="Small" TextColor="Gray" />
```

Apply to all field labels (`"Voice Model"`, `"Chat Model"`, `"Vision Model"`,
`"Transcription Model"`, `"Endpoint"`, `"API Version"`, `"Realtime Deployment"`,
`"Chat Deployment"`, `"Vision Deployment"`, `"Voice"`, `"Turn Detection"`,
`"Noise Reduction"`, `"Camera Source"`, `"Microphone Source"`, `"Speaker"`).

### Status Labels

```xml
<!-- Before -->
<Label Text="{Binding RealtimeStatus}" FontSize="13" ... />

<!-- After -->
<Label Text="{Binding RealtimeStatus}" FontSize="Small" ... />
```

Apply to all status labels (`RealtimeStatusLabel`, `ChatStatusLabel`,
`VisionStatusLabel`, `TranscriptionStatusLabel`, `ConnectionStatusLabel`).

### Note Text

```xml
<!-- Before -->
<Label Text="Changes take effect on next session start." FontSize="12" ... />

<!-- After -->
<Label Text="Changes take effect on next session start." FontSize="Caption" ... />
```

### Tool Settings

```xml
<!-- Before -->
<Label Text="{Binding DisplayName}" FontSize="15" FontAttributes="Bold" />
<Label Text="{Binding Description}" FontSize="12" TextColor="Gray" ... />

<!-- After -->
<Label Text="{Binding DisplayName}" FontSize="Body" FontAttributes="Bold" />
<Label Text="{Binding Description}" FontSize="Caption" TextColor="Gray" ... />
```

### Helper Text

```xml
<!-- Before -->
<Label Text="Configure individual tool behavior." FontSize="12" TextColor="Gray" />

<!-- After -->
<Label Text="Configure individual tool behavior." FontSize="Caption" TextColor="Gray" />
```

---

## FontAutoScalingEnabled

MAUI sets `FontAutoScalingEnabled="True"` by default on all text controls. Verify
that no code or style explicitly sets it to `False`.

Search for:
```
FontAutoScalingEnabled="False"
```

If found, remove it unless there's a deliberate reason (e.g. the debug log where
exact monospace alignment is needed — even then, prefer scaling).

---

## HeightRequest → MinimumHeightRequest

All fixed `HeightRequest` on buttons and containers should become
`MinimumHeightRequest` so they grow when text is larger:

| Control | Before | After |
|---------|--------|-------|
| `ActionButton` style | `HeightRequest="44"` | `MinimumHeightRequest="44"` |
| State pill border | `HeightRequest="36"` | `MinimumHeightRequest="36"` |
| State pill buttons | `HeightRequest="32"` | `MinimumHeightRequest="32"` |
| Debug toggle | `HeightRequest="32"` | `MinimumHeightRequest="32"` |
| Clear button | `HeightRequest="32"` | `MinimumHeightRequest="32"` |
| Tab bar grid | `HeightRequest="40"` | `MinimumHeightRequest="40"` |

---

## Testing

### Windows — 200% Text Scaling

1. Settings → System → Display → Scale and layout → 200%
2. Launch app
3. Verify all text is larger
4. Verify no text is clipped or overlapping
5. Verify buttons grow to fit text
6. Verify tab bar grows to fit text
7. Verify state pill buttons grow to fit text
8. Scroll through SettingsPage — verify no overlap

### Android — Largest Font Size

1. Settings → Display → Font size → Largest
2. Launch app
3. Same checks as Windows

### Layout Breakpoints

At very large text sizes, the quick action buttons (3-column grid) may overflow.
If text is too wide, consider:
- Reducing to 2 columns at large text
- Using `HorizontalStackLayout` with wrapping
- Shortening button text ("👁" instead of "👁 Look")

**Action:** Test first, fix only if layout breaks.

---

## Exit Criteria

- Zero hardcoded `FontSize` values remain (all use named sizes)
- Zero `HeightRequest` on interactive controls (all use `MinimumHeightRequest`)
- App respects OS text scaling at 150% and 200%
- No text clipping or overlapping at maximum text size
- `FontAutoScalingEnabled` is not explicitly disabled anywhere
