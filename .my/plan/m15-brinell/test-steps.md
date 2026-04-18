# M15 — Test Steps

Step-by-step execution procedures for every test case in [test-cases.md](test-cases.md).
Each step describes the exact setup, trigger, and assertion sequence.

---

## Prerequisites

All tests use the `BodyCamTestHost` from Phase 2. Unless noted, every test starts with:

```csharp
await using var host = BodyCamTestHost.Create();
await host.InitializeAsync();
```

Providers are accessed via `host.Mic`, `host.Speaker`, `host.Camera`, `host.Buttons`.
Managers via `host.AudioInput`, `host.AudioOutput`, `host.CameraManager`, `host.ButtonInput`.

---

## 1. describe_scene

### DS-BTN-1 — SingleTap triggers describe_scene (session active)

```
Setup:
  1. Create host with TestCameraProvider loaded: office-desk.jpg
  2. Start session via orchestrator (host.Orchestrator.StartAsync)
  3. Register ActionTriggered handler on ButtonInputManager

Trigger:
  4. host.Buttons.SimulateGesture(ButtonGesture.SingleTap)

Assert:
  5. ActionTriggered event fired with ButtonAction.Look
  6. host.Camera.FramesCaptured >= 1
  7. host.Speaker.WasAudioPlayed == true  (AI responded with audio)
```

### DS-BTN-2 — Look button via XAML (UI test)

```
Setup:
  1. Launch app with BODYCAM_TEST_MODE=1
  2. BodyCamFixture navigates to MainPage
  3. Session active

Trigger:
  4. fixture.MainPage.ClickLookButton()

Assert:
  5. fixture.TestProviders.Camera.FramesCaptured >= 1
  6. fixture.TestProviders.Speaker.WasAudioPlayed == true
```

### DS-BTN-3 — SingleTap, session NOT active (direct VisionAgent)

```
Setup:
  1. Create host with TestCameraProvider loaded: office-desk.jpg
  2. Do NOT start session (orchestrator idle)

Trigger:
  3. host.Buttons.SimulateGesture(ButtonGesture.SingleTap)

Assert:
  4. host.Camera.FramesCaptured >= 1
  5. Transcript updated (VisionAgent called directly, no audio response)
  6. host.Speaker.WasAudioPlayed == false  (no Realtime session for audio)
```

### DS-WW-1 — "bodycam-look" wake word

```
Setup:
  1. Create host with TestMicProvider loaded: wake-word-bodycam-look.pcm
  2. TestCameraProvider loaded with office-desk.jpg
  3. Start mic coordinator (host.MicCoordinator.StartAsync)

Trigger:
  4. host.Mic.StartAsync()  (emits wake word PCM chunks)
  5. Wait for wake word detection (WakeWordService fires)

Assert:
  6. QuickAction mode activated
  7. host.Camera.FramesCaptured >= 1
  8. host.Speaker.WasAudioPlayed == true
```

### DS-LLM-1 — "What do you see?" via Realtime API

```
Setup:
  1. Create host with TestCameraProvider loaded: office-desk.jpg
  2. Start session (host.Orchestrator.StartAsync)
  3. TestMicProvider loaded with speech-whats-this.pcm

Trigger:
  4. host.Mic feeds speech audio → Realtime API
  5. LLM issues function_call: describe_scene

Assert:
  6. host.Camera.FramesCaptured >= 1
  7. Tool result returned to Realtime API
  8. host.Speaker.WasAudioPlayed == true  (AI speaks description)
```

### DS-LLM-2 — "Describe what's on my left" (query parameter)

```
Setup:
  1. Create host with TestCameraProvider loaded
  2. Session active

Trigger:
  3. LLM calls describe_scene with query="what's on my left"

Assert:
  4. Tool receives query parameter
  5. host.Camera.FramesCaptured >= 1
  6. Result contains focused description
```

### DS-LLM-3 — Camera unavailable

```
Setup:
  1. Create host
  2. host.Camera.SimulateDisconnect()  (marks unavailable)
  3. Session active

Trigger:
  4. LLM calls describe_scene

Assert:
  5. Tool returns error result (IsSuccess == false)
  6. AI reports "I can't see anything right now"
  7. host.Camera.FramesCaptured == 0
```

### DS-LLM-4 — Cooldown (5s)

```
Setup:
  1. Create host with TestCameraProvider loaded
  2. Session active

Trigger:
  3. LLM calls describe_scene (first call)
  4. Immediately call describe_scene again (second call, within 5s)

Assert:
  5. First call succeeds (IsSuccess == true)
  6. Second call returns cooldown message
  7. host.Camera.FramesCaptured == 1  (only first call captured)
```

