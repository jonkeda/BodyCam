# M48 Report - Post-PoC App Architecture Review

## Executive Summary

BodyCam is past the point where the main architectural question is "can this be
made to work?" The app already proves a lot:

- multiple AI providers can be plugged in;
- multiple camera and audio providers can be plugged in;
- assistive camera commands can run through a shared command path;
- glasses, USB cameras, and platform devices can participate in the same app.

That is a strong base.

The main architectural problem now is different:

**the app has several good plug-and-play seams, but too many runtime owners and
too much cross-cutting policy spread across startup, UI, managers, and
device-specific services.**

So the recommendation is not a rewrite. The recommendation is to keep the
current registry/provider direction, simplify runtime ownership, and organize
the app more clearly around blind-first assistive workflows.

## What Is Already Good

### 1. Provider And Registry Seams Are Real

BodyCam already has several good "plug and play" patterns:

- `IAiProviderRegistry` for AI providers;
- `ICameraCommandRegistry` for camera commands;
- `ToolDispatcher` for tool registration;
- `IEnumerable<ICameraProvider>` for camera providers;
- `IEnumerable<IAudioInputProvider>` and `IEnumerable<IAudioOutputProvider>`
  for audio providers;
- `IEnumerable<ISourceProfile>` for device/source profiles.

This is the right direction. The app does not need a more abstract plugin
system than this.

### 2. The Provider Managers Are Simple Enough

`CameraManager`, `AudioInputManager`, and `AudioOutputManager` are not perfect,
but they are understandable:

```text
provider list
  -> one active provider
  -> simple fallback
  -> persistence of active choice
```

That is good-enough OOP. It keeps "device slot" logic contained without a big
inheritance tree or heavy framework.

### 3. AI Provider Architecture Is Moving In A Product Direction

`AiProviderRegistry`, provider definitions, capability flags, and
`AiProviderInstanceStore` are a healthy step beyond hard-coded OpenAI/Azure
branching.

This is exactly the sort of registry architecture worth keeping:

- providers declare capability;
- UI can inspect that capability;
- runtime can normalize IDs and resolve a provider without giant switch
  statements everywhere.

### 4. Camera Commands Are Better Than Ad-Hoc Prompt Actions

The newer `Look` / `Read` / `Scan` command path is cleaner than older direct
camera capture plus prompt glue.

That path is worth extending because it is:

- user-intent driven;
- provider-aware through `CameraManager`;
- transcript-aware;
- easier to test than page-specific logic.

## The Main Architectural Problems

## 1. Too Many Runtime Owners

Right now, several different places try to own runtime behavior:

- `MauiProgram` performs startup wiring and side-effect initialization;
- `MainPage.Loaded` initializes audio, button input, Bluetooth enumeration, and
  HeyCyan auto-reconnect;
- `MainViewModel` owns session mode, camera-preview state, transcript state,
  camera actions, and part of assistive behavior;
- `AgentOrchestrator` owns realtime session lifecycle and some tool dispatch;
- `CameraManager` reacts to HeyCyan state changes;
- `HeyCyanAudioRouter` reacts to glasses session changes and flips audio
  providers;
- `SourceProfileManager` exists, but is not clearly the single runtime owner of
  source selection.

Each piece made sense while features were being added. Together, they make the
app harder to reason about than it needs to be.

This is the biggest "beyond PoC" smell.

## 2. The App Is Still Slightly Device-Centric Instead Of User-Intent-Centric

The product is not "an app that happens to talk to devices." It is "an
assistive app for visually impaired and blind users that can use devices when
helpful."

That means the primary architecture should be centered on intents like:

- look;
- read;
- scan;
- ask;
- remember;
- navigate;
- call or message;
- capture a photo or recording for later.

Today, the app is partly organized this way, but partly organized around device
plumbing and per-platform setup.

That is normal for a PoC. It becomes tiring in a product.

## 3. `ISettingsService` Is Too Big

`ISettingsService` currently mixes:

- AI provider settings;
- model settings;
- voice settings;
- command defaults;
- runtime device slot state;
- device JSON state;
- diagnostics flags;
- onboarding state;
- device-specific camera settings;
- known-device reconnect data.

This makes it convenient at first, but it creates several problems:

- too many unrelated consumers depend on one interface;
- migration and validation get harder;
- runtime state and durable preferences blur together;
- the app keeps adding one more property instead of choosing clearer settings
  boundaries.

This is now one of the clearest cleanup targets.

## 4. Source Selection Is Still Split

M47 already showed this clearly, and it still matters at the app level:

- the manager layer exists;
- the profile layer exists;
- device-specific auto-selection also exists;
- startup does not clearly hand control to one source-selection owner.

This creates drift between:

- what the user selected;
- what startup restored;
- what a device-specific service auto-switched;
- what the current managers are actually using.

