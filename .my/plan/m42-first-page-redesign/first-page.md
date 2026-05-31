# M42 First Page

## Comments

- This is still one screen and has no tabs.
- The first page does not show a BodyCam header. On Windows, the native window title carries the app name and version.
- The top bar keeps mode controls on the left as visual chips and places the settings icon on the same row at the right.
- The visible chip labels stay short: Sleep, Listen, Active. The selected state is exposed through screen-reader text instead of visible "selected" copy.
- Speak and Silent are top-row chips. Speak allows audio output; Silent keeps output transcript-only and stores the last selected option.
- The transcript remains the primary area and scrolls.
- A message entry with a Send button stays directly below the transcript so typed input is always available.
- The camera preview is inline and full width. When Look, Read, or Scan opens it, it pushes the transcript up instead of covering it.
- The camera preview only shows the live image and a round shutter button, `#2`.
- The bottom of the screen has one Actions button. When opened, the action list appears above the page content and stacks actions vertically so more actions can be added later.

## Accessibility Contract

- The mode chips are implemented as one single-select control group, not passive tags. Screen readers announce the group as "Mode" and the current value as "Sleep selected", "Listen selected", or "Active selected".
- Visible chip labels remain Sleep, Listen, and Active. The selected mode uses a visual pill treatment and an accessible selected description.
- The output chips are implemented as one single-select group. Screen readers announce "Speak selected" or "Silent selected".
- In Silent, AI responses remain in the transcript and local playback is suppressed.
- The settings icon has accessible name "Settings" and hint "Opens settings".
- `#2` is a round visual button with accessible name "Capture frame".
- The transcript is a polite live region. New user and AI messages are announced without moving focus.
- The message entry is reachable after the transcript and before the camera/actions controls. The Send button has accessible name "Send message".
- The camera preview is not required for nonvisual use. Screen readers may announce it as "Camera preview" and skip the image content; Look, Read, and Scan results are spoken and appended to the transcript.
- Opening the camera preview announces "Camera preview opened" and keeps focus on the action that opened it unless the user explicitly moves to Capture frame.
- The Actions control announces collapsed or expanded. When collapsed, Look, Read, and Scan are not in the accessibility tree.
- Action buttons have descriptive hints: Look describes the scene, Read reads visible text, Scan scans a code.
- Every interactive target is at least 44 x 44 px and reachable by keyboard, switch control, and screen reader rotor/navigation.

## Icon Map

- `#1` settings
- `#2` capture frame / shutter

```markui
+----------------------------------------------------------------------+
| (Sleep) (Listen) (Active)  (Speak) (Silent)                    [#1]   |
+----------------------------------------------------------------------+
| #                                                                  # |
| # You: What do you see?                                            # |
| #                                                                  # |
| # AI: I can see the entrance area ahead. There is a sign above     # |
| # the door, a glass panel on the left, and a package on the        # |
| # floor near the wall.                                             # |
| #                                                                  # |
| # You: Read the sign for me.                                       # |
| #                                                                  # |
| # AI: The sign reads "Visitor Check In".                           # |
| #                                                                  # |
+######################################################################+
| <Ask about what is happening____________________________> [Send]      |
+----------------------------------------------------------------------+
| +--- Camera Preview -----------------------------------------------+ |
| |                                                                  | |
| | !==============================================================! | |
| | !                            IMG                               ! | |
| | !                         live camera                          ! | |
| | !==============================================================! | |
| |                                                                  | |
| |                               (#2)                               | |
| +------------------------------------------------------------------+ |
+----------------------------------------------------------------------+
|               +--@Drawer--- Actions ------------------------+        |
|               | [Look]                                      |        |
|               | [Read]                                      |        |
|               | [Scan]                                      |        |
|               +---------------------------------------------+        |
|                          [Actions]                                   |
+----------------------------------------------------------------------+
```