---

## 2. read_text

### RT-BTN-1 — Read button via XAML

```
Setup:
  1. Create host with TestCameraProvider loaded: text-sign.jpg
  2. Session active

Trigger:
  3. Simulate Read button press (ButtonAction.Read via gesture or XAML)

Assert:
  4. host.Camera.FramesCaptured >= 1
  5. host.Speaker.WasAudioPlayed == true
```

### RT-WW-1 — "bodycam-read" wake word

```
Setup:
  1. Create host with TestMicProvider loaded: wake-word-bodycam-read.pcm
  2. TestCameraProvider loaded with text-sign.jpg
  3. Start mic coordinator

Trigger:
  4. host.Mic.StartAsync()
  5. Wait for wake word detection

Assert:
  6. QuickAction mode activated
  7. host.Camera.FramesCaptured >= 1
  8. host.Speaker.WasAudioPlayed == true
```

### RT-LLM-1 — "Read the sign"

```
Setup:
  1. Create host with TestCameraProvider loaded: text-sign.jpg
  2. Session active

Trigger:
  3. LLM calls read_text

Assert:
  4. host.Camera.FramesCaptured >= 1
  5. Tool result contains extracted text
  6. host.Speaker.WasAudioPlayed == true
```

### RT-LLM-2 — "Read the menu" (focus parameter)

```
Setup:
  1. Create host with TestCameraProvider loaded: menu-board.jpg
  2. Session active

Trigger:
  3. LLM calls read_text with focus="menu"

Assert:
  4. Tool receives focus parameter
  5. Result focuses on menu content
```

### RT-LLM-3 — No text visible

```
Setup:
  1. Create host with TestCameraProvider loaded: empty-room.jpg
  2. Session active

Trigger:
  3. LLM calls read_text

Assert:
  4. host.Camera.FramesCaptured >= 1
  5. Tool result indicates no readable text found
```

---

## 3. take_photo

### TP-BTN-1 — DoubleTap triggers take_photo

```
Setup:
  1. Create host with TestCameraProvider loaded
  2. Session active

Trigger:
  3. host.Buttons.SimulateGesture(ButtonGesture.DoubleTap)

Assert:
  4. host.Camera.FramesCaptured >= 1
  5. File saved to AppData/photos/ directory
  6. AI confirms "Photo saved"
```

### TP-BTN-2 — Photo button via XAML (UI test)

```
Setup:
  1. Launch app with BODYCAM_TEST_MODE=1
  2. Navigate to MainPage, session active

Trigger:
  3. fixture.MainPage.ClickPhotoButton()

Assert:
  4. fixture.TestProviders.Camera.FramesCaptured >= 1
  5. Photo file exists
```

### TP-LLM-1 — "Take a photo"

```
Setup:
  1. Create host with TestCameraProvider loaded
  2. Session active

Trigger:
  3. LLM calls take_photo

Assert:
  4. host.Camera.FramesCaptured >= 1
  5. File saved to photos directory
  6. Tool returns success with file path
```

### TP-LLM-2 — "Take a photo of the whiteboard" (description metadata)

```
Setup:
  1. Create host with TestCameraProvider loaded
  2. Session active

Trigger:
  3. LLM calls take_photo with description="whiteboard"

Assert:
  4. File saved with description metadata
  5. Tool returns success
```

### TP-LLM-3 — Camera unavailable

```
Setup:
  1. Create host
  2. host.Camera.SimulateDisconnect()
  3. Session active

Trigger:
  4. LLM calls take_photo

Assert:
  5. Tool returns error (IsSuccess == false)
  6. host.Camera.FramesCaptured == 0
```

---

## 4. save_memory

### SM-WW-1 — "bodycam-remember" wake word

```
Setup:
  1. Create host with TestMicProvider loaded: wake-word-bodycam-remember.pcm + speech
  2. Session sleeping

Trigger:
  3. host.Mic.StartAsync()
  4. Wake word detected → FullSession mode starts
  5. LLM captures context from speech → calls save_memory

Assert:
  6. Memory persisted to memories.json
  7. host.Speaker.WasAudioPlayed == true  (AI confirms)
```

### SM-LLM-1 — "Remember my car is in spot B7"

