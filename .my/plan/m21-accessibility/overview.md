# M21 — Accessibility Improvements

Make the BodyCam app usable with screen readers (Narrator on Windows, TalkBack on
Android), keyboard-only navigation, and high contrast / large text preferences.

---

## Current State

**Zero accessibility support.** The UI works visually but is unusable for assistive
technology users:

- Emoji-only buttons (`😴` `👂` `💬` `🐛`) have no text labels — screen readers
  announce nothing meaningful
- No `SemanticProperties` on any control (no `Description`, `Hint`, or `HeadingLevel`)
- Transcript entries lack semantic roles — `CollectionView` items are opaque to
  screen readers
- Pickers and Entries have adjacent `Label` text but no programmatic association
- Fixed `FontSize` values everywhere (11–18) — ignores OS text scaling preferences
- No focus indicators or tab ordering defined
- Color contrast untested (some `Gray` text on light backgrounds may fail WCAG AA)
- Images (`SnapshotImage`, camera preview) have no alt text
- No high contrast theme support

---

## Phase 1: Semantic Labels & Screen Reader Support

Add `SemanticProperties` to all interactive controls so Narrator and TalkBack can
announce their purpose.

### MainPage

| Control | Current | Fix |
|---------|---------|-----|
| Sleep button | `Text="😴"` | `SemanticProperties.Description="Sleep mode"` |
| Listen button | `Text="👂"` | `SemanticProperties.Description="Listen mode"` |
| Active button | `Text="💬"` | `SemanticProperties.Description="Active session"` |
| Debug toggle | `Text="🐛"` | `SemanticProperties.Description="Toggle debug console"` |
| Clear button | Text only | `SemanticProperties.Hint="Clears the transcript"` |
| Status dot | No label | `SemanticProperties.Description="{Binding StateDescription}"` |
| Transcript tab | `Text="📝 Transcript"` | `SemanticProperties.Description="Show transcript"` |
| Camera tab | `Text="📷 Camera"` | `SemanticProperties.Description="Show camera feed"` |
| Look button | `Text="👁 Look"` | `SemanticProperties.Description="Look at what the camera sees"` |
| Read button | `Text="📖 Read"` | `SemanticProperties.Description="Read text in view"` |
| Find button | `Text="🔍 Find"` | `SemanticProperties.Description="Find objects in view"` |
| Ask button | `Text="💬 Ask"` | `SemanticProperties.Description="Ask a question"` |
| Photo button | `Text="📸 Photo"` | `SemanticProperties.Description="Take a photo"` |
| Snapshot image | None | `SemanticProperties.Description="{Binding SnapshotCaption}"` |
| Dismiss button | Text only | `SemanticProperties.Hint="Dismiss the snapshot overlay"` |

### SettingsPage

| Control | Fix |
|---------|-----|
| Section headers | `SemanticProperties.HeadingLevel="Level1"` |
| Each Picker | `SemanticProperties.Description` matching the label above it |
| API Key entry | `SemanticProperties.Description="API key, masked"` |
| Toggle switches | `SemanticProperties.Description` matching the label text |
| Tool settings items | `SemanticProperties.Description="{Binding Label}"` |

### Transcript Items

| Element | Fix |
|---------|-----|
| `TranscriptEntry` container | `SemanticProperties.Description="{Binding AccessibleText}"` |
| Thinking dots | `SemanticProperties.Description="Thinking..."` |
| Image in transcript | `SemanticProperties.Description="{Binding ImageCaption}"` |

### Tasks

- [ ] Add `SemanticProperties.Description` to all MainPage interactive controls
- [ ] Add `SemanticProperties.Description` to all SettingsPage controls
- [ ] Add `SemanticProperties.HeadingLevel` to section headers in SettingsPage
- [ ] Add `AccessibleText` property to `TranscriptEntry` model (role + text)
- [ ] Add `StateDescription` property to `MainViewModel` ("Sleep", "Listening", "Active session")
- [ ] Test with Narrator on Windows
- [ ] Test with TalkBack on Android
- [ ] Unit tests for `StateDescription` and `AccessibleText` computed properties

---

## Phase 2: Keyboard & Focus Navigation

Enable full keyboard-only operation on Windows and D-pad/switch navigation on
Android.

### Tasks

- [ ] Set `TabIndex` on all interactive controls in MainPage (logical order)
- [ ] Set `TabIndex` on all interactive controls in SettingsPage
- [ ] Add `VisualStateManager` focus states to `ActionButton` style (visible focus ring)
- [ ] Add focus ring to state pill buttons (Sleep/Listen/Active)
- [ ] Ensure Pickers are keyboard-operable (default MAUI behavior — verify only)
- [ ] Trap focus inside snapshot overlay when visible (modal focus scope)
- [ ] Test Tab key navigation on Windows — verify logical order
- [ ] Test keyboard activation (Enter/Space) on all buttons
- [ ] Android: verify D-pad navigation via `android:nextFocusDown` (if needed)

### Focus Ring Style

```xml
<VisualStateGroup x:Name="CommonStates">
    <VisualState x:Name="Focused">
        <VisualState.Setters>
            <Setter Property="BorderColor" Value="{StaticResource Primary}" />
            <Setter Property="BorderWidth" Value="2" />
        </VisualState.Setters>
    </VisualState>
</VisualStateGroup>
```

---

## Phase 3: Dynamic Type & Text Scaling

Respect OS text size preferences (Windows text scaling, Android font size setting).

