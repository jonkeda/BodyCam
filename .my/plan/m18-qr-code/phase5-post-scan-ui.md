# M18 Phase 5 - Post-Scan UI & Voice Actions

**Status:** IMPLEMENTED
**Depends on:** M18 Phase 4, camera commands, transcript UI

> Phase 5 does not need `VisionPipeline`. The current post-scan UI is driven by `ScanCommand` results and the `AgentOrchestrator.ScanResultReady` event.

---

## Goal

After a QR code or barcode is scanned, show the user an actionable result card and keep a transcript entry that can reopen those actions.

Two interaction paths exist:

| Path | Current behavior |
|------|------------------|
| Visual | `ScanResultOverlay` shows summary + buttons from `handler.SuggestedActions` |
| Voice | Realtime `scan_qr_code` tool result gives the AI `content_type`, `details`, and `suggested_actions` so it can ask what to do |

---

## Current Visual Flow

```
Actions drawer Scan
    -> MainViewModel.ScanCommand
    -> ExecuteCameraCommandAsync("scan", ActionsDrawer)
    -> CameraCommandService
    -> ScanCommand
    -> CameraCommandResult
    -> ApplyCameraCommandResult
    -> TryShowScanResult
    -> ShowScanResultCard
    -> ScanResultOverlay
```

`ScanCommand` is the source of truth for scan data. It returns a `CameraCommandResult` whose `Data` contains `found`, `content`, `format`, `content_type`, `suggested_actions`, `details`, and `requires_confirmation`.

---

## Current Realtime Tool Flow

```
Realtime model calls scan_qr_code
    -> AgentOrchestrator.HandleResponseDoneAsync
    -> ToolDispatcher.ExecuteAsync("scan_qr_code")
    -> ScanQrCodeTool
    -> CameraCommandService.ExecuteAsync("scan")
    -> AgentOrchestrator.TryFireScanResult
    -> ScanResultReady event
    -> MainViewModel.ShowScanResultCard
```

`TryFireScanResult` parses the returned tool JSON and raises `ScanResultReady` only when:

1. the tool name is `scan_qr_code`
2. the result contains `found: true`
3. the result contains `content`

The direct wake-word `InvokeTool` branch currently executes the tool but does not call `TryFireScanResult`, so do not treat wake-word-only overlay display as complete unless that branch is updated.

---

## No VisionPipeline Dependency

`Services/Vision/VisionPipeline`, `QrScanStage`, `TextDetectionStage`, and `SceneDescriptionStage` exist and are registered, but current Phase 5 behavior does not use them.

Current `LookTool` delegates to:

```
ICameraCommandService.ExecuteAsync("look")
```

Current `ScanQrCodeTool` delegates to:

```
ICameraCommandService.ExecuteAsync("scan")
```

So Phase 5 should be maintained independently of the pipeline classes. If a future change reuses `QrScanStage`, it still needs explicit UI event wiring equivalent to `TryFireScanResult` or `TryShowScanResult`.

---

## `ScanResultOverlay`

```
src/BodyCam/Pages/Main/Views/ScanResultOverlay.xaml
```

The overlay binds to `MainViewModel`:

| Binding | Source |
|---------|--------|
| `ShowScanResult` | overlay visibility |
| `ScanResultIcon` | handler icon |
| `ScanResultTitle` | handler display name |
| `ScanResultSummary` | handler summary |
| `ScanActions` | action buttons |

Current placement:

```
src/BodyCam/Pages/Main/MainPage.xaml
```

```xml
<views:ScanResultOverlay Grid.Row="1" Grid.RowSpan="4" />
```

This floats the card over the main page content.

---

## `ShowScanResultCard`

```
src/BodyCam/ViewModels/MainViewModel.cs
```

Current responsibilities:

1. Cache the last handler, parsed details, and raw content.
2. Populate overlay icon/title/summary.
3. Rebuild `ScanActions` from `handler.SuggestedActions`.
4. Show the overlay.
5. Start a 30-second auto-dismiss task.
6. Add a transcript entry with role `Scan`.
7. Add a `Show actions` transcript button that reopens the latest cached scan.

The transcript entry text is:

```csharp
$"{handler.Icon} {handler.DisplayName}: {handler.Summarize(parsed)}"
```

---

## Action Buttons

Each overlay action is a `ContentAction`:

```
src/BodyCam/Models/ContentAction.cs
```

Current tap behavior:

```csharp
private void ExecuteScanAction(
    string action,
    IQrContentHandler handler,
    Dictionary<string, object> parsed,
    string rawContent)
{
    ShowScanResult = false;

    if (IsRunning)
    {
        var prompt = $"The user chose \"{action}\" for the scanned {handler.ContentType}: {rawContent}";
        _ = _sessionCoordinator is not null
            ? _sessionCoordinator.SendTextInputAsync(prompt)
            : _orchestrator.SendTextInputAsync(prompt);
    }
}
```

Important current behavior:

| State | Tap result |
|-------|------------|
| Active session running | Sends the chosen action as text input to the AI |
| No active session | Dismisses overlay only |

---

## Transcript Rendering

```
src/BodyCam/Models/TranscriptEntry.cs
src/BodyCam/Pages/Main/Views/TranscriptView.xaml
```

Implemented pieces:

| Feature | Status |
|---------|--------|
| `TranscriptEntry.Actions` | Implemented |
| `HasActions` | Implemented |
| `NotifyActionsChanged()` | Implemented |
| `Scan` role color | Implemented |
| `TranscriptScanEntryLabel` automation ID | Implemented |
| Inline action button rendering | Implemented |

`Show actions` reopens the most recent cached scan result. It is last-scan-wins, not per-entry immutable history.

---

## Accessibility

Current overlay accessibility:

| Element | Current support |
|---------|-----------------|
| Summary label | `AutomationId="ScanResultContent"` |
| Title label | semantic description + hint |
| Summary label | semantic description + hint |
| Action buttons | `AutomationId` bound to action label |

---

## Current Edge Cases

| Scenario | Current behavior |
|----------|------------------|
| No QR/barcode found | Transcript says no code detected; overlay is not shown |
| Camera unavailable | Command result reports camera unavailable |
| Second scan while overlay is open | New overlay data replaces old data |
| Auto-dismiss from an old overlay | Current implementation uses independent delay tasks; a stale task can hide a newer overlay |
| Voice action completes | No explicit overlay-dismiss-on-tool-completion hook beyond action tap and auto-dismiss |
| Wake-word direct tool invocation | Tool executes; overlay event is not currently raised from that branch |

---

## Tests

| Area | Current test files |
|------|--------------------|
| Scan command result data | `src/BodyCam.Tests/Services/Camera/Commands/ScanCommandTests.cs` |
| MainViewModel scan UI behavior | `src/BodyCam.Tests/ViewModels/MainViewModel*Tests.cs` |
| Orchestrator scan event | `src/BodyCam.Tests/Orchestration/AgentOrchestratorTests.cs` |
| Scan overlay view | `src/BodyCam/Pages/Main/Views/ScanResultOverlay.xaml` plus UI tests |
| Transcript action rendering | `src/BodyCam/Pages/Main/Views/TranscriptView.xaml` |

---

## Exit Criteria

1. Successful Actions drawer scans show `ScanResultOverlay`.
2. Successful Realtime `scan_qr_code` tool calls raise `ScanResultReady` and show the overlay.
3. Overlay displays handler icon, display name, summary, and suggested actions.
4. Scan transcript entries use role `Scan` and include `Show actions`.
5. Tapping an action while a session is active sends the choice back to the AI.
6. The phase remains independent of `VisionPipeline`.