```
Setup:
  1. Create host
  2. Session active
  3. MemoryStore initialized with temp file

Trigger:
  4. LLM calls save_memory(content="Car parked in spot B7", category="location")

Assert:
  5. MemoryStore contains entry with content "Car parked in spot B7"
  6. Entry has category "location"
  7. Tool returns success confirmation
```

### SM-LLM-2 — "Remember this is Alice"

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls save_memory(content="This person is Alice", category="person")

Assert:
  3. MemoryStore contains entry with category "person"
  4. Tool returns success
```

### SM-LLM-3 — Category inference

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls save_memory(content="Cold brew is $5", category="item")

Assert:
  3. MemoryStore entry has category "item"
  4. Verify category was correctly inferred by LLM (Real API test only)
```

---

## 5. recall_memory

### RM-LLM-1 — "Where did I park?"

```
Setup:
  1. Create host, session active
  2. Pre-populate MemoryStore with: {content: "Car parked in spot B7", category: "location"}

Trigger:
  3. LLM calls recall_memory(query="park")

Assert:
  4. Tool returns matching entry containing "B7"
  5. AI speaks "Your car is in spot B7"
```

### RM-LLM-2 — "What do I know about Alice?"

```
Setup:
  1. Create host, session active
  2. Pre-populate MemoryStore with: {content: "This person is Alice", category: "person"}

Trigger:
  3. LLM calls recall_memory(query="Alice")

Assert:
  4. Tool returns matching entry
  5. Result contains "Alice"
```

### RM-LLM-3 — No matching memory

```
Setup:
  1. Create host, session active
  2. MemoryStore empty (or no matching entries)

Trigger:
  3. LLM calls recall_memory(query="quantum physics")

Assert:
  4. Tool returns success with empty/no-match indicator
  5. AI reports "I don't have any memories about that"
```

---

## 6. find_object

### FO-BTN-1 — Find button via XAML

```
Setup:
  1. Create host with TestCameraProvider loaded with multiple frames
  2. Session active

Trigger:
  3. Simulate Find button press

Assert:
  4. host.Camera.FramesCaptured >= 1
  5. AI describes objects found
```

### FO-WW-1 — "bodycam-find" wake word

```
Setup:
  1. Create host with TestMicProvider: wake-word-bodycam-find.pcm
  2. TestCameraProvider loaded with frames
  3. Session sleeping

Trigger:
  4. Wake word detected → FullSession starts
  5. LLM asks "what to find?" → user speaks object name → tool runs

Assert:
  6. Session activated
  7. host.Camera.FramesCaptured >= 1
```

### FO-LLM-1 — "Find my red mug" (found on 3rd frame)

```
Setup:
  1. Create host
  2. TestCameraProvider loaded with 3+ frames:
     - Frame 1: empty-room.jpg (no mug)
     - Frame 2: empty-room.jpg (no mug)
     - Frame 3: office-desk.jpg (mug visible)
  3. Session active

Trigger:
  4. LLM calls find_object(target="red mug")
  5. Tool polls camera every 3s

Assert:
  6. host.Camera.FramesCaptured >= 3
  7. Tool returns success on 3rd frame
  8. Result contains location description of the mug
```

### FO-LLM-2 — Object not found (timeout)

```
Setup:
  1. Create host
  2. TestCameraProvider loaded with empty-room.jpg (cycles same frame)
  3. Session active

Trigger:
  4. LLM calls find_object(target="red mug")
  5. Tool polls for 30s max

Assert:
  6. host.Camera.FramesCaptured >= 10  (30s / 3s interval)
  7. Tool returns timeout/not-found result
  8. AI reports "I couldn't find it"
```

### FO-LLM-3 — Custom scan settings

```
Setup:
  1. Create host
  2. Configure find_object settings: interval=1s, timeout=10s
  3. TestCameraProvider loaded with empty-room.jpg
  4. Session active

Trigger:
  5. LLM calls find_object(target="keys")

Assert:
  6. host.Camera.FramesCaptured >= 10  (10s / 1s interval)
  7. Total elapsed time ~10s (not 30s)
```

---

## 7. navigate_to

### NT-WW-1 — "bodycam-navigate" wake word

```
Setup:
  1. Create host with TestMicProvider: wake-word-bodycam-navigate.pcm
  2. Session sleeping

Trigger:
  3. Wake word detected → FullSession starts
  4. LLM asks destination → user speaks → navigate_to called

Assert:
  5. Session activated
  6. Tool returns navigation URI
```

### NT-LLM-1 — "Navigate to Starbucks"

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls navigate_to(destination="Starbucks")

