# M18 Phase 2 - Scan UI

**Status:** IMPLEMENTED
**Depends on:** M18 Phase 1, camera commands

---

## Goal

Expose a touch path for scanning QR codes and barcodes without relying on the Realtime model to choose the right tool.

---

## Current UI

The old plan expected a sixth button in `QuickActionsView`. Current code uses a different layout:

| View | Current role |
|------|--------------|
| `QuickActionsView.xaml` | Shows the bottom `Actions` toggle button |
| `ActionsDrawerView.xaml` | Shows `Look`, `Detail`, `Summary`, `Read`, and `Scan` actions |
| `MainPage.xaml` | Hosts `ActionsDrawerView` and `ScanResultOverlay` as page overlays |

`ScanButton` lives here:

```
src/BodyCam/Pages/Main/Views/ActionsDrawerView.xaml
```

```xml
<Button AutomationId="ScanButton"
        Text="Scan"
        Command="{Binding ScanCommand}"
        SemanticProperties.Description="Scan"
        SemanticProperties.Hint="Scans a QR code or barcode"
        Style="{StaticResource ActionButton}" />
```

---

## Current ViewModel Flow

```
src/BodyCam/ViewModels/MainViewModel.cs
```

`MainViewModel.ScanCommand` now executes the camera command directly:

```csharp
ScanCommand = new AsyncRelayCommand(async () =>
{
    IsActionsDrawerExpanded = false;
    await ExecuteCameraCommandAsync("scan", CommandTriggerOrigin.ActionsDrawer);
});
```

This is intentionally different from the original plan. It does not send a prompt such as "Scan for QR codes..." and wait for tool routing. The UI path is deterministic:

```
ActionsDrawerView ScanButton
    -> MainViewModel.ScanCommand
    -> ExecuteCameraCommandAsync("scan", ActionsDrawer)
    -> CameraCommandService
    -> ScanCommand
    -> TryShowScanResult
```

---

## Command Mode

For `CommandTriggerOrigin.ActionsDrawer`, `CameraCommandBase.ResolveMode` uses:

```csharp
context.Settings.DefaultTouchCommandMode
```

That setting lets touch actions run in either:

| Mode | Behavior |
|------|----------|
| `FullAuto` | capture immediately |
| `ManualAim` | reveal/coordinate a manual capture before running the command |

`ScanCommand` supports both modes.

---

## No-AI Routing Requirement

The scan button no longer depends on the LLM selecting `scan_qr_code`. That makes the touch path faster and more reliable, and keeps the Realtime tool path as a separate voice/model-triggered path.

---

## Tests

| Area | Current test files |
|------|--------------------|
| Scan button command behavior | `src/BodyCam.Tests/ViewModels/MainViewModel*Tests.cs` |
| Camera command behavior | `src/BodyCam.Tests/Services/Camera/Commands/ScanCommandTests.cs` |
| UI action surface | `src/BodyCam.UITests/Tests/MainPage/QuickActionTests.cs` |

---

## Exit Criteria

1. Actions drawer exposes a visible `ScanButton`.
2. Tapping Scan executes camera command `"scan"` directly.
3. A successful scan creates a transcript result and shows `ScanResultOverlay`.
4. No-code and camera-unavailable cases are reported gracefully.