That is fine for a lab app. It is not ideal for an assistive product where
predictability matters.

## 5. The Tool Story Is Improved, But Still Transitional

The app currently has multiple action paths:

- camera commands;
- generic tools;
- wake word quick actions;
- button actions;
- direct view-model actions;
- realtime tool calls.

The good news is that some newer tools already delegate to
`ICameraCommandService`, which is exactly what we want.

The remaining issue is that the app still has multiple top-level ways to express
"do a user-facing assistive action." That is not catastrophic, but it is a sign
that the product architecture is still converging.

## 6. Startup Work Lives Too Close To The Page

`MainPage.Loaded` currently performs meaningful runtime setup:

- audio manager initialization;
- output manager initialization;
- button input startup;
- Bluetooth enumeration startup;
- HeyCyan auto-reconnect trigger;
- router wiring.

That is more than page setup. It is app runtime bootstrap.

The page should care about:

- binding;
- focus;
- visual state;
- maybe preview activation.

It should not be the main runtime owner.

## What The Architecture Should Optimize For Now

Past PoC, the architecture should optimize for:

1. reliability;
2. predictability;
3. clear ownership;
4. easy addition of a new provider or workflow;
5. keeping the app clearly centered on blind-user assistive tasks.

It should not optimize for:

1. maximum abstraction;
2. zero duplication at any cost;
3. a framework that can model every theoretical device;
4. elegant generic patterns that hide actual behavior.

## Recommended Architecture Direction

## 1. Keep Registries, But Reduce Runtime Owners

The app does not need fewer registries. It needs fewer owners.

Recommended core owners:

### `AppRuntimeCoordinator`

One service that owns startup/runtime bootstrap:

- initialize source selection;
- initialize audio managers;
- initialize button input;
- start Bluetooth enumerators;
- start device auto-reconnect flows;
- wire long-lived cross-service listeners.

This removes operational startup logic from `MainPage.Loaded`.

### `SessionCoordinator`

One service that owns:

- sleep / wake-word / active-session mode;
- orchestrator start and stop;
- output mode and listening-mode transitions;
- interruption and session-state transitions.

`MainViewModel` should present this state, not be the real owner of it.

### `SourceProfileManager`

One service that becomes the real owner of source selection:

- app startup restore;
- profile apply;
- device connect/disconnect fallback;
- persistence of selected profile and current active slots.

This is the existing best candidate. It does not need replacement, just clearer
ownership and earlier startup participation.

## 2. Organize The Product Around Assistive Workflows

Instead of thinking mainly in terms of devices, think in terms of user-facing
assistive workflows:

```text
Observe      -> look, read, scan, find, scene watch
Converse     -> ask, explain, remember, deep analysis
Act          -> call, message, navigate
Capture      -> photo, video, voice note
Setup        -> choose sources, AI provider, command defaults
```

This does not require a huge new framework.

It mostly means:

- the main entry points should map to these workflows;
- devices and AI providers should stay behind those workflows;
- UI should call workflows, not stitch provider logic together itself.

The current camera-command path is already a good example of this.

## 3. Treat Devices As Capability Providers, Not As The Center Of The App

The app should keep simple provider abstractions, but the mental model should
be:

```text
assistive workflow
  -> asks for camera/audio/button/output capability
  -> source/profile system decides which provider is active
  -> provider manager talks to concrete device
```

That means:

- external devices remain pluggable;
- the user gets one consistent behavior model;
- the app stays blind-first instead of gadget-first.

## 4. Split Settings Into Clear Stores

Recommended split:

- `IAppPreferencesStore`
  For general UX, mode, and feature defaults.

- `IAiProviderSettingsStore`
  For provider choice, instance data, endpoint settings, and model selection.

- `IDeviceSettingsStore`
  For source profiles, active slots, known devices, and device overrides.

- `IDiagnosticsSettingsStore`
  For logging, telemetry, and debug flags.

This does not require deleting `SettingsService` immediately.

A practical path is:

- keep `SettingsService` as a compatibility facade for a while;
- introduce narrower store interfaces first;
- migrate consumers gradually.

That is much more KISS-friendly than trying to replace everything in one move.

## 5. Finish The Unification Of Action Paths

Recommended rule:

- camera-based user actions should go through `ICameraCommandService`;
- tools should be thin wrappers over workflows or commands;
- wake word and button actions should trigger the same workflows as UI actions;
- view models should not own duplicate action logic where a workflow already
  exists.

That gives the app one behavior per intent, even if there are multiple
invocation channels.

## 6. Keep OOP Concrete And Boring

Good-enough OOP for this app means:

- interfaces at hardware/provider seams;
- concrete coordinators for runtime ownership;
- small registries for discovery and lookup;
- simple records/models for persisted settings;
- event-driven wiring only where there is a real asynchronous device lifecycle.

