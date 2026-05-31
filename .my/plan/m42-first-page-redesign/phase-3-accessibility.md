# M42 Phase 3 - Accessibility Contract

Goal: make the first page usable with screen readers, keyboard navigation, switch control, and low vision.

Scope:
- The first page has no duplicate app-title header; screen reader users start on the mode/status row.
- Settings icon announces "Settings" and hints that it opens settings.
- Mode chips are a single conceptual mode group. Each chip has a clear visible name and announces selected state through semantics.
- Transcript entries use `AccessibleText`; new entries continue to auto-scroll without stealing focus.
- Message entry is after the transcript in reading order and before camera/actions controls. Send announces "Send message".
- Camera preview has accessible name "Camera preview"; the live image itself is decorative for blind users.
- Capture circle announces "Capture frame".
- Actions button stays at the bottom and announces collapsed or expanded through its text/description.
- The opened action list appears before the bottom Actions button in reading order.
- Look, Read, and Scan have semantic hints:
  - Look: describes the scene.
  - Read: reads visible text.
  - Scan: scans a QR code or barcode.

Acceptance:
- Screen reader users can understand every control without seeing the icon.
- The camera preview opening does not trap focus.
- Hidden drawer actions are not visible when the drawer is collapsed.
- Typed messages can be entered and sent without camera access.
- Results are added to the transcript as text and can be read after each action.