Assert:
  3. Tool returns success
  4. Result contains Google Maps URI with destination=Starbucks
```

### NT-LLM-2 — "Walk to the train station" (walking mode)

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls navigate_to(destination="train station", mode="walking")

Assert:
  3. Tool returns success
  4. URI contains travelmode=walking
```

### NT-LLM-3 — "Drive to the airport" (driving mode)

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls navigate_to(destination="airport", mode="driving")

Assert:
  3. Tool returns success
  4. URI contains travelmode=driving
```

---

## 8. start_scene_watch

### SW-LLM-1 — "Tell me when the bus arrives"

```
Setup:
  1. Create host with TestCameraProvider loaded with multiple frames
  2. Session active

Trigger:
  3. LLM calls start_scene_watch(condition="bus arriving")

Assert:
  4. Background polling starts
  5. Tool returns success (watch started)
  6. host.Camera.FramesCaptured increments over time
```

### SW-LLM-2 — Condition detected

```
Setup:
  1. Create host
  2. TestCameraProvider frames: [empty-street, empty-street, empty-street, bus-arriving]
  3. Session active

Trigger:
  4. LLM calls start_scene_watch(condition="bus arriving")
  5. Polling processes frames 1-3 (no match), frame 4 (match)

Assert:
  6. AI notifies user "The bus has arrived"
  7. host.Speaker.WasAudioPlayed == true
  8. Polling stops after detection
```

### SW-LLM-3 — Custom interval

```
Setup:
  1. Create host with TestCameraProvider
  2. Session active

Trigger:
  3. LLM calls start_scene_watch(condition="light turns green", intervalSeconds=5)

Assert:
  4. Polling interval is 5s (not default 3s)
  5. After 15s: host.Camera.FramesCaptured == 3  (15s / 5s)
```

---

## 9. make_phone_call

### PC-WW-1 — "bodycam-call" wake word

```
Setup:
  1. Create host with TestMicProvider: wake-word-bodycam-call.pcm
  2. Session sleeping

Trigger:
  3. Wake word detected → FullSession starts
  4. LLM asks who to call → user speaks name → make_phone_call invoked

Assert:
  5. Session activated
  6. Tool returns success (PhoneDialer.Open called)
```

### PC-LLM-1 — "Call Mom"

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls make_phone_call(contact="Mom")

Assert:
  3. PhoneDialer.Open invoked with contact lookup
  4. Tool returns success
  5. AI confirms "Calling Mom"
```

### PC-LLM-2 — Platform not supported (Windows)

```
Setup:
  1. Create host on Windows (no PhoneDialer)
  2. Session active

Trigger:
  3. LLM calls make_phone_call(contact="Mom")

Assert:
  4. Tool returns error (platform not supported)
  5. AI reports "Phone calls aren't available on this device"
```

---

## 10. send_message

### MSG-LLM-1 — "Text Alice I'm on my way" (SMS)

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls send_message(recipient="Alice", message="I'm on my way", app="sms")

Assert:
  3. SMS compose URI generated
  4. Tool returns success
```

### MSG-LLM-2 — "WhatsApp Bob that I'll be late"

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls send_message(recipient="Bob", message="I'll be late", app="whatsapp")

Assert:
  3. WhatsApp URI generated
  4. Tool returns success
```

---

## 11. lookup_address

### LA-LLM-1 — "What's the address of the nearest hospital?"

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls lookup_address(query="nearest hospital")

Assert:
  3. Tool returns address info
  4. Result contains structured address data
```

### LA-LLM-2 — Chain: lookup → navigate

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls lookup_address(query="nearest hospital") → gets address
  3. LLM chains navigate_to(destination=<resolved address>)

Assert:
  4. Both tools return success
  5. Navigation URI uses the resolved address from step 2
```

---

## 12. deep_analysis

### DA-LLM-1 — "Analyze this situation in detail"

```
Setup:
  1. Create host with TestCameraProvider loaded
  2. Session active

Trigger:
  3. LLM calls deep_analysis(query="analyze this situation")

Assert:
  4. ConversationAgent.AnalyzeAsync called
  5. Detailed multi-paragraph response returned
  6. host.Speaker.WasAudioPlayed == true
```

### DA-LLM-2 — With context ("Compare what I see now to earlier")

```
Setup:
  1. Create host with TestCameraProvider loaded
  2. Session active, previous describe_scene result in context

Trigger:
  3. LLM calls deep_analysis(query="compare", context="previous description")

Assert:
  4. Reasoning model receives both current frame and context
  5. Response compares current vs previous
```

