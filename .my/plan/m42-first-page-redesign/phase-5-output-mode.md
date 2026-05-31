# M42 Phase 5 - Output Mode

Goal: add a persistent Speak/Silent mode to the first page.

Scope:
- Add a second top-row chip group with `Speak` and `Silent`.
- Default to `Speak`.
- Persist the last selected output mode in settings.
- In `Speak`, AI responses may play through the configured audio output and still appear in the transcript.
- In `Silent`, AI responses are transcript-only: audio playback is suppressed and any queued playback is cleared.
- Keep the selected state available to screen readers without adding visible `selected` copy to the chip labels.

Acceptance:
- `Speak` and `Silent` are visible on the first page top row.
- Selecting either chip updates the selected visual state and semantic description.
- The selected output mode survives app restart through `ISettingsService`.
- Realtime transcript updates continue in both modes.
- Silent mode does not play AI output audio.
- Switching to Silent while audio is playing stops local playback.
