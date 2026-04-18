# Phase 2 — Keyboard & Focus Navigation

Enable full keyboard-only operation on Windows and D-pad / switch access
navigation on Android.

Depends on Phase 1 (semantic labels must be in place first).

---

## Why

Users with motor impairments rely on keyboard (Tab/Enter/Space) or switch devices
instead of touch/mouse. Currently the app has no defined tab order, no visible
focus indicators, and the snapshot overlay doesn't trap focus.

---

## Files Changed

| File | Change |
|------|--------|
| `MainPage.xaml` | Add `TabIndex` to all interactive controls |
| `SettingsPage.xaml` | Add `TabIndex` to all interactive controls |
| `Styles.xaml` | Add `Focused` visual state with visible focus ring |
| `MainPage.xaml` | Add `Focused` visual state to state pill buttons |
| `MainPage.xaml.cs` | Focus trap logic for snapshot overlay |

---

## Tab Order — MainPage

Define a logical reading order. Tab flows top-to-bottom, left-to-right within
each row.

```
Row 0: Status bar
  TabIndex 10  SleepButton
  TabIndex 11  ListenButton
  TabIndex 12  ActiveButton
  TabIndex 13  DebugToggleButton
  TabIndex 14  ClearButton

Row 1: Content (no interactive controls in transcript)

Row 2: Tab selector
  TabIndex 20  TranscriptTabButton
  TabIndex 21  CameraTabButton

Row 3: Quick action bar
  TabIndex 30  LookButton
  TabIndex 31  ReadButton
  TabIndex 32  FindButton
  TabIndex 33  AskButton
  TabIndex 34  PhotoButton

Snapshot overlay (when visible):
  TabIndex 40  DismissSnapshotButton
```

### XAML

```xml
<Button AutomationId="SleepButton" TabIndex="10" ... />
<Button AutomationId="ListenButton" TabIndex="11" ... />
<Button AutomationId="ActiveButton" TabIndex="12" ... />
<Button AutomationId="DebugToggleButton" TabIndex="13" ... />
<Button AutomationId="ClearButton" TabIndex="14" ... />

<Button AutomationId="TranscriptTabButton" TabIndex="20" ... />
<Button AutomationId="CameraTabButton" TabIndex="21" ... />

<Button AutomationId="LookButton" TabIndex="30" ... />
<Button AutomationId="ReadButton" TabIndex="31" ... />
<Button AutomationId="FindButton" TabIndex="32" ... />
<Button AutomationId="AskButton" TabIndex="33" ... />
<Button AutomationId="PhotoButton" TabIndex="34" ... />

<Button AutomationId="DismissSnapshotButton" TabIndex="40" ... />
```

---

## Tab Order — SettingsPage

SettingsPage is a vertical scroll, so natural document order is correct.
Add sequential `TabIndex` values to ensure consistency across platforms:

```
TabIndex 10  ProviderOpenAiRadio
TabIndex 11  ProviderAzureRadio
TabIndex 20  VoiceModelPicker
TabIndex 21  ChatModelPicker
TabIndex 22  VisionModelPicker
TabIndex 23  TranscriptionModelPicker
TabIndex 30  AzureEndpointEntry
TabIndex 31  AzureApiVersionEntry
TabIndex 32  AzureRealtimeDeploymentEntry
TabIndex 33  AzureChatDeploymentEntry
TabIndex 34  AzureVisionDeploymentEntry
TabIndex 40  VoicePicker
TabIndex 41  TurnDetectionPicker
TabIndex 42  NoiseReductionPicker
TabIndex 50  SystemInstructionsEditor
TabIndex 60  ApiKeyDisplay
TabIndex 61  ToggleKeyVisibilityButton
TabIndex 62  ChangeApiKeyButton
TabIndex 63  ClearApiKeyButton
TabIndex 64  TestConnectionButton
TabIndex 70  CameraSourcePicker
TabIndex 80  AudioInputPicker
TabIndex 90  AudioOutputPicker
TabIndex 100 DebugModeSwitch
TabIndex 101 ShowTokenCountsSwitch
TabIndex 102 ShowCostEstimateSwitch
```

---

## Focus Ring — Visual Feedback

Add a `Focused` visual state to the `ActionButton` style in `Styles.xaml` or
`MainPage.Resources` so keyboard users can see which control is focused.

### ActionButton Style Update

