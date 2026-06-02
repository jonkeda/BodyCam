# Phase 2c - Capture Busy State

## Status

Draft design for the manual capture transition after Phase 2b.

This phase is intentionally small. It smooths the moment after the user taps the
camera capture button during a manual Look/Read/Scan flow.

## Goals

1. After the user taps the take-picture/capture button, the image preview panel
   should disappear.
2. The app should return to the transcript-first layout while the command runs.
3. A visible AI busy row should appear immediately and animate as:
   `AI: .`, `AI: ..`, `AI: ...`, then repeat.
4. When the command result arrives, the busy row should be replaced by the real
   AI response.
5. The captured image should still be added to the transcript according to
   Phase 2b.

## Non-Goals

- Replacing the command execution pipeline.
- Adding a full progress bar or provider-specific network progress.
- Persisting busy-state history.
- Changing prompt settings or command settings from Phase 2b.

## Current Shape

The relevant UI and view-model pieces are:

- `src/BodyCam/Pages/Main/Views/CameraTabView.xaml`
  - `CameraPreviewPanel`
  - `CaptureFrameButton`
- `src/BodyCam/ViewModels/MainViewModel.cs`
  - `PhotoCommand`
  - `ShowInlineCameraPreview`
  - `ExecuteCameraCommandAsync(...)`
  - `IManualCameraCaptureCoordinator`
- `src/BodyCam/Models/TranscriptEntry.cs`
  - `IsThinking`
  - `Text`
  - `FormattedText`
  - `AccessibleText`

Phase 2b already adds a user transcript entry with the command prompt text and
the captured image. Phase 2c should not duplicate that image. It only changes
what happens visually between capture and answer.

## Desired Flow

Manual Look example:

```text
User taps Look
Camera preview opens
User taps capture
Camera preview disappears
Transcript becomes primary again

You: Look. Give an overview.
[captured frame]

AI: .
AI: ..
AI: ...

AI: There is a hallway ahead with a door on the left...
```

The busy visual is the same AI transcript row that will eventually contain the
answer. It should not create multiple permanent transcript rows.

## Capture Timing

Do not hide the preview before the platform has captured the frame if the
platform requires a visible `CameraView` to complete `CaptureImage`.

Recommended sequence:

1. User taps `CaptureFrameButton`.
2. Disable the capture button or ignore repeated taps.
3. Complete the pending manual capture.
4. As soon as the frame has been accepted by the command pipeline, set
   `ShowInlineCameraPreview = false`.
5. Stop the camera preview.
6. Set `ShowTranscriptTab = true`.
7. Show or start the AI busy transcript row.
8. Continue the LLM/vision request in the background.
9. Replace the busy text with the final answer.

This means the preview disappears before the network/model call finishes, but
not before the app has the frame it needs.

## Busy Text Design

Use a simple text animation:

```text
AI: .
AI: ..
AI: ...
AI: .
```

Recommended implementation:

- Keep using `TranscriptEntry { Role = "AI", IsThinking = true }`.
- While `IsThinking` is true, animate a visible text value through `.`, `..`,
  and `...`.
- When the result arrives:
  - stop the animation,
  - set `IsThinking = false`,
  - replace the row text with `result.TranscriptText`.

Avoid creating new entries for every dot state. The existing AI entry should be
updated in place.

### Accessibility

The visual text may change every few hundred milliseconds, but screen readers
should not announce every dot change.

Keep the accessible text stable while thinking:

```text
AI is thinking
```

Implementation options:

- Bind the visual busy text separately from `AccessibleText`.
- Or, if `TranscriptEntry.Text` is used for the dots, do not raise
  `AccessibleText` change notifications on every dot update while
  `IsThinking == true`.

The final answer should still announce normally when `IsThinking` becomes
`false`.

## View Model Behavior

`PhotoCommand` currently handles two cases:

1. Complete an existing manual command capture through
   `IManualCameraCaptureCoordinator`.
2. Otherwise reveal the inline preview and run the legacy photo/vision flow.

Phase 2c should make the manual command path explicit:

```csharp
PhotoCommand = new AsyncRelayCommand(async () =>
{
    if (_manualCapture is not null && await _manualCapture.CompletePendingCaptureAsync())
    {
        HideInlineCameraPreview();
        ShowTranscriptTab = true;
        StartAiBusyVisualIfNeeded();
        return;
    }

    await RevealInlineCameraPreviewAsync();
    await SendVisionCommandAsync("Take a photo of what you see.");
});
```

`HideInlineCameraPreview()` should be the single place that:

- sets `ShowInlineCameraPreview = false`,
- stops `_cameraView`,
- clears any snapshot overlay that should not remain on screen.

If `ExecuteCameraCommandAsync(...)` already creates the AI thinking entry before
waiting for manual capture, Phase 2c can attach the dot animation to that entry.
If not, create the entry before waiting for the manual capture so the busy state
is ready as soon as the capture completes.

## UI Behavior

`CameraPreviewPanel` should disappear after a successful capture tap for a
pending manual command.

The snapshot overlay should not be shown for Look/Read/Scan manual command
captures. The transcript image from Phase 2b is the durable visual record.

The transcript should scroll to the busy AI row if the user was already near the
bottom of the transcript.

## Failure Behavior

If capture fails:

- hide the image panel,
- stop the busy animation,
- replace the AI row with the existing camera error text.

If the LLM/vision request fails after capture succeeds:

- keep the user prompt/image transcript entry,
- stop the busy animation,
- replace the AI row with the error message.

If the command is canceled:

- hide the image panel,
- stop the busy animation,
- show `Command canceled.`

## Implementation Checklist

- Add a helper for hiding the inline camera preview.
- Start the AI busy visual when a manual capture is completed.
- Animate the AI busy text through `.`, `..`, and `...`.
- Stop the animation on success, failure, timeout, or cancellation.
- Ensure only one AI busy row is created per command execution.
- Ensure the preview panel is hidden before the model response is awaited.
- Ensure the captured image still appears in the transcript through Phase 2b
  transcript input handling.
- Prevent repeated capture taps from creating duplicate command completions.
- Keep screen-reader output stable while the dot animation runs.

## Tests

Add focused tests for:

- manual capture completion hides `ShowInlineCameraPreview`;
- manual capture completion switches back to transcript view;
- the AI entry shows a busy dot state while the command is pending;
- the same AI entry is reused for the final response;
- cancellation and errors stop the busy animation;
- accessibility text remains `AI is thinking` while dots change.

Add or update UI tests for:

- tapping `CaptureFrameButton` hides `CameraPreviewPanel`;
- the transcript shows an AI busy row;
- the final answer replaces the busy row instead of adding a second AI answer.

## Acceptance Criteria

- During manual aim, tapping the capture button captures the frame and the image
  panel disappears before the AI response arrives.
- The transcript shows a visible busy state cycling through `AI: .`,
  `AI: ..`, and `AI: ...`.
- The busy state is replaced by the final AI text in the same transcript row.
- The captured image still appears in the user transcript entry.
- Capture errors, model errors, and cancellation all hide the image panel and
  stop the busy animation.
- Screen readers do not announce every dot animation step.
