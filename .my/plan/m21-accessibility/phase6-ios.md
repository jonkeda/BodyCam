# Phase 6 — iOS Platform Support

Verify and fix all accessibility features from Phases 1–5 on iOS with VoiceOver,
Dynamic Type, Switch Control, and iOS-specific accessibility APIs.

Depends on all prior phases being complete.

---

## Why

iOS has the most mature accessibility platform (VoiceOver, Dynamic Type, Switch
Control, Reduce Motion, Bold Text). MAUI's `SemanticProperties` map to
UIAccessibility traits, but there are iOS-specific behaviors that need verification.

---

## Files Changed

| File | Change |
|------|--------|
| `phase5 MotionPreference` | Add iOS implementation |
| Platform-specific code | Fix any iOS-specific accessibility issues found during testing |

---

## VoiceOver Navigation (Phase 1 Verification)

VoiceOver is the iOS equivalent of Narrator/TalkBack.

### Test Plan

1. Enable VoiceOver (Settings → Accessibility → VoiceOver)
2. Swipe right through MainPage — verify each control announces:
   - Status dot: `StateDescription` binding
   - Sleep/Listen/Active buttons: `SemanticProperties.Description`
   - Debug toggle: "Toggle debug console"
   - Clear button: "Clear transcript" + hint
   - Transcript/Camera tabs
   - Quick action buttons
3. Navigate to SettingsPage — verify:
   - Section headers announced as headings (swipe with rotor set to Headings)
   - Pickers announce label + current value
   - Switches announce label + on/off state
   - Entries announce label
4. Verify transcript entries announce `AccessibleText`
5. Verify snapshot overlay receives focus when shown

### MAUI → iOS Mapping

| MAUI | iOS UIAccessibility |
|------|---------------------|
| `SemanticProperties.Description` | `accessibilityLabel` |
| `SemanticProperties.Hint` | `accessibilityHint` |
| `SemanticProperties.HeadingLevel` | `accessibilityTraits = .header` |

These should map automatically. **Verify only.**

### Known Issues to Watch

- MAUI `CollectionView` on iOS may not correctly expose items to VoiceOver.
  Each item should be a single accessible element with `AccessibleText`.
- `CameraView` from CommunityToolkit may need `IsAccessibilityElement = false`
  to avoid VoiceOver trying to read the camera preview.
- Emoji in button text (e.g. "📝 Transcript") — VoiceOver reads emoji names
  aloud ("memo Transcript"). The `SemanticProperties.Description` should
  override this, but verify.

---

## Dynamic Type (Phase 3 Verification)

### Test Plan

1. Settings → Accessibility → Display & Text Size → Larger Text → slide to max
2. Launch app
3. Verify all text scales up
4. Verify no clipping or overlapping
5. Verify buttons grow to accommodate larger text
6. Test with "Larger Accessibility Sizes" enabled (even bigger)

### iOS-Specific Considerations

- MAUI named font sizes (`Body`, `Caption`, `Title`) should map to iOS Dynamic
  Type styles. Verify the mapping is correct.
- iOS supports "Larger Accessibility Sizes" which go beyond standard Dynamic Type.
  Test at maximum accessibility size.
- `FontAutoScalingEnabled` should be respected on iOS by default.

---

## Switch Control (Phase 2 Verification)

### Test Plan

1. Settings → Accessibility → Switch Control → enable
2. Set up a simple switch (e.g. screen tap)
3. Verify scanning highlights controls in correct order
4. Verify each control can be activated
5. Verify snapshot overlay focus trap works with Switch Control

### iOS-Specific Considerations

- Switch Control uses the same accessibility tree as VoiceOver
- `TabIndex` in MAUI should influence scanning order on iOS
- If scanning order is wrong, may need to set `accessibilityElements` order
  via platform-specific code

---

## Reduce Motion (Phase 5 Verification)

### Implementation

Add the iOS path to `MotionPreference`:

```csharp
#elif IOS
    return UIKit.UIAccessibility.IsReduceMotionEnabled;
```

### Test Plan

1. Settings → Accessibility → Motion → Reduce Motion → On
2. Launch app
3. Verify transcript entries appear instantly (no slide/fade animation)
4. Verify thinking dots are static (no pulsing)

---

## Bold Text

### Test Plan

1. Settings → Accessibility → Display & Text Size → Bold Text → On
2. Launch app
3. Verify all text appears bold
4. Verify layout doesn't break (bold text is wider)

MAUI should respect this automatically via system font metrics. **Verify only.**

---

## Additional iOS Accessibility Features

### Increase Contrast

1. Settings → Accessibility → Display & Text Size → Increase Contrast → On
2. Launch app
3. Verify UI is still readable (this reduces transparency and increases contrast)

### Smart Invert

1. Settings → Accessibility → Display & Text Size → Smart Invert → On
2. Launch app
3. Verify the camera preview is NOT inverted (images should be excluded)
4. Verify text and UI controls are inverted and readable

If the camera preview gets inverted, add in platform-specific code:

```csharp
// In iOS platform code
CameraPreview.AccessibilityIgnoresInvertColors = true;
```

### Spoken Content

1. Settings → Accessibility → Spoken Content → Speak Selection → On
2. Select text in transcript → verify "Speak" option appears
3. Verify selected text is spoken correctly

---

## Audio Session Conflicts

VoiceOver and the app both use audio. Verify:

1. VoiceOver speech doesn't conflict with AI TTS playback
2. VoiceOver audio ducking works (VoiceOver lowers its volume during TTS)
3. Starting/stopping an active session doesn't silence VoiceOver
4. The `AVAudioSession` category doesn't block VoiceOver announcements

If conflicts occur, ensure the audio session is configured with:
```csharp
AVAudioSession.SharedInstance().SetCategory(
    AVAudioSessionCategory.PlayAndRecord,
    AVAudioSessionCategoryOptions.AllowBluetooth |
    AVAudioSessionCategoryOptions.DuckOthers |
    AVAudioSessionCategoryOptions.MixWithOthers);
```

The `MixWithOthers` option allows VoiceOver to speak alongside app audio.

---

## Testing Matrix

| Feature | Setting | Expected |
|---------|---------|----------|
| VoiceOver | On | All controls announced with correct labels |
| Dynamic Type | Maximum | Text scales, no clipping |
| Dynamic Type | Max Accessibility | Text scales, no clipping |
| Switch Control | On | All controls scannable and activatable |
| Reduce Motion | On | No animations |
| Bold Text | On | All text bold, layout intact |
| Increase Contrast | On | UI readable |
| Smart Invert | On | Camera not inverted, UI inverted correctly |
| Spoken Content | Speak Selection | Transcript text speakable |
| VoiceOver + Active Session | Simultaneous | No audio conflicts |

---

## Exit Criteria

- VoiceOver navigates all controls with correct announcements
- Dynamic Type scaling works at all sizes including max accessibility
- Switch Control scans controls in correct order
- Reduce Motion disables all animations
- Bold Text renders correctly
- Smart Invert excludes camera preview
- No VoiceOver / app audio conflicts during active sessions
- All VoiceOver rotor options (Headings, Form Controls) work correctly
