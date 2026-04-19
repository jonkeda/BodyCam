# M23 — First Start & Setup

**Status:** NOT STARTED  
**Goal:** Guide the user through initial setup on first launch — request permissions, configure API keys, and verify connectivity before entering the main UI.

**Depends on:** None (all permission and settings infrastructure already exists).

---

## Why This Matters

Right now the app launches straight into MainPage. On Android this causes:
- **Crashes** — `BLUETOOTH_CONNECT` (Android 12+), `CAMERA`, and `RECORD_AUDIO` are runtime permissions. Calling BT or camera APIs without them throws `SecurityException`.
- **Confusion** — no API key is configured on a fresh install, so every AI feature silently fails or throws `InvalidOperationException("API key not configured")`.
- **No guidance** — the user has to discover Settings on their own.

A first-start flow solves all of this in one place.

---

## Phases

### Phase 1 — Permission Request Flow

**What:** A setup page that requests all required runtime permissions in sequence before the main UI loads.

| Permission | Android API | When Needed |
|---|---|---|
| `RECORD_AUDIO` | All | Microphone / voice input |
| `CAMERA` | All | Vision features |
| `BLUETOOTH_CONNECT` | 31+ (Android 12) | BT audio devices |
| `ACCESS_FINE_LOCATION` | 31+ (if BT scan) | BT device discovery (some OEMs) |

Tasks:
- [ ] Create `SetupPage.xaml` with a step-by-step permission flow
- [ ] Request permissions one at a time with explanation text ("BodyCam needs microphone access for voice conversation")
- [ ] Show granted/denied status per permission with icons
- [ ] Handle "Don't ask again" — show a button to open app settings
- [ ] Skip permissions already granted (re-launch scenario)
- [ ] On Windows, skip the permission page entirely (not needed)
- [ ] Guard `MainPage.Loaded` BT/camera code — don't call if permission was denied

### Phase 2 — API Key Configuration

**What:** After permissions, prompt the user to enter their OpenAI or Azure OpenAI API key.

Tasks:
- [ ] Add an API key entry step to the setup flow
- [ ] Provider picker (OpenAI / Azure) with the right fields per provider
- [ ] Validate the key by making a lightweight API call (e.g. list models or a tiny chat completion)
- [ ] Show success/error feedback
- [ ] Allow "Skip" with a warning that AI features won't work
- [ ] If key is already configured (returning user), skip this step

### Phase 3 — Connectivity Check

**What:** Verify the device can reach the OpenAI/Azure endpoint.

Tasks:
- [ ] Ping the API endpoint after key validation
- [ ] Show network status (connected / no internet / API unreachable)
- [ ] If offline, allow proceeding with a warning

### Phase 4 — First-Start State Management

**What:** Track whether setup has been completed so it only shows once.

Tasks:
- [ ] Add `SetupCompleted` bool to `ISettingsService` (persisted via `Preferences`)
- [ ] `App.xaml.cs` checks `SetupCompleted` — routes to `SetupPage` or `AppShell`
- [ ] "Reset setup" option in Settings for re-running the flow
- [ ] On app update, re-run only if new permissions were added (version check)

### Phase 5 — Welcome & Onboarding (Optional)

**What:** A brief intro explaining what BodyCam does before the permissions flow.

Tasks:
- [ ] 2-3 swipeable cards: "Voice assistant", "Camera vision", "Smart tools"
- [ ] Keep it minimal — under 10 seconds to get through
- [ ] Skip button always visible

---

## Design Principles

1. **One permission at a time** — don't pop 4 dialogs at once. Explain why each is needed, then request.
2. **Graceful degradation** — if a permission is denied, disable that feature, don't crash. BT denied → no BT audio. Camera denied → no vision.
3. **No blocking on API key** — the app should be explorable without a key. AI features show "Configure your API key in Settings" instead of throwing.
4. **Idempotent** — running setup again doesn't break anything. Already-granted permissions are shown as ✓.
5. **Platform-aware** — skip the entire flow on Windows where runtime permissions aren't needed.

---

## Current State

| Piece | Status |
|---|---|
| Runtime permissions | Declared in manifest, not requested at runtime → crash |
| API key entry | Only in Settings page, no first-run prompt |
| Connectivity check | None |
| Setup tracking | No `SetupCompleted` flag |
| Permission guards | `MainPage.Loaded` calls BT APIs without checking grants |
| `AppChatClient` | Already handles missing key gracefully (throws at use, not startup) |
