# BodyCam Figma UX Development Plan

**Status:** Draft  
**Created:** 2026-05-21  
**Goal:** Use Figma as the shared UX workbench for BodyCam: product flows, design system, interactive prototypes, accessibility review, and agent-assisted MAUI implementation.

---

## Why Figma For BodyCam

BodyCam is not just a mobile app with a chat feed. It is a wearable AI control surface: microphone state, camera state, glasses connection, API health, transcript confidence, and physical button behavior all need to be legible at a glance.

Figma should help us answer these UX questions before we keep polishing XAML:

- What should the user see when the app is idle, listening for wake word, actively conversing, scanning, or failing?
- How can the app confirm camera/mic privacy without making the screen noisy?
- What does a good "eyes-free first, screen-second" workflow look like for Look, Read, Find, Ask, Photo, and Scan?
- How should hardware troubleshooting feel when glasses, Bluetooth audio, camera capture, or API setup fails?
- Which UI patterns are reusable enough to become BodyCam design components?

---

## Current Product Surface

Use the current app as the baseline, not older wireframes.

| Area | Current implementation | UX risk to explore in Figma |
| --- | --- | --- |
| Home | `MainPage` composed from status bar, transcript, camera tab, overlays, and quick actions | Too much mode switching, unclear active state, action overload |
| Status control | Off / On / Listening segmented icon control | Names and visual states may not match user mental model |
| Transcript | Streaming entries, images, inline actions, scan actions | Needs stronger feedback for thinking, errors, and tool results |
| Camera | Live preview, snapshot overlay, scan overlay | Privacy and capture state need to be unmistakable |
| Quick actions | Look, Read, Find, Ask, Photo, Scan | Need priority, grouping, and one-handed ergonomics |
| Setup | Permissions and API key validation | Needs trust-building and clear recovery states |
| Settings | Connection, Voice & AI, Devices, Advanced | Needs hierarchy; some settings are developer-grade and should not compete with core use |
| Devices | Glasses status, media counts, source profiles, capture tests, audio tests, button mappings | Hardware troubleshooting needs a clearer diagnostic story |

---

## UX Principles

1. **State before features.** The user should always know whether BodyCam is off, wake-word listening, active, scanning, capturing, speaking, or blocked.
2. **Eyes-free first.** The visible UI should confirm and recover; the primary interaction is still voice and glasses buttons.
3. **Trust is visible.** Camera, mic, provider, recording/capture, and API status should be explicit without becoming alarmist.
4. **Hardware is part of the UX.** Connection, battery, audio routing, and button mappings are not "settings clutter"; they are part of making a wearable assistant reliable.
5. **Debuggability stays available.** BodyCam is still a developer/hacker product, so advanced diagnostics should be discoverable without overwhelming the normal path.
6. **Accessibility is not a later pass.** Prototype keyboard, screen reader labels, contrast, large text, and reduced motion states while designing.

---

## Figma File Structure

Create one Figma Design file named **BodyCam UX** and organize it like this:

| Page | Purpose |
| --- | --- |
| `00 Product Map` | FigJam-style flow maps, personas, task lists, state machine diagrams |
| `01 Foundations` | Color, type, spacing, radius, icon, elevation, motion, accessibility tokens |
| `02 Components` | Reusable components and variants mapped to MAUI controls |
| `03 Core Flows` | First-run setup, home loop, camera/transcript, quick actions, settings |
| `04 Hardware & Diagnostics` | Glasses connection, device tests, button mappings, error recovery |
| `05 Prototype` | Clickable prototype entry points for usability testing |
| `06 Dev Handoff` | Frames marked ready for development, annotations, frame links, implementation notes |
| `99 Archive` | Rejected directions and old explorations |

Use semantic frame names:

```text
Flow / Platform / Screen / State
Home / Android / Camera / Active scanning
Settings / Windows / Devices / Glasses connected expanded
Setup / Android / Permissions / Bluetooth denied
```

---

## Design System Workstream

Start by turning the existing MAUI UI into a small BodyCam design system. This gives us stable pieces before exploring new layouts.

### Variables

Define Figma variables for:

- `color.surface`, `color.surface.subtle`, `color.text`, `color.text.muted`
- `color.intent.active`, `color.intent.listening`, `color.intent.off`, `color.intent.error`, `color.intent.success`
- `space.4`, `space.8`, `space.12`, `space.16`, `space.24`
- `radius.4`, `radius.8`, `radius.pill`
- `type.caption`, `type.body`, `type.label`, `type.title`

