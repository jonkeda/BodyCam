# M18 Phase 2 — Scan UI

**Status:** NOT STARTED  
**Depends on:** M18 Phase 1

---

## Goal

Add a **Scan** button to the quick-actions grid so the user can trigger a QR code scan with a single tap, without needing to use a voice command.

---

## Changes

### 1. QuickActionsView — Add Scan Button

```
Pages/Main/Views/QuickActionsView.xaml
```

Add a 6th button in row 1, column 2 of the existing 3×2 grid:

```xml
<Button Grid.Row="1" Grid.Column="2" AutomationId="ScanButton"
        Text="📷 Scan"
        Command="{Binding ScanCommand}"
        Style="{StaticResource ActionButton}" />
```

The grid already has `ColumnDefinitions="*,*,*"` and `RowDefinitions="Auto,Auto"`. The new button fills the empty slot at row 1, col 2 (next to the Photo button).

### 2. MainViewModel — ScanCommand

```
ViewModels/MainViewModel.cs
```

Add a new `AsyncRelayCommand` following the same pattern as `LookCommand`, `ReadCommand`, etc.:

```csharp
ScanCommand = new AsyncRelayCommand(async () =>
{
    await SendVisionCommandAsync("Scan for QR codes in front of me and tell me what you find.");
});
```

Expose the property:

```csharp
public ICommand ScanCommand { get; }
```

This sends a vision prompt to the AI, which triggers the `scan_qr_code` tool call through the normal tool-dispatch flow. No direct tool invocation needed — the AI decides to call the tool based on the prompt.

### 3. UI Tests

```
BodyCam.UITests/Tests/MainPage/QuickActionsTests.cs
```

| Test | Asserts |
|------|---------|
| `ScanButton_Exists` | Button found by AutomationId |
| `ScanButton_IsClickable` | Visible and enabled |

---

## Design Notes

- The Scan button uses the same `ActionButton` style as all other quick-action buttons.
- The prompt wording ("Scan for QR codes in front of me…") is designed to trigger the `scan_qr_code` tool via the AI's function-calling behavior.
- The button is always visible regardless of layer state — same as Look, Read, Find, Ask, Photo.
- On platforms without a camera, the tool returns a "Camera not available" message and the AI reports it to the user.

---

## Exit Criteria

1. Scan button visible in the quick-actions grid
2. Tapping Scan sends the vision command and triggers QR scanning
3. AI reads the scan result aloud and asks what to do
4. UI tests pass for ScanButton existence and clickability
