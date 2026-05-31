# M44 Phase 5 - UI And Accessibility

Goal: make command modes and detail levels usable from the first page without
making blind-first flows depend on the screen.

## UI Changes

- Keep the bottom Actions button.
- Actions list is built from registered command metadata and initially includes
  Look, Read, and Scan.
- Each action can expose a compact options row or long-press menu:
  - Auto / Manual
  - detail level where relevant
- Manual mode opens the inline camera preview.
- Preview pushes transcript up and is full width.
- Preview has a large round capture button.
- Preview does not require decorative labels.

## Detail Level UI

Look levels:

- Summary
- Overview
- Detailed
- Full

Read levels:

- Summary
- Overview
- Full

The last selected level should be stored in settings per command.

## Screen Reader Contract

- Action buttons have clear names and hints.
- Selected mode and detail are announced to screen readers without needing
  visible "selected" text.
- The capture button announces what command will run, for example:
  "Capture for Look" or "Capture for Read".
- Scan confirmation dialogs announce the decoded content type and proposed
  action.
- Transcript updates use live-region style announcements where supported.
- Focus returns to the originating control after command completion.

## Hardware And Hands-Free

- Physical button mappings run full-auto by default.
- A long press or alternate gesture can be mapped to manual aim later.
- Wake-word and LLM tool calls do not open a preview by default.
- Keyboard shortcuts run full-auto by default.

## Acceptance

- A blind user can trigger Look, Read, and Scan without touching the screen.
- A sighted user can choose manual aim and capture from preview.
- Adding a registered command can add an Actions item without editing the main
  page switch logic.
- Detail level settings persist.
- Screen reader labels and focus order are covered by UI tests where possible.