It does not mean:

- abstract factories everywhere;
- deep class hierarchies;
- generic command buses for everything;
- DDD/CQRS/event-sourcing layers;
- a plugin marketplace architecture inside the app.

## Recommended Target Shape

```text
Pages / ViewModels
  -> AppRuntimeCoordinator
  -> SessionCoordinator
  -> Assistive workflows / command services

Assistive workflows
  -> CameraCommandService
  -> ToolDispatcher
  -> Conversation / vision / barcode services

Source selection
  -> SourceProfileManager
  -> CameraManager / AudioInputManager / AudioOutputManager / ButtonInputManager

Device layer
  -> ICameraProvider / IAudioInputProvider / IAudioOutputProvider / IButtonInputProvider
  -> concrete phone / glasses / USB / Bluetooth / WiFi providers

AI layer
  -> IAiProviderRegistry
  -> IAiProviderInstanceStore
  -> provider adapters

Settings layer
  -> app preferences
  -> AI provider settings
  -> device settings
  -> diagnostics settings
```

This is still simple. It is just clearer.

## Concrete Improvement Proposal

## Phase 1 - Runtime Ownership Cleanup

Create an `AppRuntimeCoordinator` and move runtime bootstrap into it.

Move out of `MainPage.Loaded`:

- manager initialization;
- Bluetooth enumerator startup;
- long-lived router wiring;
- device auto-reconnect trigger.

Keep in `MainPage`:

- page-specific UI state;
- preview control handoff to the phone camera provider;
- visual behavior.

Expected result:

- easier startup reasoning;
- fewer hidden page lifecycle side effects;
- simpler testing of app startup behavior.

## Phase 2 - Make `SourceProfileManager` The Real Source Owner

Do this first because it has the clearest user value.

Changes:

- call `SourceProfileManager.InitializeAsync()` during startup;
- make it the primary owner of source-selection policy;
- keep managers as slot managers, not policy owners;
- reduce independent auto-switching decisions in other places.

Expected result:

- what the user selects is what the app actually uses;
- fewer profile/runtime drifts;
- more predictable assistive behavior.

## Phase 3 - Split Settings By Concern

Introduce narrower settings stores while keeping `SettingsService` as a bridge.

Start with:

- device settings;
- AI provider settings;
- app UX/preferences.

Expected result:

- smaller dependency surfaces;
- easier migration and validation;
- less temptation to keep adding random settings to one giant interface.

## Phase 4 - Unify Workflow Entry Points

Standardize how assistive actions enter the system.

Recommended shape:

- `Look`, `Read`, `Scan`, and similar vision actions go through
  `ICameraCommandService`;
- tools remain wrappers;
- wake word and buttons trigger the same command or workflow service;
- duplicate legacy action code in `MainViewModel` gets retired where a shared
  workflow already exists.

Expected result:

- fewer behavior mismatches;
- easier testing;
- better transcript consistency.

## Phase 5 - Clarify Device Capability Boundaries

After the ownership cleanup is done, review whether any remaining device logic
should move behind clearer capability services.

Examples:

- video recording may deserve its own provider/service boundary;
- device reconnect policy may deserve a small dedicated service;
- some device-specific diagnostic/probe code may belong in test/hardware paths
  rather than in product runtime surfaces.

This phase should be small and selective, not architectural theater.

## What I Would Not Do

I would not:

- replace all current managers with a new grand device framework;
- introduce a mediator bus for every interaction;
- merge every tool and command into one hyper-generic action system right away;
- build a plugin loader beyond DI registrations and small registries;
- split the app into many tiny layers just because the codebase is growing.

That would overshoot the problem.

## Recommended Milestone Shape

If this report turns into implementation work, I would frame the next real
architecture milestone around:

```text
M49 - Runtime Ownership And Source Selection Cleanup
```

Suggested scope:

- add `AppRuntimeCoordinator`;
- initialize `SourceProfileManager` at startup;
- move runtime bootstrap out of `MainPage`;
- make `DeviceSettings` the authoritative source-selection persistence model;
- trim duplicate source-switching logic.

Then a follow-up milestone could handle settings-store split and workflow
consolidation.

## Final Recommendation

BodyCam does not need a dramatic new architecture.

It already has the right building blocks for a product-phase app:

- provider interfaces;
- registries;
- workflow-oriented commands;
- AI provider capability metadata;
- device profile concepts.

The right move now is to simplify ownership and make the app more clearly about
assistive outcomes for blind users.

So the improvement proposal is:

- keep the registries;
- keep the simple managers;
- elevate one runtime bootstrap owner;
- elevate one source-selection owner;
- split settings by concern;
- organize new work around assistive workflows rather than device plumbing.
