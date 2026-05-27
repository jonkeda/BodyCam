# BodyCam Figma Step-By-Step Validation Plan

**Status:** Draft
**Created:** 2026-05-21
**Purpose:** Break the Figma setup into small steps that can be completed and validated one by one.

---

## How To Use This Plan

Work one step at a time. Do not move to the next step until the validation gate is accepted.

Each step has:

- **Goal:** what we are trying to accomplish.
- **Actions:** what we do.
- **Output:** what should exist afterward.
- **Validation:** what you check before approving the next step.

Progress states:

- `[ ]` Not started
- `[~]` In progress
- `[x]` Accepted

---

## Progress Tracker

| Step | Status  | Name                              | Validation owner |
| ---- | ------- | --------------------------------- | ---------------- |
| 0    | `[x]` | Decide workspace and file setup   | User             |
| 1    | `[x]` | Inventory current XAML screens    | User + Codex     |
| 2    | `[x]` | Create clean Figma file structure | User             |
| 3    | `[ ]` | Create tokens from current app    | User + Codex     |
| 4    | `[ ]` | Create primitive controls         | User             |
| 5    | `[ ]` | Create BodyCam product components | User             |
| 6    | `[ ]` | Recreate current screens          | User + Codex     |
| 7    | `[ ]` | Add states and prototypes         | User             |
| 8    | `[ ]` | Test Figma-to-code handoff        | User + Codex     |
| 9    | `[ ]` | Establish maintenance rhythm      | User             |

---

## Step 0 - Workspace Decision

**Goal:** Start clean without losing connection to the current app.

**Actions:**

- Create or choose a Figma project for BodyCam.
- Create one new file named `BodyCam UX System`.
  - Preferred: use the official Figma MCP `create_new_file` tool if the active MCP client exposes it.
  - Fallback: create the file manually in Figma.
  - If MCP creates it in Drafts, move it into the BodyCam project afterward if desired.
- Start and authorize the `figma` server in VS Code using `MCP: List Servers` or the inline Start action in `.vscode/mcp.json`.
- Keep this file separate from experiments, screenshots dumps, and old design work.
- Confirm `.vscode/mcp.json` only contains the official Figma MCP server.

**Output:**

- A clean Figma file exists.
- The repo MCP config points at `https://mcp.figma.com/mcp`.

**Validation:**

- You can open the empty `BodyCam UX System` file.
- If created via MCP, the file link is recorded or shared back into this workstream.
- VS Code shows the Figma MCP server as running and authorized.
- No legacy Framelink/token setup is required.

**Stop point:** approve the clean workspace before any components or screens are created.

---

## Step 1 - Current XAML Inventory

**Goal:** Know exactly what exists before recreating anything in Figma.

**Actions:**

- Inventory all screen XAML files.
- Extract pages, controls, layouts, colors, font sizes, spacing, corner radii, and AutomationIds.
- Identify repeated patterns and possible Figma components.

**Output:**

- `.my/figma/xaml-screen-inventory.md`
- `.my/figma/xaml-token-inventory.md`
- `.my/figma/xaml-component-candidates.md`

**Validation:**

- The inventory includes Home, Setup, Settings hub, Connection, Voice, Devices, Advanced, Glasses, Media Gallery, Audio Player, and Image Viewer.
- The inventory lists both resource colors and hardcoded colors.
- The component candidates make sense and do not over-componentize labels/layout grids.

**Stop point:** approve the inventory before creating Figma components.

---

## Step 2 - Figma File Structure

**Goal:** Give the clean Figma file a maintainable shape.

**Actions:**

Create these Figma pages:

```text
00 Read Me
01 Tokens
02 Primitive Controls
03 Product Components
04 Current Screens
05 Proposed Screens
06 Prototypes
07 Dev Handoff
99 Archive
```

Add a short `00 Read Me` section containing:

- Repo link/path.
- Naming convention.
- Current source of truth note: MAUI is implementation source; Figma becomes UX maintenance source.
- Links to `.my/figma/screen-maintenance-setup.md`.

**Output:**

- Figma file has the agreed pages.
- `00 Read Me` explains how the file is organized.

**Validation:**

- You can quickly find tokens, primitive controls, product components, current screens, proposals, prototypes, and handoff frames.
- No screen design work has started yet.

**Stop point:** approve the structure before adding tokens.

---

## Step 3 - Tokens From Current App

**Goal:** Turn current XAML styling into Figma variables and text styles.

**Actions:**

- Add color variables from `Colors.xaml`.
- Add semantic color variables for app background, surfaces, text, muted text, primary action, info, success, error, and focus.
- Add light/dark modes.
- Add type styles: `Caption`, `Small`, `Body`, `Subtitle`, `Title`, `Headline`.
- Add spacing variables: `2`, `4`, `8`, `12`, `16`, `24`.
- Add radius variables: `4`, `8`, `14`, `pill`.

**Output:**

- `01 Tokens` contains the first BodyCam token set.
- `.my/figma/token-sync-log.md` records token names and source XAML keys.

**Validation:**

- Tokens cover current XAML resources and common hardcoded colors.
- Tokens are semantic enough to survive redesign.
- Light and dark mode values are visible in Figma.

**Stop point:** approve tokens before creating controls.

---

## Step 4 - Primitive Controls

**Goal:** Create reusable controls that mirror the styled MAUI controls we actually use.

**Actions:**

Create Figma primitive controls:

- `Button`
- `IconButton`
- `TextField`
- `MultilineTextField`
- `SelectField`
- `RadioGroup`
- `ToggleRow`
- `DisclosureSection`
- `SegmentedTabs`
- `ProgressIndicator`

Use text styles for labels instead of a generic `Label` component.

**Output:**

- `02 Primitive Controls` contains a control playground.
- Each control has useful variants.
- Controls use variables, not raw colors.

**Validation:**

- Buttons show primary, secondary, outline, danger, disabled, focused, and icon-only variants.
- `DisclosureSection` has collapsed and expanded variants.
- `SegmentedTabs` supports the Transcript/Camera pattern.
- Text fields and select fields match settings usage.

**Stop point:** approve primitive controls before BodyCam-specific components.

---

## Step 5 - Product Components

**Goal:** Build BodyCam-specific UX components from primitives.

**Actions:**

Create product components:

- `StatusBar`
- `StateSegmentButton`
- `QuickActionButton`
- `TranscriptEntry`
- `CameraPreviewSurface`
- `SnapshotOverlay`
- `ScanResultOverlay`
- `SettingsCard`
- `ConnectionTestPanel`
- `ModelHealthRow`
- `DeviceStatusHeader`
- `ExpandableSettingsSection`

Annotate each component with its MAUI mapping.

**Output:**

- `03 Product Components` contains BodyCam-specific components.
- Components map to XAML files where possible.

**Validation:**

- `StatusBar` covers off, wake-word/on, active, connecting, and error states.
- `TranscriptEntry` covers user, AI, thinking, scan/tool, image, actions, and error states.
- `SettingsCard` matches the current settings hub pattern.
- Components are built from primitives where practical.

**Stop point:** approve product components before recreating full screens.

---

## Step 6 - Recreate Current Screens

**Goal:** Capture today’s app as the baseline in Figma.

**Actions:**

Recreate current screens in `04 Current Screens`:

- `Home / Android / Off`
- `Home / Android / On`
- `Home / Android / Active`
- `Home / Android / Camera`
- `Home / Android / Scan result`
- `Setup / Android / Permission`
- `Setup / Android / API key`
- `Settings / Android / Hub`
- `Settings / Android / Connection OpenAI`
- `Settings / Android / Connection Azure`
- `Settings / Android / Voice`
- `Settings / Android / Devices disconnected`
- `Settings / Android / Devices connected expanded`
- `Settings / Android / Advanced`

Use screenshots plus XAML, not XAML alone.

**Output:**

- Current screens exist in Figma.
- Each screen has annotations for source XAML files and key AutomationIds.

**Validation:**

- Screens look close enough to the current app to serve as a baseline.
- Known mismatches are annotated.
- No redesign decisions are mixed into current-state frames.

**Stop point:** approve the baseline before proposing changes.

---

## Step 7 - States And Prototypes

**Goal:** Make the Figma file useful for UX validation, not just static screenshots.

**Actions:**

- Add state frames for loading, empty, success, error, disabled, permission denied, no camera, no API key, and disconnected device.
- Create prototypes for:
  - First-run setup.
  - Home off to active.
  - Look/Read/Scan quick actions.
  - Camera snapshot flow.
  - Device troubleshooting.
  - Settings navigation.

**Output:**

- `06 Prototypes` contains clickable flows.
- State coverage is documented.

**Validation:**

- You can click through the main flows without needing code.
- Each flow has clear stop/error/recovery states.
- Accessibility notes exist for focus order and screen reader labels where relevant.

**Stop point:** approve prototypes before using Figma for implementation.

---

## Step 8 - Figma-To-Code Handoff Test

**Goal:** Prove that Figma can drive a real, small MAUI change.

**Actions:**

- Pick one small component or screen section.
- Mark the Figma frame as ready in `07 Dev Handoff`.
- Use Figma MCP to read design context.
- Implement the change in MAUI.
- Preserve AutomationIds.
- Run relevant tests or at least build the touched project if tests are not practical.

Good first candidates:

- `QuickActionButton` spacing/style.
- `SegmentedTabs` visual cleanup.
- `SettingsCard` styling.
- `DisclosureSection` visual treatment in Devices.

**Output:**

- One real code change shipped from a Figma frame.
- `.my/figma/token-sync-log.md` or a handoff note records the round trip.

**Validation:**

- The implementation matches the approved frame closely enough.
- Existing bindings still work.
- AutomationIds are preserved or tests are updated.
- Build/tests pass or blockers are documented.

**Stop point:** approve the handoff workflow before using Figma broadly.

---

## Step 9 - Maintenance Rhythm

**Goal:** Prevent Figma and XAML from drifting apart.

**Actions:**

- Keep current app frames in `04 Current Screens`.
- Keep future ideas in `05 Proposed Screens`.
- Only move frames to `07 Dev Handoff` when ready to implement.
- Update Figma when XAML changes directly.
- Update XAML when an approved Figma change ships.
- Record token changes in `.my/figma/token-sync-log.md`.

**Output:**

- A stable maintenance habit.
- Figma remains useful after the initial setup.

**Validation:**

- Every production screen has a Figma baseline.
- Every reusable component has a code mapping or a design-only note.
- Proposed work is visually separated from implemented work.

**Stop point:** this is the ongoing operating model.

---

## Suggested First Session

For the first working session, do only Steps 0 and 1.

Deliverables:

- Confirm `BodyCam UX System` exists.
- Generate the three XAML inventory files.
- Review whether the proposed component candidates feel right.

After that, Step 2 becomes a clean Figma setup task instead of guesswork.
