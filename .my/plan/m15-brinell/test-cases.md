# M15 — Tool Test Cases

Test every tool through all three invocation paths: **Button/Gesture**, **Wake Word**,
and **LLM Voice** (Realtime API function calling). Each test uses mock providers
(TestMicProvider, TestCameraProvider, TestSpeakerProvider, TestButtonProvider) so
no real hardware or API keys are needed for UI tests.

---

## Invocation Paths

| Path | Trigger | Flow |
|------|---------|------|
| **Button** | Physical/simulated tap/gesture → GestureRecognizer → ActionMap → DispatchActionAsync | Fastest, 4 actions only (Look, Read, Find, Photo) |
| **Wake Word** | Mic audio → Porcupine → WakeWordDetected → ToolDispatcher.ExecuteAsync | 7 tools have wake words |
| **LLM Voice** | Mic audio → Realtime API → function_call → ToolDispatcher.ExecuteAsync | All 13 tools |

---

## Default Gesture → Action Mapping

| Gesture | ButtonAction | Equivalent Tool |
|---------|-------------|----------------|
| SingleTap | Look | describe_scene |
| DoubleTap | Photo | take_photo |
| LongPress | ToggleSession | (session control) |

---

## Test Cases by Tool

### 1. describe_scene

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| DS-BTN-1 | Button | SingleTap (default Look) | TestCameraProvider loaded with `office-desk.jpg`. Session active. | Frame captured → sent to Realtime API → AI describes scene → TestSpeakerProvider receives audio response |
| DS-BTN-2 | Button | Look button (XAML) | TestCameraProvider loaded. Session active. | Same as DS-BTN-1 via UI button click |
| DS-BTN-3 | Button | SingleTap, session NOT active | TestCameraProvider loaded. Session sleeping. | Frame captured → direct VisionAgent call → transcript updated (no audio) |
| DS-WW-1 | Wake Word | "bodycam-look" | TestMicProvider emits wake word PCM. TestCameraProvider loaded. | Wake word detected → QuickAction mode → frame captured → describe result spoken |
| DS-LLM-1 | LLM Voice | "What do you see?" | Session active. TestMicProvider emits speech. TestCameraProvider loaded. | LLM calls `describe_scene` → frame captured → result returned → AI speaks description |
| DS-LLM-2 | LLM Voice | "Describe what's on my left" | Session active. TestCameraProvider loaded. | LLM calls `describe_scene(query="what's on my left")` → frame captured → focused description |
| DS-LLM-3 | LLM Voice | Camera unavailable | Session active. TestCameraProvider returns null. | LLM calls `describe_scene` → tool returns error → AI says "I can't see anything right now" |
| DS-LLM-4 | LLM Voice | Cooldown (5s) | Two rapid requests. | First succeeds. Second returns cooldown message within 5s window |

### 2. read_text

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| RT-BTN-1 | Button | Read button (XAML) | TestCameraProvider loaded with `text-sign.jpg`. Session active. | Frame captured → AI reads text → TestSpeakerProvider receives audio |
| RT-WW-1 | Wake Word | "bodycam-read" | TestMicProvider emits wake word PCM. TestCameraProvider with text image. | Wake word → QuickAction → reads text → speaks result |
| RT-LLM-1 | LLM Voice | "Read the sign" | Session active. TestCameraProvider with text image. | LLM calls `read_text` → extracts text → speaks it |
| RT-LLM-2 | LLM Voice | "Read the menu" | Session active. TestCameraProvider with menu image. | LLM calls `read_text(focus="menu")` → focused extraction |
| RT-LLM-3 | LLM Voice | No text visible | TestCameraProvider with `empty-room.jpg`. | LLM calls `read_text` → AI reports no readable text found |

### 3. take_photo

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| TP-BTN-1 | Button | DoubleTap (default Photo) | TestCameraProvider loaded. Session active. | Frame captured → saved to AppData/photos/ → AI confirms "Photo saved" |
| TP-BTN-2 | Button | Photo button (XAML) | TestCameraProvider loaded. Session active. | Same as TP-BTN-1 via UI button |
| TP-LLM-1 | LLM Voice | "Take a photo" | Session active. TestCameraProvider loaded. | LLM calls `take_photo` → file saved → AI confirms |
| TP-LLM-2 | LLM Voice | "Take a photo of the whiteboard" | Session active. | LLM calls `take_photo(description="whiteboard")` → saved with description metadata |
| TP-LLM-3 | LLM Voice | Camera unavailable | TestCameraProvider returns null. | Tool returns error → AI reports failure |

