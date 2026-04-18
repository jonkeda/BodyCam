# M14 — Button & Gesture Input Architecture

**Status:** PLANNING  
**Goal:** Unified input abstraction for physical buttons (BT glasses, phone volume keys),
gestures (shake), and keyboard shortcuts — with configurable action mapping and gesture
recognition (single tap, double tap, long press).

**Depends on:** M4 (BT glasses pairing), M11 (camera abstraction — for photo capture actions).

---

## Why This Matters

BodyCam is a hands-free AI assistant. Users wear smart glasses and need to trigger
actions **without looking at their phone**. A button tap on the glasses should start
listening, a double-tap should take a photo, a long press should end the session.

Today, all actions require touching the phone screen — tapping "Look", "Photo", "Ask"
buttons in MainPage.xaml. There's no button abstraction at all. M4's design doc mentions
`IGlassesService.ButtonPressed` and a `GlassesButtonEvent` enum, but that's too narrow.
We need a general input system that works across:

- **BT glasses buttons** — AVRCP media controls or custom GATT characteristics
- **Phone volume buttons** — quick action triggers without unlocking
- **Phone shake gesture** — accelerometer threshold detection
- **Keyboard shortcuts** — Windows development experience (F5=look, F6=photo, etc.)

All of these should funnel through a single abstraction so the ViewModel and orchestrator
don't care where the input came from.

---

## Input Sources

| Source | Platform | Latency | Notes |
|--------|----------|---------|-------|
| **BT glasses — AVRCP** | Android (MediaSession), Windows (SMTC) | <50ms | Media play/pause button events |
| **BT glasses — GATT** | Android + Windows (BLE) | <100ms | Custom button characteristic, glasses-specific |
| **BLE remotes — BTHome** | Android + Windows (BLE scan) | <100ms | Shelly BLU Remote, BLU Button1, any BTHome device |
| **Phone volume keys** | Android (AudioManager), Windows (raw input) | <30ms | Optional, can conflict with system volume |
| **Phone shake** | Android + iOS (Accelerometer) | 100-300ms | MAUI Essentials accelerometer API |
| **Keyboard shortcuts** | Windows only | <10ms | Dev-time convenience, global hotkeys |

---

## Architecture

### Core Abstraction

```
IButtonInputProvider (per source type)
  ├── AvrcpButtonProvider        ← BT glasses media button
  ├── GattButtonProvider         ← BT glasses custom GATT button
  ├── BtHomeButtonProvider       ← BLE remotes (Shelly BLU, BTHome protocol)
  ├── VolumeButtonProvider       ← Phone volume keys
  ├── ShakeGestureProvider       ← Accelerometer shake detection
  └── KeyboardShortcutProvider   ← Windows keyboard shortcuts

GestureRecognizer (tap / double-tap / long-press from raw events)
  → Converts raw button-down/button-up into semantic gestures

ButtonInputManager (aggregates all providers)
  → Receives raw button events from providers
  → Passes through GestureRecognizer
  → Also accepts PreRecognizedGesture events (bypasses GestureRecognizer)
  → Maps recognized gestures to actions via ActionMap
  → Executes the mapped action on MainViewModel / Orchestrator
```

### Data Flow

```
┌─────────────────────┐
│  Button Source       │  (glasses, volume key, shake, keyboard)
│  IButtonInputProvider│
└────────┬────────────┘
         │ RawButtonEvent (ButtonDown / ButtonUp / Click)
         ▼
┌─────────────────────┐
│  GestureRecognizer   │  Single tap / Double tap / Long press
└────────┬────────────┘
         │ ButtonGesture (SingleTap, DoubleTap, LongPress)
         ▼
┌─────────────────────┐
│  ButtonInputManager  │  Lookup action in ActionMap
└────────┬────────────┘
         │ ButtonAction enum
         ▼
┌─────────────────────┐
│  Action Executor     │  Calls MainViewModel commands
│  → SetLayerAsync     │  or orchestrator methods
│  → SendVisionCommand │
│  → CapturePhoto      │
└─────────────────────┘
```

---

## Phases

