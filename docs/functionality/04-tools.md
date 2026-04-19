# 04 — Tools

## Tool System

Tools are AI-callable functions. The Realtime API can invoke them during a conversation (via MAF function invocation middleware), and quick action buttons can invoke them directly from the UI.

### Interfaces

**`ITool`** — every tool implements this:
```
Name            → tool name (e.g. "describe_scene")
Description     → what it does (sent to the AI as function description)
ParameterSchema → JSON Schema for arguments
IsEnabled       → can be toggled off
WakeWord        → optional Porcupine binding for voice activation
ExecuteAsync(arguments, context, ct) → ToolResult
```

**`ToolBase<TArgs>`** — generic base that deserializes JSON arguments into typed `TArgs` record. Subclasses implement `ExecuteAsync(TArgs, ToolContext, ct)`.

**`ToolDispatcher`** — central registry. Resolves tool by name, deserializes args, executes, returns JSON result.

**`ToolContext`** — execution environment:
- `CaptureFrame` — async function to grab a JPEG from the camera
- `Session` — `SessionContext` with conversation history and vision context
- `Log` — callback that fires `DebugLog` event to UI

**`ToolResult`** — return value:
- `ToolResult.Success(data)` — serializes data to JSON
- `ToolResult.Fail(error)` — error message

### Tool Settings

Tools can implement `IToolSettings` to expose user-configurable settings in the Advanced Settings page. Each setting has a key, label, type (Boolean/Integer/Text), default value, and getter/setter.

---

## Built-In Tools

### Vision Tools

**DescribeSceneTool** (`describe_scene`)
- Captures camera frame, sends to VisionAgent
- Optional `query` parameter for specific questions
- Rate-limited: 5-second cooldown between calls, returns cached description if too soon
- Wake word: "bodycam look" → QuickAction → "Describe what you see in detail."

**ReadTextTool** (`read_text`)
- Captures frame, sends to VisionAgent with text-extraction prompt
- Optional `focus` parameter: "sign", "label", "document"
- Returns extracted text

**FindObjectTool** (`find_object`)
- Required `target` parameter (what to look for)
- Polls camera every 3 seconds for up to 30 seconds
- Returns FOUND (with description of location) or NOT_FOUND

**StartSceneWatchTool** (`start_scene_watch`)
- Required `condition` parameter (what to watch for)
- Optional `interval_seconds` (default: 5)
- Fire-and-forget background task
- Continuously polls vision until condition is detected, then notifies

**TakePhotoTool** (`take_photo`)
- Captures frame, saves as JPEG to app data directory
- Optional `description` parameter (embedded in filename or metadata)
- Returns file path

### Memory Tools

**SaveMemoryTool** (`save_memory`)
- Required `content` — what to remember
- Optional `category` — organize memories
- Persists to `MemoryStore` (JSON file)
- Wake word: "bodycam remember" → FullSession → "What would you like me to remember?"

**RecallMemoryTool** (`recall_memory`)
- Required `query` — what to search for
- Returns up to 10 matching entries from `MemoryStore`

### Communication Tools

**SendMessageTool** (`send_message`)
- Required: `recipient`, `message`, `app` ("sms" or "whatsapp")
- SMS: uses `Sms.Default` platform API
- WhatsApp: launches `whatsapp://send?phone={recipient}&text={message}`

**MakePhoneCallTool** (`make_phone_call`)
- Required: `contact` (phone number)
- Uses `PhoneDialer.Open()` platform API

### Reasoning Tools

**DeepAnalysisTool** (`deep_analysis`)
- Required: `query` — the question requiring deep analysis
- Optional: `context` — additional context
- Routes to `ConversationAgent.AnalyzeAsync()` (Chat Completions, not Realtime)
- Used when the AI decides a question needs multi-step reasoning beyond Realtime's capabilities

### Utility Tools

**LookupAddressTool** (`lookup_address`)
- Required: `query` — address to look up
- Pass-through to LLM knowledge (the tool returns a prompt, not a real lookup)

**SetTranslationModeTool** (`set_translation_mode`)
- Required: `target_language`, `active` (bool)
- Injects/removes translation instruction into the system prompt
- When active: AI translates everything the user hears into the target language

---

## Tool Execution Paths

### Path 1: AI-initiated (Realtime API function call)
```
User speaks → Realtime API decides to call a tool
  → MAF FunctionInvocation middleware intercepts
  → Finds matching AIFunction (registered via DI)
  → Executes tool
  → Sends result back to API as function_call_output
  → API incorporates result into next response
```

### Path 2: UI-initiated (Quick Action buttons)
```
User taps Look/Read/Find/Photo button
  → MainViewModel.SendVisionCommandAsync(prompt)
  → If session active: _orchestrator.SendTextInputAsync(prompt)
    → AI receives text, may call tools, responds with voice
  → If no session: VisionAgent.DescribeFrameAsync() directly
    → Result shown as text in transcript (no voice)
```

### Path 3: Wake word-initiated
```
User says "Hey BodyCam, look"
  → Porcupine detects wake word
  → AgentOrchestrator.OnWakeWordDetected()
  → If QuickAction mode: start session if needed, execute tool
  → If FullSession mode: start session, send initial prompt
```

## Wake Word Bindings

Tools can declare a `WakeWordBinding` with:
- `KeywordPath` — path to `.ppn` Porcupine model file
- `Sensitivity` — detection threshold (0.0–1.0)
- `Mode` — `QuickAction` (execute and done) or `FullSession` (start conversation)
- `InitialPrompt` — what the AI "hears" when triggered

Current bindings:
| Wake Word | Tool | Mode |
|-----------|------|------|
| "bodycam look" | DescribeSceneTool | QuickAction |
| "bodycam remember" | SaveMemoryTool | FullSession |
