# M42 Phase 2 - ViewModel State

Goal: add only the state needed for the m42 layout.

Scope:
- Add `ShowInlineCameraPreview` for the inline camera row.
- Add `IsActionsDrawerExpanded` and `ToggleActionsDrawerCommand` for the bottom Actions button and overlay action list.
- Default the action list to collapsed.
- Add a settings navigation command for the status-row settings icon.
- Add message entry state and a send command for typed input below the transcript.
- Keep short visible chip labels in XAML and expose selected chip text through semantic descriptions.
- Before Look, Read, or Scan performs capture work, reveal and start the inline camera preview.
- Close the action list after Look, Read, or Scan is selected so the transcript regains space.
- Keep legacy tab commands/properties only if needed for compatibility, but the UI no longer presents tabs.

Acceptance:
- Look, Read, and Scan reveal the camera preview.
- Actions drawer can collapse and expand.
- Actions are hidden by default and visible only after tapping Actions.
- Settings navigation works from the first-page status row.
- Sending blank typed input is a no-op.
- Sending nonblank typed input appends the user message and routes it through the active session when one is running.
- The selected mode is announced through semantics, e.g. `Listen selected`, while the visible label remains `Listen`.
- Existing command names for Look, Read, and Scan continue to work for tests and button mappings.
