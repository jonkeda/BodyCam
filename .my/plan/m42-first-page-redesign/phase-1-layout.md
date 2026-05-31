# M42 Phase 1 - Main Page Layout

Goal: replace the current tabbed first page with the one-screen m42 layout.

Scope:
- Remove the Transcript / Camera tab selector from `Pages/Main/MainPage.xaml`.
- Remove the visible BodyCam header from the first page.
- Move the settings entry point into the first-page status row, aligned with Sleep, Listen, and Active.
- On Windows, show the app version/build in the native window title.
- Keep the transcript as the main `*` row.
- Add a persistent typed message entry and Send button directly below the transcript.
- Show camera preview as an inline full-width row below the transcript when Look, Read, or Scan needs camera context.
- Restyle the listening layer controls as chip-like buttons: Sleep, Listen, Active.
- Replace the quick-action grid with a bottom Actions button plus an overlay action list that opens above the page content.

Acceptance:
- No visible tabs on the first page.
- No visible BodyCam header on the first page.
- Settings is visible on the same row as the mode controls.
- Transcript is visible by default and occupies the largest area.
- The message entry and Send button are visible below the transcript.
- Camera preview is hidden by default and pushes the transcript up when visible.
- Bottom area contains one Actions button when collapsed.
- Opened actions are vertical: Look, Read, Scan.
- All interactive controls remain at least 44 x 44 px.
