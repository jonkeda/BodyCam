# Phase 3 - Capture And Snapshot Flow

**Goal:** When the user taps a generated sub-button, capture one frame, show the
captured picture in the transcript, run the registered action/command with that
same frame, and show the existing waiting visual while the command runs.

## Desired Flow

```text
User taps Look / Read / Scan
  -> sub-buttons for that registered action are shown with the camera view
User taps a sub-button
  -> camera preview captures one frame
  -> captured still is displayed in transcript
  -> command runs using that captured frame
  -> wait visual cycles: . .. ...
  -> transcript receives the result
```

This is intentionally different from a camera-only snapshot overlay. The still
belongs in the transcript because it documents what frame the command processed.

## Transcript Behavior

On sub-button tap:

1. Add a user/transcript input entry with the captured image.
2. Use a caption that identifies the action and variant, for example:
   `Captured frame for Look - Detail`.
3. Add or update the AI/result entry with `IsThinking = true`.
4. While the command runs, show the existing busy-dot sequence: `.`, `..`,
   `...`.
5. Replace the waiting entry with the command result when it completes.

The captured still should remain visible in transcript history after the result
arrives.

## Command Execution

- Resolve the selected top-level action from the registered action list.
- Resolve the selected sub-button into action request options.
- Capture exactly one frame from the preview.
- Pass that same captured frame into the command context instead of allowing the
  command to capture a different frame.
- Use `ActionTriggerOrigin.ActionsDrawer` or a new touch-camera origin if the
  codebase adds one; do not treat this as a physical button or LLM trigger.
- Keep non-touch triggers on their current full-auto path.

## Implementation Notes

- Reuse the existing manual capture and busy-visual code where possible:
  `IManualCameraCaptureCoordinator`, `CameraCommandService`,
  `CameraCommandMode.ManualAim`, and transcript busy entries.
- Avoid routing sub-buttons through `PhotoCommand`, because Photo currently
  sends a generic "Take a photo of what you see" prompt.
- `SnapshotImage`, `SnapshotCaption`, and `ShowSnapshot` can remain for other
  flows, but M50's selected action flow should add the still to the transcript.
- The command result should still call the existing transcript tracking path.

## Failure States


- If capture fails, keep the preview visible and show a concise transcript
  message.
- If the command fails after capture, keep the captured image visible and
  replace the waiting entry with the error.
- If the user taps another sub-button while a command is running, ignore it or
  disable the sub-buttons until the current command completes.
- If an action cannot be mapped to a command, hide or disable its sub-buttons
  and log the mapping issue.

## Acceptance Criteria

- Tapping a top-level registered action shows sub-buttons and camera preview.
- Tapping a sub-button captures one still image.
- The still image is added to the transcript.
- The command receives the same frame shown in the transcript.
- A waiting transcript row cycles `.`, `..`, `...` while the command runs.
- Command result text replaces the waiting row.
- Failures do not leave the UI stuck in a hidden-preview or permanent-thinking
  state.