---

## 13. set_translation_mode

### TM-WW-1 — "bodycam-translate" wake word

```
Setup:
  1. Create host with TestMicProvider: wake-word-bodycam-translate.pcm
  2. Session sleeping

Trigger:
  3. Wake word detected → FullSession starts
  4. LLM asks target language → user speaks → set_translation_mode called

Assert:
  5. Session activated
  6. Translation mode enabled
```

### TM-LLM-1 — "Translate everything to Spanish"

```
Setup:
  1. Create host, session active

Trigger:
  2. LLM calls set_translation_mode(targetLanguage="Spanish", active=true)

Assert:
  3. System prompt updated with translation instructions
  4. Tool returns success
  5. Subsequent AI responses should be in Spanish
```

### TM-LLM-2 — "Stop translating"

```
Setup:
  1. Create host, session active
  2. Translation mode already ON (Spanish)

Trigger:
  3. LLM calls set_translation_mode(active=false)

Assert:
  4. System prompt restored to default (no translation)
  5. Tool returns success
  6. Subsequent AI responses in English
```

---

## Session Control Tests

### SC-BTN-1 — LongPress starts session

```
Setup:
  1. Create host, session sleeping

Trigger:
  2. host.Buttons.SimulateGesture(ButtonGesture.LongPress)

Assert:
  3. Orchestrator session status == Active
```

### SC-BTN-2 — LongPress stops session

```
Setup:
  1. Create host, session active

Trigger:
  2. host.Buttons.SimulateGesture(ButtonGesture.LongPress)

Assert:
  3. Orchestrator session status == Sleeping
```

### SC-BTN-3 — Ask button starts session (UI test)

```
Setup:
  1. Launch app, session sleeping

Trigger:
  2. fixture.MainPage.ClickAskButton()

Assert:
  3. Session activates
```

### SC-WW-1 — "Hey BodyCam" activates session

```
Setup:
  1. Create host with TestMicProvider: wake-word-hey-bodycam.pcm
  2. Session sleeping, mic coordinator started

Trigger:
  3. host.Mic.StartAsync()

Assert:
  4. Wake word detected
  5. Session activates
```

### SC-WW-2 — "Go to sleep" deactivates session

```
Setup:
  1. Create host, session active
  2. TestMicProvider loaded with "go to sleep" audio

Trigger:
  3. Voice command processed

Assert:
  4. Session deactivates
```

### SC-BTN-4 — ToggleSleepActive (sleep → active)

```
Setup:
  1. Create host, session sleeping

Trigger:
  2. ToggleSleepActive action dispatched

Assert:
  3. Session transitions to Active
```

### SC-BTN-5 — ToggleSleepActive (active → sleep)

```
Setup:
  1. Create host, session active

Trigger:
  2. ToggleSleepActive action dispatched

Assert:
  3. Session transitions to Sleep
```

---

## Cross-Cutting / Integration Tests

### INT-1 — Multi-tool chain: find + save_memory

```
Setup:
  1. Create host with TestCameraProvider loaded: office-desk.jpg
  2. Session active, MemoryStore initialized

Trigger:
  3. LLM receives: "Find my keys and remember where they are"
  4. LLM calls find_object(target="keys") → finds them in frame
  5. LLM calls save_memory(content="Keys on the desk", category="location")

Assert:
  6. host.Camera.FramesCaptured >= 1
  7. MemoryStore contains "keys" entry
  8. Both tool results successful
```

### INT-2 — Multi-tool chain: read_text + navigate_to

```
Setup:
  1. Create host with TestCameraProvider loaded: text-sign.jpg (address on sign)
  2. Session active

Trigger:
  3. LLM receives: "Read that sign and navigate there"
  4. LLM calls read_text → extracts address
  5. LLM calls navigate_to(destination=<extracted address>)

Assert:
  6. host.Camera.FramesCaptured >= 1
  7. Navigation URI contains extracted address
```

### INT-3 — Multi-tool chain: recall_memory + navigate_to

```
Setup:
  1. Create host, session active
  2. Pre-populate MemoryStore: {content: "Car in garage level B, spot 42"}

Trigger:
  3. LLM receives: "Where did I park?" → "Navigate there"
  4. LLM calls recall_memory(query="park") → gets location
  5. LLM calls navigate_to(destination="garage level B spot 42")

Assert:
  6. Both tools return success
  7. Navigation URI references parking location
```

