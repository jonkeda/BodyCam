# Tools

The tool framework lets the OpenAI Realtime API invoke device-side actions via function calling. The LLM decides when to call a tool based on conversation context — no keyword matching or manual routing needed.

## Framework

### ITool

Every tool implements `ITool`:

```csharp
string Name { get; }              // Function name sent to the API
string Description { get; }       // LLM-visible description
string ParameterSchema { get; }   // JSON Schema for arguments
bool IsEnabled { get; }           // Can be toggled in settings
WakeWordBinding? WakeWord { get; } // Optional wake word shortcut

Task<ToolResult> ExecuteAsync(string? argumentsJson, ToolContext context, CancellationToken ct);
```

### ToolBase\<TArgs\>

Generic base class that handles JSON deserialization of arguments into a typed `TArgs` object. Subclasses only implement:

```csharp
abstract Task<ToolResult> ExecuteAsync(TArgs args, ToolContext context, CancellationToken ct);
```

### ToolDispatcher

Registry and router. Injected with all `ITool` implementations via DI.

- `GetToolDefinitions()` — returns enabled tools as `ToolDefinitionDto` list (sent in `session.update`)
- `ExecuteAsync(toolName, argumentsJson, context, ct)` — finds and executes the matching tool
- `BuildWakeWordEntries()` — aggregates wake word bindings from all tools

### ToolContext

Passed to every tool execution:

- `CaptureFrame` — delegate to capture a JPEG from the camera
- `Session` — current `SessionContext` (conversation history)
- `Log` — debug logging callback
- `RealtimeClient` — for tools that need to send data back to the API

### SchemaGenerator

`SchemaGenerator.Generate<T>()` reflects on the args class properties and produces a JSON Schema. Reads `[Description]` attributes and detects nullability for the `required` array.

### ToolResult

Static factory: `ToolResult.Success(data)` serializes to JSON, `ToolResult.Fail(error)` wraps an error.

## Tool Catalog

### Vision Tools

| Tool | Args | Description |
|------|------|-------------|
| `describe_scene` | `Query?` | Captures a camera frame and describes what's visible. 5-second cooldown. Wake word: "bodycam-look". |
| `read_text` | `Focus?` | Captures a frame and extracts text (OCR via vision). Wake word: "bodycam-read". |
| `find_object` | `Target` | Continuously scans camera frames looking for a target object. Configurable scan interval (3s) and timeout (30s). Annotates found frames with a red border. Wake word: "bodycam-find". |
| `take_photo` | `Description?` | Captures a frame and saves it as JPEG to app data. |
| `start_scene_watch` | `Condition`, `IntervalSeconds?` | Starts background polling — periodically captures and checks if a condition is met. |
| `deep_analysis` | `Query`, `Context?` | Routes to `ConversationAgent` for reasoning via Chat Completions. |

### Memory Tools

| Tool | Args | Description |
|------|------|-------------|
| `save_memory` | `Content`, `Category?` | Saves an entry to the JSON memory store. Wake word: "bodycam-remember". |
| `recall_memory` | `Query` | Searches saved memories by keyword. |

### Communication Tools

| Tool | Args | Description |
|------|------|-------------|
| `make_phone_call` | `Contact` | Opens the platform phone dialer. Wake word: "bodycam-call". |
| `send_message` | `Recipient`, `Message`, `App` | Sends SMS or WhatsApp message via platform intents. |

### Navigation Tools

| Tool | Args | Description |
|------|------|-------------|
| `navigate_to` | `Destination`, `Mode` | Opens Google Maps with directions. Wake word: "bodycam-navigate". |
| `lookup_address` | `Query` | Looks up an address (delegates to LLM knowledge). |

### Mode Tools

| Tool | Args | Description |
|------|------|-------------|
| `set_translation_mode` | `TargetLanguage`, `Active` | Appends/removes translation instructions in the system prompt. Wake word: "bodycam-translate". |

## Adding a New Tool

1. Create a class inheriting `ToolBase<TArgs>` in `Tools/`
2. Define an args class with `[Description]` attributes on properties
3. Implement `ExecuteAsync(args, context, ct)`
4. Optionally add a `WakeWordBinding` property
5. Optionally implement `IToolSettings` for user-configurable settings
6. Register as `ITool` singleton in `MauiProgram.cs`

The tool automatically appears in the Realtime API session and can be invoked by the LLM.
