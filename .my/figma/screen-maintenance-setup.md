# BodyCam Figma Screen Maintenance Setup

**Status:** Draft  
**Created:** 2026-05-21  
**Purpose:** Define how to set up Figma so BodyCam screens can be maintained over time, starting from the .NET MAUI XAML that already exists.

---

## Short Answer

Yes, we should add components.  
Yes, we should add colors/tokens.  
Yes, we can reverse-engineer the existing XAML, but we should treat that as a structured baseline import, not a perfect automatic conversion.

The right setup is:

1. Build a small BodyCam Figma library from the current MAUI app.
2. Convert repeated XAML views/styles into Figma components and variables.
3. Recreate current screens in Figma using those components.
4. Use Figma as the design source for future screen changes.
5. Keep MAUI as the implementation source, with a clear mapping between Figma components and XAML files.

---

## What Figma Should Own

Figma should own:

- Screen layouts and responsive intent.
- Visual hierarchy.
- Component variants and states.
- Color, spacing, type, radius, and focus tokens.
- Empty/loading/success/error states.
- Prototype flows for setup, home, camera, scan, devices, and settings.
- Dev annotations for behavior that is not obvious visually.

Figma should not own:

- ViewModel logic.
- MAUI binding expressions.
- Hardware/audio/camera implementation details.
- Exact platform quirks that must be solved in C# or native platform code.
- Every one-off debug control before it proves reusable.

That boundary matters. Figma should help us reason about the UX; it should not become a second implementation that drifts from the code.

---

## Current Code Sources

Use these files as the import baseline:

| Area | XAML source |
| --- | --- |
| App colors | `src/BodyCam/Resources/Styles/Colors.xaml` |
| Shared styles | `src/BodyCam/Resources/Styles/Styles.xaml` |
| Shell/title bar | `src/BodyCam/AppShell.xaml` |
| Home composition | `src/BodyCam/Pages/Main/MainPage.xaml` |
| Status bar | `src/BodyCam/Pages/Main/Views/StatusBarView.xaml` |
| Transcript | `src/BodyCam/Pages/Main/Views/TranscriptView.xaml` |
| Camera tab | `src/BodyCam/Pages/Main/Views/CameraTabView.xaml` |
| Quick actions | `src/BodyCam/Pages/Main/Views/QuickActionsView.xaml` |
| Scan result overlay | `src/BodyCam/Pages/Main/Views/ScanResultOverlay.xaml` |
| Setup | `src/BodyCam/Pages/Setup/SetupPage.xaml` |
| Settings hub | `src/BodyCam/Pages/Settings/SettingsPage.xaml` |
| Settings card | `src/BodyCam/Views/SettingsCardView.xaml` |
| Connection settings | `src/BodyCam/Pages/Settings/ConnectionSettingsPage.xaml` |
| Voice settings | `src/BodyCam/Pages/Settings/VoiceSettingsPage.xaml` |
| Device settings | `src/BodyCam/Pages/Settings/DeviceSettingsPage.xaml` |
| Advanced settings | `src/BodyCam/Pages/Settings/AdvancedSettingsPage.xaml` |
| Glasses page | `src/BodyCam/Pages/GlassesPage.xaml` |
| Media gallery | `src/BodyCam/Pages/MediaGalleryPage.xaml` |

---

## Figma File Setup

Create one Figma file named **BodyCam UX System**.

Use these pages:

| Figma page | Contents |
| --- | --- |
| `00 Read Me` | Rules, links to repo files, naming conventions, current priorities |
| `01 Tokens` | Colors, type, spacing, radius, focus, elevation, motion |
| `02 Components` | Reusable screen building blocks |
| `03 Current Screens` | Recreated current MAUI screens from XAML/screenshots |
| `04 Proposed Screens` | New UX explorations and redesigns |
| `05 Prototypes` | Clickable user flows |
| `06 Dev Handoff` | Approved frames, implementation notes, frame links |
| `99 Archive` | Old or rejected explorations |

Use naming like:

```text
Screen / Platform / State
Home / Android / Off
Home / Android / Active camera
Settings / Windows / Devices connected expanded
Setup / Android / Bluetooth denied
```

For component names, mirror the MAUI component where possible:

```text
Component / StatusBar
Component / QuickActionButton
Component / TranscriptEntry
Component / SettingsCard
Component / ScanResultOverlay
```

---

## Tokens And Colors

Start from the current XAML colors, then normalize them into semantic tokens.

### Current Raw Colors

Current resources include:

| XAML key | Value |
| --- | --- |
| `Primary` | `#512BD4` |
| `PrimaryDark` | `#ac99ea` |
| `PrimaryDarkText` | `#242424` |
| `Secondary` | `#DFD8F7` |
| `SecondaryDarkText` | `#9880e5` |
| `Tertiary` | `#2B0B98` |
| `OffBlack` | `#1f1f1f` |
| `Gray100` | `#E1E1E1` |
| `Gray200` | `#C8C8C8` |
| `Gray300` | `#ACACAC` |
| `Gray400` | `#919191` |
| `Gray500` | `#6E6E6E` |
| `Gray600` | `#404040` |
| `Gray900` | `#212121` |
| `Gray950` | `#141414` |

Also inventory hardcoded XAML colors such as `#FAFAFA`, `#F0F0F0`, `#222222`, `#2A2A2A`, `#E3F2FD`, `#1A3A5C`, `#1565C0`, `#64B5F6`, `Red`, `Green`, `White`, `Black`, and `Gray`.

### Recommended Figma Variables

Use semantic variable names so future redesigns do not leak implementation names into design decisions:

| Figma variable | Initial source |
| --- | --- |
| `color.bg.app.light` | `White` |
| `color.bg.app.dark` | `OffBlack` |
| `color.bg.surface.light` | `#FAFAFA` / `White` |
| `color.bg.surface.dark` | `#1A1A1A` / `#2A2A2A` |
| `color.bg.subtle.light` | `Gray100` / `#F0F0F0` |
| `color.bg.subtle.dark` | `Gray900` / `#222222` |
| `color.text.primary.light` | `Black` / `Gray900` |
| `color.text.primary.dark` | `White` / `Gray100` |
| `color.text.muted.light` | `Gray600` |
| `color.text.muted.dark` | `Gray300` |
| `color.action.primary.light` | `Primary` |
| `color.action.primary.dark` | `PrimaryDark` |
| `color.intent.success` | `Green` replacement token |
| `color.intent.error` | `Red` replacement token |
| `color.intent.info.bg.light` | `#E3F2FD` |
| `color.intent.info.text.light` | `#1565C0` |
| `color.intent.info.bg.dark` | `#1A3A5C` |
| `color.intent.info.text.dark` | `#64B5F6` |

Create Figma modes for:

- `Light`
- `Dark`
- `High contrast draft`

Later, mirror stable token changes back into `Colors.xaml` and `Styles.xaml`.

---

## Typography And Spacing

Define text styles from the current MAUI scale:

| Figma text style | Current MAUI source |
| --- | --- |
| `Caption` | `FontSize="12"` |
| `Small` | `FontSize="13"` |
| `Body` | `FontSize="14"` |
| `Subtitle` | `FontSize="16"` / MAUI `Subtitle` |
| `Title` | `FontSize="20"` |
| `Headline` | `FontSize="32"` |

Use spacing variables:

| Variable | Use |
| --- | --- |
| `space.2` | dense row gap |
| `space.4` | tiny internal gap |
| `space.8` | button/card internal gap |
| `space.12` | default section spacing |
| `space.16` | page padding |
| `space.24` | major section gap |

Use radius variables:

| Variable | Current source |
| --- | --- |
| `radius.4` | transcript/actions |
| `radius.8` | buttons, settings cards, overlays |
| `radius.14` | state segment buttons |
| `radius.pill` | segmented status container |

---

## Components To Add

Add components only where they represent reusable product language or repeated XAML. Do not componentize every label and grid.

### Standard Controls

MAUI already gives us standard controls such as `Button`, `Label`, `Entry`, `Editor`, `Picker`, `RadioButton`, `Switch`, `Slider`, `ProgressBar`, `Image`, `ScrollView`, `Grid`, and stack layouts. Figma does not automatically know about those controls in a reusable way. We should create a small BodyCam control kit that mirrors the styled controls we actually use.

Use this rule:

- **Labels:** usually text styles, not components.
- **Plain layout containers:** usually Auto Layout frames, not components.
- **Buttons:** components, because states, intent, padding, radius, focus, and disabled behavior matter.
- **Form controls:** components when they appear as a labeled row or reusable settings pattern.
- **One-off debug UI:** only componentize after it repeats.

| MAUI control | Figma representation | Reuse guidance |
| --- | --- | --- |
| `Label` | Text layer using styles like `Body`, `Caption`, `SectionHeader` | Do not create a generic Label component unless it has a special structure |
| `Button` | `Button` component with variants | Create variants for primary, secondary, outline, danger, icon-only, disabled, focused |
| `Entry` | `TextField` or `SettingsFieldRow` component | Componentize with label, helper/error text, secure/masked state |
| `Editor` | `MultilineTextField` component | Use for system instructions and long text settings |
| `Picker` | `SelectField` or `SettingsPickerRow` component | Componentize because picker rows repeat in settings |
| `RadioButton` | `RadioOption` and `RadioGroup` component | Use for OpenAI/Azure provider selection |
| `Switch` | `ToggleRow` component | Use for settings rows, especially Advanced settings |
| `Slider` | `SliderRow` component if reused | Add later only when a real setting needs it |
| `ProgressBar` | `ProgressIndicator` component | Use for imports, validation, or setup progress |
| Expander / disclosure section | `DisclosureSection` component | Use for connected device details, button mappings, and diagnostics |
| Tabs / segmented navigation | `TabControl` or `SegmentedTabs` component | Use for Home transcript/camera switching and future local tab groups |
| `Image` | Plain image layer or domain component | Componentize only for snapshots, previews, thumbnails |
| `Grid` / stacks | Auto Layout frames | Treat as layout, not controls |
| `CollectionView` | Repeating component instances | Use item components such as `TranscriptEntry` or `MediaTile` |

The design system should therefore have both:

- **Primitive controls:** `Button`, `IconButton`, `TextField`, `SelectField`, `ToggleRow`, `RadioGroup`, `DisclosureSection`, `SegmentedTabs`.
- **Product components:** `StatusBar`, `QuickActionButton`, `TranscriptEntry`, `SettingsCard`, `CameraPreviewSurface`, `ScanResultOverlay`.

Primitive controls keep the UI consistent. Product components express BodyCam-specific UX.

### Expanders And Tabs

BodyCam should treat expanders and tabs as reusable interaction patterns, not just ad-hoc rows.

Current examples:

- `DeviceSettingsPage.xaml` uses tappable rows with `▶` / `▼` to expand glasses details and button mappings.
- `MainPage.xaml` uses two buttons as a local tab switcher for Transcript and Camera.

Recommended Figma primitives:

| Pattern | Figma component | Variants | Code mapping |
| --- | --- | --- | --- |
| Expander | `DisclosureSection` | `collapsed`, `expanded`, `disabled`, `error`, `withStatus`, `withActions` | Current custom rows in `DeviceSettingsPage.xaml`; possible future MAUI Toolkit `Expander` |
| Local tabs | `SegmentedTabs` | `twoTabs`, `threeTabs`, `compact`, `comfortable`, `light`, `dark` | Transcript/Camera switcher in `MainPage.xaml` |
| App-level navigation | `NavigationSurface` | `root`, `pushed`, `modal`, `withBack`, `withAction` | `AppShell.xaml` and Shell title/back behavior |

Use tabs only for peer views within the same task. Use expanders for progressive disclosure inside a single task. For example:

- Transcript and Camera are peers, so tabs are appropriate.
- Glasses firmware, MAC address, media counts, and disconnect/remove actions are supporting detail, so an expander is appropriate.
- Connection, Voice, Devices, and Advanced are not tabs right now; they are separate settings pages from the Settings hub.

Figma annotations for these should include:

- Default open/closed state.
- What changes the state.
- Whether state persists after navigation.
- Keyboard/focus order.
- Screen reader label, such as "Glasses details, expanded" or "Camera tab selected".

### Priority 1 Components