Create modes for:

- Light
- Dark
- High contrast draft
- Android compact
- Windows desktop

Map these back to `src/BodyCam/Resources/Styles/Colors.xaml` and `Styles.xaml`.

### Components

Build components for the controls we already have or clearly need:

| Figma component | MAUI counterpart |
| --- | --- |
| `StateSegmentedControl` | `StatusBarView` |
| `QuickActionButton` | `QuickActionsView` button style |
| `TranscriptEntry` | `TranscriptView` item template |
| `ThinkingIndicator` | `TranscriptEntry.IsThinking` state |
| `CameraPreviewSurface` | `CameraTabView` |
| `SnapshotOverlay` | `CameraTabView` snapshot overlay |
| `ScanResultOverlay` | `ScanResultOverlay` |
| `SettingsCard` | `SettingsCardView` |
| `ConnectionTestPanel` | `ConnectionSettingsPage` test card |
| `DeviceStatusRow` | `DeviceSettingsPage` connected devices |
| `ExpandableDiagnosticSection` | glasses detail and button mappings |
| `ProviderPicker` | OpenAI / Azure radio group |
| `ModelHealthRow` | model picker plus status mark |

Each component should have variants for normal, pressed/focused, disabled, error, loading, and large text where relevant.

---

## Flow Workstream

Design these flows first, in this order.

### 1. First-Run Setup

Prototype:

- Android permissions: microphone, camera, Bluetooth
- Provider choice: OpenAI vs Azure
- API key validation success, missing key, invalid key, network failure
- Skip path and what the app looks like after skipping

Outcome:

- A setup flow that feels trustworthy and recoverable.
- Clear copy for why each permission is needed.
- Exact empty/error/loading states for implementation.

### 2. Core Home Loop

Prototype:

- Off state
- Wake-word listening state
- Active voice session
- User taps Look while inactive
- User taps Ask and escalates to active
- AI thinking, streaming response, tool result, error result
- Camera tab while active

Outcome:

- Decide whether Home should feel more like a mission control panel, a camera-first interface, or a transcript-first interface.
- Decide how much status appears in the top bar versus the content area.
- Decide whether quick actions should remain six equal buttons or become grouped by intent.

### 3. Camera, Scan, And Capture

Prototype:

- Camera initializing
- Live preview
- Snapshot captured
- QR scan success with one action
- QR scan success with multiple actions
- Scan failure / no code found
- Privacy-sensitive state when camera is on

Outcome:

- A clear overlay system for snapshot and scan results.
- Visual language for camera/mic activity that does not rely only on color.

### 4. Device And Hardware Diagnostics

Prototype:

- No glasses connected
- Glasses connected with battery and media counts
- Bluetooth audio routed incorrectly
- Test recording in progress / failed / succeeded
- Test picture in progress / failed / succeeded
- Button mapping collapsed and expanded

Outcome:

- Make hardware setup understandable without needing logs.
- Separate everyday device status from deeper diagnostics.

### 5. Settings Hierarchy

Prototype:

- Settings hub
- Connection page
- Voice & AI page
- Devices page
- Advanced page
- Tool settings editor

Outcome:

- Clear grouping between everyday settings, hardware setup, AI configuration, and developer diagnostics.
- Reduce visual sameness across long settings pages.

---

## Prototype Testing Plan

Run quick tests against Figma prototypes before implementation.

| Test | Task | Success signal |
| --- | --- | --- |
| New user | Set up BodyCam and reach the home screen | User understands permissions and API setup without explanation |
| Daily use | Start listening, ask what the camera sees, return to off | User can explain current mic/camera state at every step |
| Scan | Scan a QR code and choose the correct action | User notices result and action without hunting |
| Hardware issue | Fix a disconnected or misrouted glasses/audio state | User knows which test to run next |
| Accessibility pass | Navigate core flows with large text and keyboard order | No clipped labels, hidden controls, or ambiguous focus |

Record findings in `.my/figma/research-notes.md` as tests happen.

---

## Figma MCP And Dev Handoff Workflow

The workspace has `.vscode/mcp.json` configured for the official Figma MCP server:

- Official Figma MCP server: `https://mcp.figma.com/mcp`

Use the official remote Figma MCP server as the default path for design context and handoff.

### Design-To-Code Loop