### 4. save_memory

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| SM-WW-1 | Wake Word | "bodycam-remember" | TestMicProvider emits wake word + speech. Session starts (FullSession mode). | Wake word → full session → LLM captures context → calls `save_memory` → persisted to memories.json |
| SM-LLM-1 | LLM Voice | "Remember my car is in spot B7" | Session active. | LLM calls `save_memory(content="Car parked in spot B7", category="location")` → saved → AI confirms |
| SM-LLM-2 | LLM Voice | "Remember this is Alice" | Session active. | LLM calls `save_memory(content="...", category="person")` → saved |
| SM-LLM-3 | LLM Voice | Category inference | "Remember the cold brew is $5" | LLM picks `category="item"` → saved → verify correct category in JSON |

### 5. recall_memory

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| RM-LLM-1 | LLM Voice | "Where did I park?" | Session active. Memories.json has car parking entry. | LLM calls `recall_memory(query="park")` → returns B7 entry → AI speaks "Your car is in spot B7" |
| RM-LLM-2 | LLM Voice | "What do I know about Alice?" | Memories.json has person entry. | LLM calls `recall_memory(query="Alice")` → returns match |
| RM-LLM-3 | LLM Voice | No matching memory | Empty memories.json. | LLM calls `recall_memory` → no results → AI says "I don't have any memories about that" |

### 6. find_object

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| FO-BTN-1 | Button | Find button (XAML) | TestCameraProvider loaded. Session active. | Frame captured → AI describes objects found → TestSpeakerProvider receives audio |
| FO-WW-1 | Wake Word | "bodycam-find" | TestMicProvider emits wake word. | Wake word → FullSession → LLM asks "what to find?" → user says object → tool runs |
| FO-LLM-1 | LLM Voice | "Find my red mug" | Session active. TestCameraProvider cycles frames (object not in first, appears in third). | LLM calls `find_object(target="red mug")` → polls camera every 3s → finds on 3rd frame → reports location |
| FO-LLM-2 | LLM Voice | Object not found | TestCameraProvider returns empty-room frames. | Polls for 30s → timeout → AI reports "I couldn't find it" |
| FO-LLM-3 | LLM Voice | Custom scan settings | find_object settings: interval=1s, timeout=10s. | Polls every 1s for 10s max → verify timing respected |

### 7. navigate_to

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| NT-WW-1 | Wake Word | "bodycam-navigate" | TestMicProvider emits wake word. | Wake word → FullSession → LLM asks destination → user speaks → `navigate_to` called |
| NT-LLM-1 | LLM Voice | "Navigate to Starbucks" | Session active. | LLM calls `navigate_to(destination="Starbucks")` → Google Maps URI launched (or `lookup_address` first) |
| NT-LLM-2 | LLM Voice | "Walk to the train station" | Session active. | LLM calls `navigate_to(destination="train station", mode="walking")` → walking mode URI |
| NT-LLM-3 | LLM Voice | "Drive to the airport" | Session active. | `navigate_to(mode="driving")` → driving mode URI |

### 8. start_scene_watch

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| SW-LLM-1 | LLM Voice | "Tell me when the bus arrives" | Session active. TestCameraProvider cycles frames. | LLM calls `start_scene_watch(condition="bus arriving")` → background polling starts |
| SW-LLM-2 | LLM Voice | Condition detected | TestCameraProvider: frames 1-3 show empty street, frame 4 shows bus. | Polling detects condition → AI notifies user "The bus has arrived" |
| SW-LLM-3 | LLM Voice | Custom interval | "Check every 5 seconds if the light turns green" | `start_scene_watch(intervalSeconds=5)` → verify 5s polling interval |

### 9. make_phone_call

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| PC-WW-1 | Wake Word | "bodycam-call" | TestMicProvider emits wake word. | Wake word → FullSession → LLM asks who to call → user says name → `make_phone_call` invoked |
| PC-LLM-1 | LLM Voice | "Call Mom" | Session active. | LLM calls `make_phone_call(contact="Mom")` → PhoneDialer.Open invoked → AI confirms |
| PC-LLM-2 | LLM Voice | Platform not supported | Running on Windows (no PhoneDialer). | Tool returns error → AI reports "Phone calls aren't available on this device" |

### 10. send_message

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| MSG-LLM-1 | LLM Voice | "Text Alice I'm on my way" | Session active. | LLM calls `send_message(recipient="Alice", message="I'm on my way", app="sms")` → SMS composed |
| MSG-LLM-2 | LLM Voice | "WhatsApp Bob that I'll be late" | Session active. | `send_message(app="whatsapp")` → WhatsApp URI launched |

### 11. lookup_address

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| LA-LLM-1 | LLM Voice | "What's the address of the nearest hospital?" | Session active. | LLM calls `lookup_address(query="nearest hospital")` → returns address info |
| LA-LLM-2 | LLM Voice | Then navigate | Follow-up: "Take me there" | LLM chains `lookup_address` → `navigate_to` with resolved address |

### 12. deep_analysis

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| DA-LLM-1 | LLM Voice | "Analyze this situation in detail" | Session active. TestCameraProvider loaded. | LLM calls `deep_analysis(query="...")` → ConversationAgent.AnalyzeAsync → detailed response |
| DA-LLM-2 | LLM Voice | With context | "Compare what I see now to what I saw earlier" | `deep_analysis(query="compare", context="previous description")` → reasoning model response |