### Phase 1: Abstraction & Gesture Recognition
Define `IButtonInputProvider`, `GestureRecognizer`, `ButtonInputManager`, and the
action mapping system. Implement `KeyboardShortcutProvider` for Windows dev testing.
Wire into `MainViewModel` to execute actions.

**Deliverables:** `IButtonInputProvider`, `GestureRecognizer`, `ButtonInputManager`,
`KeyboardShortcutProvider`, action mapping settings, integration with MainViewModel.

### Phase 2: BT Glasses Buttons & BLE Remotes
Implement `AvrcpButtonProvider` using Android MediaSession and Windows SMTC.
Implement `GattButtonProvider` for glasses that expose a custom BLE button
characteristic. Implement `BtHomeButtonProvider` for BLE remotes using the
BTHome v2 protocol (Shelly BLU Remote, BLU Button1, any BTHome device).

BTHome devices emit **pre-recognized gestures** from firmware (press, double_press,
long_press), so `BtHomeButtonProvider` raises `PreRecognizedGesture` events that
bypass `GestureRecognizer` and go directly to the `ActionMap`. This avoids the
300ms single-tap delay. Add `PreRecognizedGesture` event to `IButtonInputProvider`.

Primary target: **Shelly BLU Remote Control ZB** — 4 buttons + scroll wheel,
BLE 5.0, ~2yr battery, BTHome v2 passive scanning (no pairing required).

**Deliverables:** `AvrcpButtonProvider`, `GattButtonProvider`, `BtHomeButtonProvider`,
`BtHomeParser` (BTHome v2 protocol), `BtHomeDeviceProfile` (button name mapping),
BLE scanning (Android `BluetoothLeScanner`, Windows `BluetoothLEAdvertisementWatcher`),
platform-specific media button handling, default action mapping for Shelly remote.

See [ble-remotes.md](ble-remotes.md) for full BTHome protocol details, parser
implementation, and device profiles.

### Phase 3: Phone Inputs
Implement `VolumeButtonProvider` for phone volume key interception.
Implement `ShakeGestureProvider` using MAUI Essentials accelerometer.
Add settings for enabling/disabling each provider and configuring
shake sensitivity.

**Deliverables:** `VolumeButtonProvider`, `ShakeGestureProvider`, per-provider
enable/disable settings, shake threshold configuration.

### Phase 4: Settings & Customization
Full settings UI for button mapping. Users can choose what each gesture does
for each input source. Presets for common configurations (e.g., "Hands-free
photographer" maps double-tap to photo).

**Deliverables:** Button mapping settings page, preset configurations,
per-source gesture-to-action mapping.

### Phase 5: iOS Platform Support
Implement iOS-specific button input providers. `ShakeGestureProvider` already uses
MAUI Essentials accelerometer (cross-platform). Add `VolumeButtonProvider` for iOS
using `AVAudioSession` volume change notifications (`outputVolumeDidChange`). No
keyboard shortcut provider on iOS (Mac Catalyst only if needed later).

**Deliverables:** iOS `VolumeButtonProvider` (AVAudioSession volume observation),
verify `ShakeGestureProvider` works on iOS, platform-specific DI registration.

---

## Exit Criteria

- [ ] `IButtonInputProvider` interface defined with at least 3 implementations
- [ ] `GestureRecognizer` correctly distinguishes single tap, double tap, and long press
- [ ] `ButtonInputManager` aggregates events from multiple active providers
- [ ] Action mapping is configurable via settings
- [ ] BT glasses media button triggers actions on Android and Windows
- [ ] Keyboard shortcuts work on Windows for development
- [ ] Existing touch-based UI buttons continue to work unchanged

---

## Documents

| Document | Purpose |
|----------|---------|
| [overview.md](overview.md) | This file — scope, phases, exit criteria |
| [button-abstraction.md](button-abstraction.md) | IButtonInputProvider, GestureRecognizer, ButtonInputManager, action mapping |
| [bt-buttons.md](bt-buttons.md) | BT glasses buttons — AVRCP media controls, custom GATT characteristics |
| [ble-remotes.md](ble-remotes.md) | BLE remote controls — Shelly BLU Remote Control ZB, BTHome protocol, device profiles |
| [phone-buttons.md](phone-buttons.md) | Phone volume keys, shake gesture, keyboard shortcuts |
