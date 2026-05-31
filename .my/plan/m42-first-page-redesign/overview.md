# M42 - First Page Redesign

First MarkUI pass for a one-screen first page.

Design decisions:
- One screen only; no tabs.
- The first page does not show an app header. Windows uses the native window title to show `BodyCam` plus version/build.
- Mode controls stay at the top left as visual chips with short visible labels. Screen readers announce the selected state.
- Output controls sit beside the mode controls as Speak/Silent chips. Silent keeps responses in the transcript without local audio playback.
- Settings is an icon on the same top row as the mode controls.
- Transcript owns the main surface and scrolls.
- A typed message entry and Send button sit below the transcript.
- Look, Read, and Scan reveal the camera preview inline above the action drawer, reducing the transcript height instead of opening a modal.
- The camera preview has no action label or status copy; it only shows the preview and a round shutter control.
- Bottom actions start as one Actions button. Opening it displays a vertical action list above the page content so more actions can be added later.
- The design includes an accessibility contract for screen readers, focus order, live transcript announcements, and minimum touch targets.

Icon map:
- `#1` settings
- `#2` capture frame / shutter

Design:
- [first-page.md](first-page.md)

Implementation phases:
- [phase-1-layout.md](phase-1-layout.md)
- [phase-2-viewmodel.md](phase-2-viewmodel.md)
- [phase-3-accessibility.md](phase-3-accessibility.md)
- [phase-4-tests.md](phase-4-tests.md)
- [phase-5-output-mode.md](phase-5-output-mode.md)