### INT-4 — Camera disconnect mid-describe

```
Setup:
  1. Create host with TestCameraProvider loaded
  2. Session active

Trigger:
  3. host.Camera.SimulateDisconnect()
  4. LLM calls describe_scene

Assert:
  5. CameraManager.Active changes (fallback if available)
  6. Tool returns error or uses fallback camera
  7. No crash or unhandled exception
```

### INT-5 — Mic disconnect mid-session

```
Setup:
  1. Create host with TestMicProvider
  2. Session active, mic capturing

Trigger:
  3. host.Mic.SimulateDisconnect()

Assert:
  4. AudioInputManager.Active falls back (or null)
  5. Disconnected event fired
  6. Session degrades gracefully (no crash)
  7. host.Mic.IsCapturing == false
```

### INT-6 — Gesture remapping (SingleTap → Photo)

```
Setup:
  1. Create host
  2. host.ButtonInput.ActionMap.SetAction(
       "test-buttons:main", ButtonGesture.SingleTap, ButtonAction.Photo)

Trigger:
  3. host.Buttons.SimulateGesture(ButtonGesture.SingleTap)

Assert:
  4. ActionTriggered fires with ButtonAction.Photo (not Look)
```

### INT-7 — Button tap during active LLM response

```
Setup:
  1. Create host with TestCameraProvider loaded
  2. Session active, AI currently generating audio response
  3. host.Speaker.IsPlaying == true

Trigger:
  4. host.Buttons.SimulateGesture(ButtonGesture.SingleTap)

Assert:
  5. New describe_scene queued or interrupts current
  6. No crash, no data corruption
  7. Speaker eventually receives new audio
```

### INT-8 — Wake word starts session, then button fires

```
Setup:
  1. Create host with TestMicProvider: wake-word-hey-bodycam.pcm
  2. TestCameraProvider loaded, session sleeping

Trigger:
  3. host.Mic.StartAsync() → wake word detected → session activates
  4. host.Buttons.SimulateGesture(ButtonGesture.SingleTap)

Assert:
  5. Session is active (from wake word)
  6. describe_scene runs in active session
  7. host.Camera.FramesCaptured >= 1
```

### INT-9 — Full round-trip (all audio flows)

```
Setup:
  1. Create host with TestMicProvider: wake-word-hey-bodycam.pcm + speech-whats-this.pcm
  2. TestCameraProvider loaded: office-desk.jpg
  3. Session sleeping, mic coordinator started

Trigger:
  4. host.Mic.StartAsync()
  5. Wake word detected → session starts
  6. Speech audio flows → Realtime API → function_call: describe_scene
  7. Frame captured → result → AI speaks

Assert:
  8. host.Mic.ChunksEmitted > 0
  9. host.Camera.FramesCaptured >= 1
  10. host.Speaker.WasAudioPlayed == true
  11. host.Speaker.TotalBytesPlayed > 0
  12. Full pipeline: mic → wake → session → speech → LLM → tool → camera → result → audio out
```

---

## Test Execution Tiers

### Unit Tests (mocked API, < 1s each)

```powershell
dotnet test src/BodyCam.Tests -f net10.0-windows10.0.19041.0 --filter "Category=Unit"
```

Covers: DS-BTN-1/3, DS-LLM-3/4, RT-LLM-3, TP-LLM-3, SM-LLM-1/2/3, RM-LLM-1/2/3,
FO-LLM-2/3, NT-LLM-1/2/3, TM-LLM-1/2, SC-BTN-1/2/4/5, INT-4/5/6/7

### Integration Tests (mocked API, < 5s each)

```powershell
dotnet test src/BodyCam.Tests -f net10.0-windows10.0.19041.0 --filter "Category=Integration"
```

Covers: DS-WW-1, RT-WW-1, FO-LLM-1, SW-LLM-1/2/3, INT-1/2/3/8/9

### Real API Tests (live OpenAI, 5–30s each)

```powershell
# Requires OPENAI_API_KEY env var
dotnet test src/BodyCam.RealTests -f net10.0-windows10.0.19041.0
```

Covers: DS-LLM-1/2, RT-LLM-1/2, SM-LLM-3 (category inference), DA-LLM-1/2

### UI Tests (Brinell + Appium, 10–60s each)

```powershell
# Requires app built and Appium server running
dotnet test src/BodyCam.UITests -f net10.0-windows10.0.19041.0
```

Covers: DS-BTN-2, RT-BTN-1 (via XAML), TP-BTN-2, SC-BTN-3
