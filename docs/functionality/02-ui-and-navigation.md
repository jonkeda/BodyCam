# 02 — UI and Navigation

## Shell Structure

`AppShell.xaml` defines the navigation. Two top-level routes:

| Route | Page | Purpose |
|-------|------|---------|
| `MainPage` | `MainPage` | Core chat/camera interface |
| `SettingsPage` | `SettingsPage` | Settings hub |

Four settings sub-pages are registered dynamically in `AppShell.xaml.cs`:

- `ConnectionSettingsPage` — API provider, keys, models
- `VoiceSettingsPage` — Voice preset, VAD, noise reduction, system prompt
- `DeviceSettingsPage` — Camera, mic, speaker provider selection
- `AdvancedSettingsPage` — Debug flags, telemetry, tool settings

Navigation icon in the shell toggles between ⚙ (go to Settings) and ✕ (back to Main).

## First-Run: SetupPage

On launch, `AppShell` checks `ISettingsService.SetupCompleted`. If false, navigates to `SetupPage`.

**Setup Steps** (built dynamically):
1. **Android only:** Microphone permission → Camera permission → Bluetooth permission
2. **All platforms:** API key entry + validation

Each step shows title, description, icon, and status (pending/granted/denied/skipped).

- `RequestPermissionCommand` — requests Android runtime permission
- `ValidateKeyCommand` — hits `GET /v1/models` to verify the key works
- `NextCommand` / `SkipCommand` — advance through steps
- On completion: fires `SetupFinished` event, navigates to MainPage

## MainPage Layout

The main page has three visual layers stacked:

### Status Bar (top)
Three segment buttons for listening layer control:

| Button | Emoji | CommandParameter | Effect |
|--------|-------|-----------------|--------|
| Sleep | 😴 | `"Sleep"` | Stop everything |
| Listen | 👂 | `"Listen"` | Wake word only |
| Active | 💬 | `"Active"` | Full Realtime session |

Each button has dynamic background/text colors driven by `CurrentLayer` (e.g., `SleepSegmentColor`, `ActiveSegmentColor`).

Plus `StatusText` label showing current state ("Sleeping", "Listening...", "Active", "Connecting...").

### Content Area (middle)
Two tabs, toggled by `ShowTranscriptTab`:

**Transcript Tab:**
- `CollectionView` bound to `Entries: ObservableCollection<TranscriptEntry>`
- Each entry shows role ("You" / "AI"), text, optional image, thinking dots animation
- Auto-scrolls to bottom on new entries (via `Dispatcher.Dispatch` to avoid layout race)
- Entries can contain `ContentAction` buttons (detected URLs, emails, phone numbers)

**Camera Tab:**
- `CameraView` from CommunityToolkit.Maui showing live preview
- Tab buttons: 📝 (transcript) and 📷 (camera)

### Quick Actions Bar (bottom)
Five action buttons, always enabled:

| Button | AutomationId | Command | What It Does |
|--------|-------------|---------|-------------|
| 👁 Look | `LookButton` | `LookCommand` | "Describe what you see in front of me." |
| 📖 Read | `ReadButton` | `ReadCommand` | "Read any text you can see." |
| 🔍 Find | `FindButton` | `FindCommand` | "Look around and tell me what objects you can find." |
| ❓ Ask | `AskButton` | `AskCommand` | Escalates to Active session |
| 📸 Photo | `PhotoButton` | `PhotoCommand` | "Take a photo of what you see." |

Look/Read/Find/Photo all call `SendVisionCommandAsync(prompt)` which has two paths:
- **Session active:** Sends text through Realtime API (AI speaks the response)
- **No session:** Captures frame directly via VisionAgent, shows text in transcript (no voice)

### Floating Snapshot
When a photo is taken or vision returns an image, a floating overlay shows:
- `SnapshotImage` — the captured frame
- `SnapshotCaption` — description text
- `DismissSnapshotCommand` — closes overlay

### Debug Overlay
Toggle via `ToggleDebugCommand`. Shows `DebugLog` string — timestamped log entries from orchestrator events.

## Transcript Entries

Each `TranscriptEntry` is an `ObservableObject` with:

| Property | Type | Purpose |
|----------|------|---------|
| `Role` | `string` | "You" or "AI" |
| `Text` | `string` | Message content (streams in for AI via deltas) |
| `IsThinking` | `bool` | Shows animated thinking dots |
| `Image` | `ImageSource?` | Optional captured frame |
| `ImageCaption` | `string?` | Caption for image |
| `Actions` | `ObservableCollection<ContentAction>` | Detected actionable items |

**Streaming behavior:** When AI responds, `TranscriptDelta` events append text character by character. `TranscriptCompleted` finalizes the entry. The `_currentAiEntry` field tracks the in-progress entry.

**User messages:** `TranscriptCompleted` events starting with `"You:"` create a new user entry and insert it *before* the current AI thinking entry (so the order is always User → AI).

## Content Actions

`ContentActionDetector.Detect(text)` scans completed transcript text for:
- **URLs** → "Open Link" action
- **Email addresses** → "Email" action
- **Phone numbers** → "Call" action

Each action has an emoji icon and a `RelayCommand` that launches the appropriate system handler.

## Navigation Flow Summary

```
App Launch
  ↓
AppShell checks SetupCompleted?
  ├─ No  → SetupPage (permissions + API key)
  │         ↓ SetupFinished event
  │         → MainPage
  └─ Yes → MainPage
               ↕ ⚙ button
           SettingsPage
             ├─ ConnectionSettingsPage
             ├─ VoiceSettingsPage
             ├─ DeviceSettingsPage
             └─ AdvancedSettingsPage
```
