# Phase 4 - Tests And Accessibility

**Goal:** Verify the registry-driven camera button flow and make it usable with
touch, keyboard, and screen readers.

## Unit Tests

Add focused tests for the view-model layer:

- camera action items are built from registered action descriptors;
- non-camera/session actions are excluded from the camera action list;
- active action variants are generated from command prompt/option metadata;
- actions without metadata get one default sub-button;
- tapping a top-level action shows sub-buttons and preview without capturing;
- tapping a sub-button captures one frame;
- the captured frame is added to the transcript with a useful caption;
- the command receives the same captured frame;
- a waiting transcript row uses the busy-dot sequence while work is in flight;
- completion replaces the waiting row with command text;
- errors keep the captured image and replace the waiting row with an error.

## UI/Markup Checks

Add or update tests where the project already checks XAML-visible state:

- `CameraTabView` contains the registered action rail under the preview;
- `CameraTabView` contains a generated sub-button rail under the action rail;
- each generated button has a stable automation id derived from action id and
  variant key;
- only the selected action's sub-buttons are visible;
- the old drawer is not required for camera actions.

Suggested automation IDs:

```text
CameraActionRail
CameraActionButton_{actionId}
CameraActionVariantRail
CameraActionVariantButton_{actionId}_{variantKey}
CameraCapturedStillTranscriptImage
CameraCommandWaitingEntry
```

Do not add fixed automation IDs such as `CameraLookPanel`,
`CameraReadPanel`, or `CameraActionNextButton`; those would reintroduce the
hardcoded panel model.

## Accessibility

- Every generated action button needs a clear semantic label and hint.
- The selected action's sub-button rail should announce the action name when it
  appears.
- Captured still transcript entries should have useful descriptions, for
  example `Captured frame for Look - Detail`.
- Keyboard focus should move from the selected top-level action to its
  sub-buttons, then to the transcript image/waiting row after capture.
- Buttons must remain at least 44x44 device-independent pixels.
- Dynamic action names and variant names should not overflow the button row.

## Verification

Run:

```powershell
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -p:SkipBuildNumberIncrement=true
```

For implementation work, also visually inspect desktop and mobile-size
viewports or screenshots to confirm:

- registered camera actions sit under the video;
- tapping an action reveals only its sub-buttons;
- tapping a sub-button adds the captured still to the transcript;
- busy dots are visible until the result arrives.