### 13. set_translation_mode

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| TM-WW-1 | Wake Word | "bodycam-translate" | TestMicProvider emits wake word. | Wake word → FullSession → LLM asks target language → user speaks → translation mode ON |
| TM-LLM-1 | LLM Voice | "Translate everything to Spanish" | Session active. | `set_translation_mode(targetLanguage="Spanish", active=true)` → system prompt updated → subsequent AI responses in Spanish |
| TM-LLM-2 | LLM Voice | "Stop translating" | Translation mode ON. | `set_translation_mode(active=false)` → system prompt restored → back to English |

---

## Session Control Tests (No Tool — Button Only)

| ID | Path | Trigger | Setup | Expected |
|----|------|---------|-------|----------|
| SC-BTN-1 | Button | LongPress | Session sleeping. | ToggleSession → session starts → status = "Active" |
| SC-BTN-2 | Button | LongPress | Session active. | ToggleSession → session stops → status = "Sleeping" |
| SC-BTN-3 | Button | Ask button (XAML) | Session sleeping. | ToggleSession → session starts |
| SC-WW-1 | Wake Word | "Hey BodyCam" | Session sleeping. | System wake word → session activates |
| SC-WW-2 | Wake Word | "Go to sleep" | Session active. | System wake word → session deactivates |
| SC-BTN-4 | Button | ToggleSleepActive | Sleep state. | Transitions to Active |
| SC-BTN-5 | Button | ToggleSleepActive | Active state. | Transitions to Sleep |

---

## Cross-Cutting / Integration Tests

| ID | Category | Scenario | Expected |
|----|----------|----------|----------|
| INT-1 | Multi-tool chain | "Find my keys and remember where they are" | `find_object` → result → `save_memory` chained by LLM |
| INT-2 | Multi-tool chain | "Read that sign and navigate there" | `read_text` → result → `navigate_to` chained by LLM |
| INT-3 | Multi-tool chain | "Where did I park?" → "Navigate there" | `recall_memory` → result → `navigate_to` chained |
| INT-4 | Provider fallback | Camera disconnects mid-describe | TestCameraProvider fires Disconnected → CameraManager falls back → next capture uses fallback |
| INT-5 | Provider fallback | Mic disconnects mid-session | TestMicProvider fires Disconnected → AudioInputManager handles → session degrades gracefully |
| INT-6 | Gesture remapping | Remap SingleTap → Photo | ActionMap.SetAction("keyboard:look", SingleTap, Photo) → tap fires Photo instead of Look |
| INT-7 | Concurrent inputs | Button tap during active LLM response | Audio still playing → new describe_scene queued → no crash or corruption |
| INT-8 | Wake → Button | Wake word starts session, then button fires | "Hey BodyCam" → session active → SingleTap → describe_scene runs in active session |
| INT-9 | All audio flows | Full round-trip | TestMicProvider → wake word → session → speech → LLM → tool → result → TestSpeakerProvider captures response audio |

---

## Test Infrastructure Requirements

### Mock Providers Needed

| Provider | Purpose | Key Methods |
|----------|---------|-------------|
| `TestMicProvider` | Feed pre-recorded PCM | `StartAsync()` emits chunks on timer |
| `TestSpeakerProvider` | Capture audio output | `PlayChunkAsync()` stores chunks, `WasAudioPlayed` for assertions |
| `TestCameraProvider` | Supply test JPEG frames | `CaptureFrameAsync()` cycles through loaded images |
| `TestButtonProvider` | Programmatic button presses | `SimulateClick()`, `SimulateGesture()` |

### Test Assets

| File | Format | Purpose |
|------|--------|---------|
| `silence-1s.pcm` | Raw PCM 16kHz 16-bit mono | No-op mic input |
| `wake-word-hey-bodycam.pcm` | Raw PCM | Triggers "Hey BodyCam" wake word |
| `wake-word-bodycam-look.pcm` | Raw PCM | Triggers "bodycam-look" tool wake word |
| `speech-whats-this.pcm` | Raw PCM | "What's this?" spoken audio |
| `office-desk.jpg` | JPEG | Scene with identifiable objects |
| `text-sign.jpg` | JPEG | Readable text on a sign |
| `empty-room.jpg` | JPEG | Empty room (no objects/text) |
| `menu-board.jpg` | JPEG | Restaurant menu with prices |

### Test Execution Tiers

| Tier | Providers | API | Speed | CI |
|------|-----------|-----|-------|-----|
| **Unit** | All mocked | Mocked | <1s | Yes |
| **Integration** | Test providers, real managers | Mocked | <5s | Yes |
| **Real API** | Test providers, real managers | Live OpenAI | 5-30s | No (needs keys) |
| **UI (Brinell)** | Test providers in running app | Mocked or Live | 10-60s | Windows only |