```xml
<Style x:Key="ActionButton" TargetType="Button">
    <Setter Property="HeightRequest" Value="44" />
    <Setter Property="FontSize" Value="14" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Padding" Value="4,0" />
    <Setter Property="BackgroundColor"
            Value="{AppThemeBinding Light=#EEEEEE, Dark=#2A2A2A}" />
    <Setter Property="TextColor"
            Value="{AppThemeBinding Light=#333333, Dark=#E0E0E0}" />
    <Setter Property="VisualStateManager.VisualStateGroups">
        <VisualStateGroupList>
            <VisualStateGroup x:Name="CommonStates">
                <VisualState x:Name="Normal" />
                <VisualState x:Name="Focused">
                    <VisualState.Setters>
                        <Setter Property="BorderColor" Value="{StaticResource Primary}" />
                        <Setter Property="BorderWidth" Value="2" />
                    </VisualState.Setters>
                </VisualState>
                <VisualState x:Name="Disabled">
                    <VisualState.Setters>
                        <Setter Property="Opacity" Value="0.5" />
                    </VisualState.Setters>
                </VisualState>
            </VisualStateGroup>
        </VisualStateGroupList>
    </Setter>
</Style>
```

### State Pill Buttons

The state pill buttons (`SleepButton`, `ListenButton`, `ActiveButton`) use
inline styles, not `ActionButton`. Add the same `Focused` visual state inline:

```xml
<Button AutomationId="SleepButton" Text="😴" TabIndex="10" ...>
    <VisualStateManager.VisualStateGroups>
        <VisualStateGroupList>
            <VisualStateGroup x:Name="CommonStates">
                <VisualState x:Name="Normal" />
                <VisualState x:Name="Focused">
                    <VisualState.Setters>
                        <Setter Property="BorderColor" Value="{StaticResource Primary}" />
                        <Setter Property="BorderWidth" Value="2" />
                    </VisualState.Setters>
                </VisualState>
            </VisualStateGroup>
        </VisualStateGroupList>
    </VisualStateManager.VisualStateGroups>
</Button>
```

Repeat for `ListenButton` and `ActiveButton`.

### Global Focus Ring (Styles.xaml)

Consider adding focus states to the global `Button` style in `Styles.xaml` so all
buttons across the app get focus rings by default. Only do this if the global
style doesn't conflict with themed buttons.

---

## Snapshot Overlay — Modal Focus Trap

When `ShowSnapshot` is true, keyboard focus should be trapped inside the overlay
so Tab doesn't reach controls behind the semi-transparent backdrop.

### Implementation in MainPage.xaml.cs

```csharp
private void OnShowSnapshotChanged()
{
    if (BindingContext is MainViewModel vm && vm.ShowSnapshot)
    {
        // Move focus to the dismiss button when overlay appears
        DismissSnapshotButton.Focus();
    }
}
```

The simplest approach: when `ShowSnapshot` becomes true, call `Focus()` on the
`DismissSnapshotButton`. Since the snapshot overlay only has one interactive
control, Tab will stay on it.

If more controls are added to the overlay later, use `IsTabStop="False"` on all
controls behind the overlay while it's visible.

### Alternative — InputTransparent

The existing `Grid` with `BackgroundColor="#80000000"` already covers the full
screen. Setting `InputTransparent="False"` on the overlay grid (which is already
the default) should block touch. For keyboard, the focus management above
handles it.

---

## Keyboard Activation

MAUI buttons respond to Enter and Space by default when focused. **No code
changes needed** — just verify during testing.

Pickers: on Windows, MAUI Pickers open their dropdown with Enter/Space when
focused. **Verify only.**

Switches: on Windows, MAUI Switches toggle with Space when focused. **Verify
only.**

---

## Android Focus Navigation

On Android with TalkBack, swipe gestures handle focus navigation. D-pad
navigation (for switch access devices) uses the default view tree order.

If D-pad navigation is wrong, add `android:nextFocusDown`, `nextFocusUp`,
`nextFocusLeft`, `nextFocusRight` via platform-specific handlers. This is
unlikely to be needed since the layout is a simple vertical stack.

**Action:** Test with Android switch access. Only add platform attributes if
navigation order is incorrect.

---

## Testing Checklist

### Windows Keyboard

1. Press Tab from the first control on MainPage
2. Verify focus moves: Sleep → Listen → Active → Debug → Clear → Transcript tab → Camera tab → Look → Read → Find → Ask → Photo
3. Verify a visible focus ring appears on each focused control
4. Press Enter or Space on each button — verify command fires
5. Open snapshot overlay → verify focus moves to Dismiss button
6. Press Tab — verify focus stays on Dismiss (doesn't go behind overlay)
7. Dismiss overlay → verify focus returns to main content

### Windows Keyboard — SettingsPage

1. Tab through all controls in order
2. Verify radio buttons toggle with Space
3. Verify pickers open with Enter
4. Verify switches toggle with Space
5. Verify entries accept text input when focused

### Android Switch Access

1. Enable Switch Access (Settings → Accessibility → Switch Access)
2. Verify scanning highlights controls in logical order
3. Verify each control can be activated

---

## Exit Criteria

- `TabIndex` set on all interactive controls (MainPage + SettingsPage)
- Visible focus ring on all buttons (ActionButton style + state pills)
- Snapshot overlay traps focus when visible
- Full app navigable via Tab key on Windows (no missing controls)
- Enter/Space activates all buttons, toggles switches, opens pickers
