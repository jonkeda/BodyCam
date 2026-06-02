# M48 Phase Map - Post-PoC Architecture Cleanup

**Status:** Draft

This phase map turns the M48 architecture report into implementation-sized
steps. The intent is to keep the app moving from proof-of-concept shape toward
product shape without introducing a large generic framework.

## Decision Summary

- Keep `ICameraCommand` as the name for camera-backed commands such as Look,
  Read, and Scan.
- Do not rename `ICameraCommand` to `IDeviceCommand`.
- Do not use `ICommand` for domain behavior; MAUI/UI commands already own that
  name in this app.
- Add a higher-level assistive action layer when non-camera actions need the
  same invocation path as camera commands.
- Treat transcript/session persistence as a future durable store boundary now,
  but do not build a full database layer until there is a concrete storage
  feature.

## Naming Direction

Recommended future names:

| Concept | Recommended Name | Notes |
| --- | --- | --- |
| UI command | `ICommand` | Existing MAUI/MVVM command surface. Do not reuse for domain actions. |
| Camera-backed workflow | `ICameraCommand` | Keep this for Look, Read, Scan, Find, Watch, etc. when they require camera capture or frame streaming. |
| User-facing action | `IAssistiveAction` | Higher-level intent invoked by UI, button, wake word, tool call, automation, or keyboard. |
| Action executor | `IAssistiveActionService` | Resolves and executes an action by id. |
| Action registry | `IAssistiveActionRegistry` | Lists actions and metadata for settings/tool/button mapping. |
| Trigger origin | `ActionTriggerOrigin` | Broader replacement for `CommandTriggerOrigin` once assistive actions exist. |
| Hardware command | `IDeviceCommand` | Reserve for direct device operations such as pair, reconnect, set volume, enter transfer mode, or download media. |

The key distinction:

```text
button / wake word / UI / tool call
  -> assistive action
  -> camera command, session command, transcript command, or device operation
```

`IDeviceCommand` is not the right name for Look, Listen, Save Transcript, or Ask
because the device is usually the trigger source or capability provider, not the
user intent.

## Target Flow

```text
Pages / ViewModels
ButtonInputManager
Wake word
ToolDispatcher
Keyboard shortcuts
Automation
  -> IAssistiveActionService
      -> ICameraCommandService for camera-backed actions
      -> SessionCoordinator for listen, toggle, ask, stop
      -> transcript/session store boundary for save/review actions
      -> device managers only for direct hardware operations
```

This keeps one behavior per intent while preserving simple provider-specific
services underneath.

## Phase 0 - Lock Architecture Terms

Goal: prevent future naming drift before more code is added.

Scope:

- Document the naming decision above in M48.
- Keep current `ICameraCommand` contracts unchanged for now.
- Treat `ButtonAction` as the temporary enum-based mapping surface.
- Avoid introducing `IDeviceCommand` for user-facing assistive workflows.
- Reserve `IDeviceCommand` only for low-level hardware operations if such a
  boundary becomes necessary later.

Out of scope:

- Renaming all current command contracts.
- Building the assistive action service immediately.
- Introducing persistent transcript/session storage.

Done when:

- M48 has a clear naming decision.
- Future implementation work can tell whether a new behavior belongs under
  `ICameraCommand`, `IAssistiveAction`, or a device-specific service.

## Phase 1 - App Runtime Coordinator

Goal: move long-lived runtime bootstrap out of page lifecycle code.

Scope:

- Add `AppRuntimeCoordinator`.
- Move startup/runtime bootstrap currently living near `MainPage.Loaded` into
  the coordinator.
- Initialize audio managers through the coordinator.
- Start button input through the coordinator.
- Start Bluetooth/device enumeration through the coordinator.
- Trigger known-device auto-reconnect through the coordinator.
- Wire long-lived cross-service listeners in one place.

Out of scope:

- Changing the user-facing behavior of session modes.
- Reworking source selection policy.
- Replacing provider managers.

Done when:

- `MainPage` is mostly page setup, binding, focus, and visual handoff.
- Runtime startup can be tested without constructing the main page.
- Startup ownership is understandable from one service.

## Phase 2 - Source Profile Ownership

Goal: make source selection predictable and owned by one policy surface.

Scope:

- Initialize `SourceProfileManager` from `AppRuntimeCoordinator`.
- Make `SourceProfileManager` the primary owner of startup restore, profile
  apply, device fallback, and persisted active slots.
- Keep `CameraManager`, `AudioInputManager`, and `AudioOutputManager` as active
  slot managers.
- Reduce independent auto-switching policy in device-specific services.
- Make "what the user selected" and "what the app actually uses" converge.

Out of scope:

- Replacing the existing provider manager model.
- Building a generic device framework.

Done when:

- Startup source restore flows through `SourceProfileManager`.
- Device connect/disconnect fallback is policy-driven rather than scattered.
- Tests can verify selected camera/audio/button/output sources after startup.

## Phase 3 - Session Coordinator

Goal: make session/listening behavior a first-class runtime concern instead of
mostly view-model behavior.

Scope:

- Add `SessionCoordinator`.
- Move sleep, wake-word, active-session, listen, stop, and interruption
  transitions toward the coordinator.
- Let `MainViewModel` present state instead of owning session policy.
- Keep `AgentOrchestrator` focused on realtime session lifecycle and message
  routing.
- Route future "listen" or "start listening" actions through this coordinator.

Out of scope:

- Rewriting `AgentOrchestrator`.
- Changing realtime provider behavior.
- Adding durable session history.

Done when:

- A button, UI command, or wake-word event can trigger the same session
  transition path.
- `MainViewModel` no longer needs to know as much about operational session
  policy.
- Session state transitions are testable without page UI.

## Phase 4 - Assistive Action Layer

Goal: unify user-facing action entry points without making camera commands too
generic.

Scope:

- Introduce `IAssistiveAction`, `AssistiveActionRequest`,
  `AssistiveActionContext`, `AssistiveActionResult`,
  `IAssistiveActionRegistry`, and `IAssistiveActionService`.
- Keep `ICameraCommandService` and have camera actions delegate to it.
- Rename or wrap `CommandTriggerOrigin` as `ActionTriggerOrigin`.
- Move button action dispatch out of `MainViewModel` and into the action layer.
- Let wake word, physical buttons, keyboard shortcuts, UI actions, and tool
  calls invoke the same action ids.
- Start with a small action set: `look`, `read`, `scan`, `toggle_session`,
  `listen`, `stop_session`, and `photo`.

Out of scope:

- A mediator bus for every app event.
- A generic plugin loader.
- Replacing all tools at once.
- Renaming `ICameraCommand` to a broader name.

Done when:

- `Look`, `Read`, and `Scan` still use registered camera commands.
- Non-camera actions can be triggered through the same top-level action service.
- Button mappings no longer require `MainViewModel` to contain the behavior
  switch for every future action.

## Phase 5 - Session And Transcript Storage Boundary

Goal: prepare for storing and adding sessions/transcriptions without turning UI
state into the durable data model.

Scope:

- Add a narrow future-facing boundary such as `ISessionHistoryStore` or
  `ITranscriptStore`.
- Define durable models separate from `TranscriptEntry`.
- Include session id, timestamps, role, text, media references, action id,
  trigger origin, source profile, provider id, model id, and retention metadata.
- Let transcript-producing paths emit store-ready events even if the first
  implementation is in-memory or disabled.
- Keep privacy, deletion, and retention requirements explicit.

Out of scope:

- Full search.
- Sync.
- Cloud storage.
- Embeddings or long-term memory.
- Migrating all transcript UI behavior at once.

Done when:

- There is a clear distinction between live transcript UI entries and durable
  transcript records.
- Future "save session", "review session", or "add transcription" features have
  a place to plug in.
- Stored transcript design does not depend on `MainViewModel.Entries`.

## Phase 6 - Settings Store Split

Goal: reduce the size and blast radius of `ISettingsService`.

Scope:

- Introduce narrow store interfaces while keeping `SettingsService` as a
  compatibility facade.
- Start with:
  - `IAppPreferencesStore`
  - `IAiProviderSettingsStore`
  - `IDeviceSettingsStore`
  - `IDiagnosticsSettingsStore`
- Move source profiles, active slots, known devices, and device overrides into
  device settings.
- Move provider instances, endpoint settings, and model selection into AI
  provider settings.
- Keep migration gradual.

Out of scope:

- A one-shot settings rewrite.
- Breaking existing settings pages.
- Changing persisted keys without migration.

Done when:

- New code depends on narrower stores instead of the whole `ISettingsService`.
- `SettingsService` can remain as a bridge for old consumers.
- Settings ownership is visible by concern.

## Phase 7 - Device Capability Boundaries

Goal: clarify hardware-specific boundaries after ownership and action entry
points are cleaner.

Scope:

- Review device operations that do not belong in assistive actions.
- Reserve possible `IDeviceCommand` usage for direct hardware operations only.
- Consider focused capability services for:
  - reconnect policy;
  - recorded media transfer;
  - video recording;
  - device diagnostics;
  - firmware or transport operations.
- Keep provider interfaces and managers simple.

Out of scope:

- A grand device abstraction.
- Converting every provider method into a command.
- Device marketplace or plugin architecture.

Done when:

- User-facing actions and direct hardware operations are not mixed together.
- Device-specific code remains behind provider/capability services.
- The app mental model remains assistive-workflow first.

## Recommended Implementation Order

The lowest-risk path is:

1. Phase 0 - Lock terms.
2. Phase 1 - Add `AppRuntimeCoordinator`.
3. Phase 2 - Make `SourceProfileManager` the source-selection owner.
4. Phase 3 - Add `SessionCoordinator`.
5. Phase 4 - Add the assistive action layer.
6. Phase 5 - Add transcript/session storage boundary.
7. Phase 6 - Split settings by concern.
8. Phase 7 - Clean up remaining device capability boundaries.

Phase 4 can be pulled earlier if button/glasses actions become the next urgent
feature. If that happens, keep it small: introduce the action service and route
only the first few actions through it.

## Non-Goals For M48 Follow-Up

- Do not replace the current provider managers.
- Do not replace all tools with a universal action bus.
- Do not rename `ICameraCommand` just to make it more abstract.
- Do not persist live transcript UI objects directly.
- Do not make source/device startup decisions in page code.
- Do not add a large plugin framework.

## First Follow-Up Milestone Shape

Recommended next implementation milestone:

```text
M49 - Runtime Ownership And Source Selection Cleanup
```

Suggested M49 scope:

- Phase 1: `AppRuntimeCoordinator`.
- Phase 2: `SourceProfileManager` startup ownership.
- Small Phase 0 documentation carry-over if needed.

Then use a later milestone for `SessionCoordinator`, `IAssistiveAction`, and
the transcript/session storage boundary.