### Tasks

- [ ] Replace all fixed `FontSize` values with named sizes (`Body`, `Caption`, `Title`, `Subtitle`)
- [ ] Map current sizes to MAUI named sizes:
  - `FontSize="11"` → `FontSize="Caption"`
  - `FontSize="12"` → `FontSize="Caption"`
  - `FontSize="13"` → `FontSize="Small"`
  - `FontSize="14"` → `FontSize="Body"`
  - `FontSize="16"` → `FontSize="Subtitle"`
  - `FontSize="18"` → `FontSize="Title"`
- [ ] Use `FontAutoScalingEnabled="True"` (default in MAUI — verify it's not disabled)
- [ ] Test at 200% text scaling on Windows
- [ ] Test at largest font size on Android
- [ ] Verify layouts don't clip or overlap at large text sizes
- [ ] Adjust minimum `HeightRequest` values to accommodate larger text
- [ ] Remove hardcoded `HeightRequest="32"` / `HeightRequest="36"` on state buttons (use Auto + padding)

---

## Phase 4: Color Contrast & High Contrast

Ensure all text meets WCAG 2.1 AA contrast ratios (4.5:1 for normal text, 3:1
for large text).

### Current Contrast Issues

| Pair | Ratio | Verdict |
|------|-------|---------|
| `#333` on `#EEE` (light) | 10.1:1 | ✅ Pass |
| `#E0E0E0` on `#2A2A2A` (dark) | 9.5:1 | ✅ Pass |
| `Gray` on `White` (light captions) | ~3.9:1 | ⚠️ Fails AA for small text |
| `Gray` on `#1A1A1A` (dark debug) | ~3.3:1 | ⚠️ Fails AA for small text |
| Status dot colors (binding) | Unknown | ❓ Needs audit |

### Tasks

- [ ] Audit all `TextColor="Gray"` usages — replace with `Gray600` (light) / `Gray300` (dark) via `AppThemeBinding`
- [ ] Audit status dot / state colors for contrast against background
- [ ] Audit transcript `RoleColor` values for contrast
- [ ] Add Windows high contrast theme resource dictionary (`HighContrast.xaml`)
- [ ] Test with Windows High Contrast mode enabled
- [ ] Test with Android "High contrast text" setting
- [ ] Document final contrast ratios for all text/background pairs

### High Contrast Resource Dictionary

```xml
<!-- Resources/Styles/HighContrast.xaml -->
<ResourceDictionary>
    <Color x:Key="PanelBackground">Black</Color>
    <Color x:Key="PanelText">White</Color>
    <Color x:Key="AccentColor">Yellow</Color>
    <Color x:Key="LinkColor">Cyan</Color>
    <Color x:Key="ErrorColor">#FF4444</Color>
    <Color x:Key="FocusRing">White</Color>
</ResourceDictionary>
```

---

## Phase 5: Reduced Motion & Audio Cues

Respect user motion preferences and add short audio cues for state changes and
background activity. The app already has a full AI voice — `SemanticScreenReader.Announce()`
would be redundant.

### Tasks

- [ ] Check `Accessibility.PreferReducedMotion` before running transcript item animations
  - Currently: `Opacity="0" TranslationY="20"` with `EntryItem_Loaded` animation
  - With reduced motion: set `Opacity="1" TranslationY="0"` immediately
- [ ] Check before running thinking dots animation
- [ ] `IAudioCueService` + `AudioCueService` — plays short earcons through device speaker
- [ ] Audio cue files (WAV, <20KB each): activate, deactivate, listen, tool_start, tool_done, error, connected
- [ ] Play cues on state transitions (Sleep/Listen/Active)
- [ ] Play cues on tool start and completion
- [ ] Play cues on connection test result and errors
- [ ] Settings toggle: "Audio cues" (default on)
- [ ] Verify cues don't interfere with AI voice output

---

## Phase 6: iOS Platform Support

- [ ] Test VoiceOver navigation on iOS
- [ ] Verify `SemanticProperties` map to `accessibilityLabel` / `accessibilityHint`
- [ ] Test Dynamic Type scaling
- [ ] Ensure no `UIAccessibility` conflicts with AVAudioSession
- [ ] Test Switch Control navigation
- [ ] Verify bold text preference is respected

---

## Architecture Notes

**No new services or interfaces needed.** All changes are XAML attributes, small
ViewModel property additions, and code-behind tweaks.

New ViewModel properties:
- `MainViewModel.StateDescription` → `string` (computed from current state)
- `TranscriptEntry.AccessibleText` → `string` (computed: `$"{Role}: {Text}"`)

Changes to existing code:
- `MainPage.xaml` — add `SemanticProperties`, `TabIndex`, focus states
- `SettingsPage.xaml` — add `SemanticProperties`, `TabIndex`, heading levels
- `Colors.xaml` — fix Gray contrast values
- `Styles.xaml` — add focus ring visual states
- `MainPage.xaml.cs` — check `PreferReducedMotion` in animation handlers
- `MainViewModel.cs` — add `StateDescription`, audio cue playback on state transitions
- `TranscriptEntry.cs` — add `AccessibleText` property

**Testing:**
- Manual testing with Narrator (Windows) and TalkBack (Android) is required
- Unit tests for computed accessibility properties (`StateDescription`, `AccessibleText`)
- UI tests can verify `AutomationId` + `SemanticProperties` via Brinell
- Audio cue playback verified manually (state changes, tool execution)