| Figma component | Why | Code mapping |
| --- | --- | --- |
| `StatusBar` | Central app state control | `StatusBarView.xaml` |
| `StateSegmentButton` | Off/On/Listening state variants | inside `StatusBarView.xaml` |
| `QuickActionButton` | Six home actions share a pattern | `QuickActionsView.xaml` |
| `TranscriptEntry` | Core chat/vision output pattern | `TranscriptView.xaml` |
| `SettingsCard` | Repeated settings navigation card | `SettingsCardView.xaml` |
| `CameraPreviewSurface` | Camera state, placeholder, active preview | `CameraTabView.xaml` |
| `SnapshotOverlay` | Reusable captured-image overlay | `CameraTabView.xaml` |
| `ScanResultOverlay` | Scan result and action sheet behavior | `ScanResultOverlay.xaml` |

### Priority 2 Components

| Figma component | Why | Code mapping |
| --- | --- | --- |
| `ConnectionTestPanel` | Provider/API health is a recurring trust pattern | `ConnectionSettingsPage.xaml` |
| `ModelHealthRow` | Picker plus status mark repeats | `ConnectionSettingsPage.xaml` |
| `ProviderChoice` | OpenAI/Azure selection | `ConnectionSettingsPage.xaml` |
| `DeviceStatusHeader` | Glasses and connected devices | `DeviceSettingsPage.xaml` |
| `ExpandableSettingsSection` | Device details and button mappings | `DeviceSettingsPage.xaml` |
| `SettingsToggleRow` | Advanced switches and telemetry toggles | `AdvancedSettingsPage.xaml` |
| `SettingsFieldRow` | Label plus entry/picker | settings pages |

### Recommended Variants

Use Figma variants for:

- State: `default`, `focused`, `pressed`, `disabled`
- Intent: `neutral`, `primary`, `danger`, `success`, `info`
- Mode: `light`, `dark`, `highContrast`
- Density: `compact`, `comfortable`
- Content: `withIcon`, `textOnly`, `withStatus`

For BodyCam-specific components:

- `StatusBar`: `off`, `wakeWord`, `active`, `connecting`, `error`
- `TranscriptEntry`: `user`, `ai`, `thinking`, `tool`, `scan`, `error`, `withImage`, `withActions`
- `CameraPreviewSurface`: `initializing`, `active`, `noCamera`, `permissionDenied`, `captureError`
- `DeviceStatusHeader`: `disconnected`, `scanning`, `connected`, `lowBattery`, `audioIssue`

---

## Reverse-Engineering Existing XAML

We can reverse-engineer the existing UI in four passes.

### Pass 1 - Inventory

Read all screen XAML and extract:

- Page names and routes.
- Layout hierarchy: `Grid`, `VerticalStackLayout`, `HorizontalStackLayout`, `ScrollView`, `CollectionView`.
- Shared styles and resource keys.
- Hardcoded colors, font sizes, padding, margins, heights, widths, and corner radii.
- Automation IDs.
- Binding names that imply states.

Output:

- `xaml-screen-inventory.md`
- `xaml-token-inventory.md`
- `xaml-component-candidates.md`

### Pass 2 - Screenshot Baseline

Capture the current app in representative states:

- Home off
- Home wake-word/on
- Home active
- Transcript with user, AI, image, and action entries
- Camera initializing / active / snapshot
- Scan result overlay
- Setup permission step
- Setup API key step
- Settings hub
- Connection settings, OpenAI
- Connection settings, Azure
- Devices disconnected
- Devices connected expanded
- Voice settings
- Advanced settings

Use screenshots as visual truth, because XAML alone does not reveal rendered platform details.

### Pass 3 - Figma Reconstruction

Rebuild each screen manually or agent-assisted in Figma:

1. Create the frame at target device size.
2. Use Auto Layout to mirror MAUI stacks and grids.
3. Use variables instead of raw colors.
4. Replace repeated structures with component instances.
5. Add notes for bindings and states.
6. Mark uncertain platform details as annotations, not fake precision.

### Pass 4 - Drift Check

Compare Figma frames against app screenshots:

- Layout spacing close enough?
- Component state names match ViewModel state?
- Colors/tokens match current resources?
- Text does not clip at large size?
- Figma component maps to one existing XAML file or an intentional new one?

---

## How Much Can Be Automatic?