1. Mark a frame or section as ready for dev in Figma.
2. Add annotations for behavior that visuals cannot express: state transitions, timing, focus order, empty states, platform differences.
3. Paste the Figma frame link into the coding agent.
4. Ask the agent to generate .NET MAUI XAML/C# using existing BodyCam components and resources, not React/Tailwind output.
5. Use Figma MCP tools to pull design context, screenshots, and variable definitions.
6. Implement in the smallest matching MAUI file or component.
7. Run UI/unit tests and capture before/after screenshots where useful.
8. Add the Figma frame link to the implementation PR or plan note.

Prompt template:

```text
Use this Figma frame as the source of truth: <frame-url>.
Implement it in BodyCam as .NET MAUI XAML/C#.
Reuse existing components and styles where possible:
- src/BodyCam/Pages/Main/Views/*
- src/BodyCam/Views/SettingsCardView.xaml
- src/BodyCam/Resources/Styles/Colors.xaml
- src/BodyCam/Resources/Styles/Styles.xaml
Preserve existing AutomationId values unless the plan explicitly renames them.
Translate design variables into MAUI resources instead of hardcoding colors repeatedly.
```

### Token Sync

When Figma variables are stable:

- Export or inspect variables for color, spacing, typography, and radius.
- Update `Colors.xaml` and shared styles first.
- Only then update individual pages.
- Add a short note to `.my/figma/token-sync-log.md` with the frame/link and files changed.

### Component Mapping

If Figma Code Connect is available for the account/plan, map reusable Figma components to these MAUI implementation files:

| Figma component | Source path |
| --- | --- |
| `SettingsCard` | `src/BodyCam/Views/SettingsCardView.xaml` |
| `StatusBar` | `src/BodyCam/Pages/Main/Views/StatusBarView.xaml` |
| `QuickActions` | `src/BodyCam/Pages/Main/Views/QuickActionsView.xaml` |
| `Transcript` | `src/BodyCam/Pages/Main/Views/TranscriptView.xaml` |
| `CameraTab` | `src/BodyCam/Pages/Main/Views/CameraTabView.xaml` |

If Code Connect is not available, maintain a lightweight mapping table in this plan and in Figma annotations.

---

## Milestones

### M41 - Figma Foundation

- Create BodyCam UX Figma file.
- Add product map and state machine.
- Import current screenshots for Home, Setup, Settings, Devices.
- Define variables and first component set.
- Document exact current UI gaps found during import.

### M42 - Core Loop Redesign

- Prototype Home states and quick actions.
- Decide camera-first vs transcript-first default.
- Decide state naming and icons.
- Validate large text and dark mode.
- Produce one ready-for-dev Home frame.

### M43 - Setup And Trust

- Prototype setup, provider selection, API key validation, and blocked states.
- Tighten privacy/mic/camera messaging.
- Produce ready-for-dev setup and connection states.

### M44 - Hardware Diagnostics

- Prototype glasses connection and audio/camera diagnostics.
- Redesign button mapping and source profile sections.
- Produce ready-for-dev Devices frames.

### M45 - Handoff And Implementation

- Use MCP to implement one screen/component from Figma end to end.
- Sync Figma variables into MAUI resources.
- Add visual QA screenshots to the implementation note.
- Update UI tests where visual or navigation changes affect automation.

---

## Definition Of Done

A Figma-driven UX change is done when:

- The Figma frame has semantic layer names, auto layout, variables, and component instances where practical.
- The frame includes light/dark behavior and a large text sanity check.
- Empty, loading, success, error, and disabled states are covered if the UI can enter them.
- Dev annotations explain behavior, not just layout.
- The implementation reuses MAUI resources/components or intentionally adds new shared ones.
- Existing `AutomationId` values are preserved or the tests/page objects are updated in the same work item.
- The plan or PR links back to the Figma frame.

---

## References

- [Figma MCP Server introduction](https://developers.figma.com/docs/figma-mcp-server/)
- [Figma MCP tools and prompts](https://developers.figma.com/docs/figma-mcp-server/tools-and-prompts/)
- [Structure Figma files for better code](https://developers.figma.com/docs/figma-mcp-server/structure-figma-file/)
- [Figma Code Connect introduction](https://developers.figma.com/docs/code-connect/)
- [Figma Dev Mode guide](https://help.figma.com/hc/en-us/articles/15023124644247-Guide-to-Dev-Mode)