| Task | Automation level | Notes |
| --- | --- | --- |
| Extract colors from XAML | High | Can parse `Colors.xaml` and hardcoded color attributes |
| Extract font sizes/spacing | High | Can parse attributes, but semantic meaning needs cleanup |
| Identify component candidates | Medium | Repeated XAML and existing `ContentView`s give strong hints |
| Create Figma components | Medium | Possible with MCP-assisted canvas writes, but needs review |
| Recreate exact screens | Medium-low | MAUI layout behavior differs from Figma; screenshots are needed |
| Infer all states | Medium | Bindings reveal many states, but runtime examples are better |
| Generate final MAUI from Figma | Medium | Useful for diffs, but must preserve bindings, AutomationIds, and platform behavior |

So: yes, reverse engineering is practical. The best result is a curated design system, not a one-click conversion.

---

## Using Figma MCP

The workspace now keeps only the official Figma MCP server in `.vscode/mcp.json`:

```json
{
  "servers": {
    "figma": {
      "type": "http",
      "url": "https://mcp.figma.com/mcp"
    }
  }
}
```

Use MCP for:

- Reading design context from selected frames or frame links.
- Pulling variables, components, and layout data into the coding workflow.
- Creating or updating Figma canvas content when the workflow supports it.
- Keeping implementation prompts grounded in real frame names and component names.

Use this agent prompt when implementing from Figma:

```text
Use this Figma frame as the UX source: <frame-url>.
Implement it in .NET MAUI XAML/C#.
Prefer existing BodyCam components and resource dictionaries.
Preserve current AutomationId values unless the Figma handoff explicitly says otherwise.
Map Figma variables to Colors.xaml/Styles.xaml resources.
Do not replace ViewModel behavior unless the handoff requires it.
```

---

## Code Connect

If the Figma account supports Code Connect, use it for the stable components:

| Figma component | Code file |
| --- | --- |
| `StatusBar` | `src/BodyCam/Pages/Main/Views/StatusBarView.xaml` |
| `QuickActionButton` | `src/BodyCam/Pages/Main/Views/QuickActionsView.xaml` |
| `TranscriptEntry` | `src/BodyCam/Pages/Main/Views/TranscriptView.xaml` |
| `CameraPreviewSurface` | `src/BodyCam/Pages/Main/Views/CameraTabView.xaml` |
| `ScanResultOverlay` | `src/BodyCam/Pages/Main/Views/ScanResultOverlay.xaml` |
| `SettingsCard` | `src/BodyCam/Views/SettingsCardView.xaml` |

Code Connect is most valuable once Figma components are stable. Do not start there. First create the design components, then map them to code.

---

## Maintenance Rules

1. Every production screen has a matching frame in `03 Current Screens`.
2. Every proposed redesign starts in `04 Proposed Screens`.
3. Every reusable Figma component has a mapped MAUI file or a note saying it is design-only.
4. Every design handoff includes states, not just a happy path.
5. Colors, spacing, radius, and type use variables unless there is a temporary annotation.
6. New XAML hardcoded colors should either become tokens or be documented as intentional one-offs.
7. Screens should be recreated using component instances; detach only for exploration.
8. Keep `AutomationId` names visible in Figma annotations for test-sensitive controls.
9. When code changes without Figma changing, update `03 Current Screens` or record the drift.
10. When Figma changes without code changing, keep it in `04 Proposed Screens` until implemented.

---

## First Implementation Checklist

1. Create the **BodyCam UX System** Figma file.
2. Add the page structure from this document.
3. Import current color tokens from `Colors.xaml`.
4. Add semantic color variables and light/dark modes.
5. Create `StatusBar`, `QuickActionButton`, `TranscriptEntry`, and `SettingsCard`.
6. Recreate `Home / Android / Off` from `MainPage.xaml` plus screenshots.
7. Recreate `Settings / Android / Hub`.
8. Add annotations with code file paths and AutomationIds.
9. Use the official Figma MCP server to pull one frame into the coding workflow.
10. Make one small MAUI change from Figma and document the round trip.

---

## References

- [Figma MCP Server introduction](https://developers.figma.com/docs/figma-mcp-server/)
- [Figma Code Connect introduction](https://developers.figma.com/docs/code-connect/)
- [Figma Auto Layout guide](https://help.figma.com/hc/en-us/articles/360040451373-Guide-to-auto-layout)
- [Figma variants guide](https://help.figma.com/hc/en-us/articles/360056440594-Create-and-use-variants)
