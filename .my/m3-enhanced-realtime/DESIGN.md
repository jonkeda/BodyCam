# M3 Design: Enhanced Realtime — Single Pipeline with Function Calling

## Summary

Remove Mode B (Separated pipeline). Keep Mode A (Realtime) as the sole conversation mode. Use the Realtime API's native **function calling** and **image input** capabilities to achieve everything Mode B was meant to provide — and more — without the latency penalty.

## Why This Is Better

| Concern | Mode B (Removed) | Enhanced Mode A |
|---------|-------------------|-----------------|
| Voice latency | 3-7 seconds | < 1 second |
| Vision | Stub; planned via separate model call | Native `input_image` content parts |
| Deep reasoning | Chat Completions with gpt-5.4 | Function call → Chat Completions (on demand) |
| Long-form output | Chat Completions streaming | Function call → out-of-band response |
| Tool extensibility | None | Native function calling + future MCP |
| Race conditions | Dual-response races, context pollution | None — single response pipeline |
| Code complexity | Dual code paths everywhere | Single path |

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Realtime API Session                  │
│                                                         │
│  modalities: ["text", "audio"]                          │
│  voice: "marin"                                         │
│  turn_detection: { type: "semantic_vad" }               │
│  tools: [describe_scene, deep_analysis, ...]            │
│  tool_choice: "auto"                                    │
│                                                         │
│  ┌─────────┐     ┌──────────┐     ┌─────────────────┐  │
│  │ STT     │────▶│ Reasoning│────▶│ TTS / Tool Call │  │
│  │ (input) │     │ (model)  │     │ (output)        │  │
│  └─────────┘     └──────────┘     └─────────────────┘  │
└──────────┬──────────────────────────────────┬───────────┘
           │                                  │
    Audio in/out                        Function calls
           │                                  │
┌──────────▼──────────────────────────────────▼───────────┐
│                   AgentOrchestrator                      │
│                                                         │
│  • Pipes mic → Realtime API → speaker                   │
│  • Handles function_call events:                        │
│    ├─ describe_scene → VisionAgent                      │
│    ├─ deep_analysis → ConversationAgent (Chat API)      │
│    └─ (future tools)                                    │
│  • Sends function_call_output + response.create         │
│  • Manages interruption (truncation)                    │
│  • Injects periodic camera frames (optional)            │
└─────────────────────────────────────────────────────────┘
```

## Realtime API Capabilities We Leverage

### 1. Function Calling (Native)

The Realtime API supports `tools` in `session.update`. When the model decides to call a function:

1. Server emits `response.function_call_arguments.delta` (streaming args)
2. Server emits `response.function_call_arguments.done` (complete args)
3. Server emits `response.done` with output item `type: "function_call"`
4. Client executes the function
5. Client sends `conversation.item.create` with `type: "function_call_output"` and matching `call_id`
6. Client sends `response.create` to let the model continue

**Session config with tools:**
```json
{
  "type": "session.update",
  "session": {
    "modalities": ["text", "audio"],
    "voice": "marin",
    "instructions": "You are a helpful AI assistant...",
    "tools": [
      {
        "type": "function",
        "name": "describe_scene",
        "description": "Capture what the camera currently sees and describe it. Use when the user asks about their surroundings, asks you to look at something, or when visual context would improve your response.",
        "parameters": {
          "type": "object",
          "properties": {},
          "required": []
        }
      },
      {
        "type": "function",
        "name": "deep_analysis",
        "description": "Perform deep analysis using a more capable reasoning model. Use for complex questions requiring extended reasoning, code generation, detailed explanations, or tasks where the realtime model's reasoning is insufficient.",
        "parameters": {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "The question or task to analyze in depth"
            },
            "context": {
              "type": "string",
              "description": "Relevant context from the conversation so far"
            }
          },
          "required": ["query"]
        }
      }
    ],
    "tool_choice": "auto"
  }
}
```

### 2. Direct Image Input (Native)

The Realtime API (`gpt-realtime` and `gpt-realtime-mini`) supports `input_image` content parts. We can inject camera frames directly into the conversation:

```json
{
  "type": "conversation.item.create",
  "item": {
    "type": "message",
    "role": "user",
    "content": [
      {
        "type": "input_image",
        "image_url": "data:image/jpeg;base64,/9j/4AAQ..."
      }
    ]
  }
}
```

**Two strategies (not mutually exclusive):**

- **On-demand via function call**: The model calls `describe_scene`, VisionAgent captures a frame, but instead of describing it in text we inject the raw image and let the realtime model see it directly. Best quality, minimal latency added.
- **Periodic background injection**: Every N seconds, capture a low-res frame and inject it as a user message. The model maintains ambient visual awareness. Uses more tokens but enables proactive visual commentary.

We start with on-demand only (function call). Periodic injection is a future optimization.

### 3. Out-of-Band Responses

For the `deep_analysis` tool, the Chat Completions call may take several seconds. While waiting, the realtime model is paused (it emitted a function_call, waiting for output). This is acceptable because:

- The model can say "Let me think about that..." before calling the tool (natural pause)
- The user hears nothing while the function executes (same UX as a human pausing to think)
- When the result comes back, the model speaks it with full audio

If we wanted concurrent processing, we could use `conversation: "none"` for out-of-band responses, but that adds complexity we don't need yet.

## Tools Design

### `describe_scene`

**When called:** User asks about surroundings, asks to look at something, etc.

**Execution flow:**
1. Model emits function_call for `describe_scene`
2. AgentOrchestrator receives `response.done` with function_call output
3. Calls `VisionAgent.CaptureAndDescribeAsync()` which captures a JPEG frame
4. **Option A (Direct image):** Inject the image as an `input_image` content part alongside the function_call_output. The realtime model sees the actual image.
5. **Option B (Text description):** Send a text description from a vision model as function_call_output. Cheaper but lossy.
6. Send `response.create` to continue

**Recommendation:** Start with Option B (text description via vision model) because:
- The realtime model's vision capability may have token/size limits we haven't tested
- A dedicated vision model (gpt-5.4) may produce better descriptions
- We can switch to Option A later as an optimization

### `deep_analysis`

**When called:** Complex reasoning, code generation, detailed explanations.

**Execution flow:**
1. Model emits function_call for `deep_analysis` with `query` and optional `context`
2. AgentOrchestrator receives the function_call
3. Calls `ConversationAgent.AnalyzeAsync(query, context)` which calls Chat Completions (gpt-5.4)
4. Returns the full text result as function_call_output
5. Sends `response.create` — model speaks the result

**Note:** The realtime model will summarize/speak the result in its own words. This is good — it adapts the response for audio delivery (shorter sentences, conversational tone) rather than reading a wall of text.

## What Gets Removed

### Code to delete:
- `ConversationMode` enum and all references
- `ModelOptions.ConversationModes` array
- `IChatCompletionsClient` interface
- `ChatCompletionsClient` class
- Mode B code paths in `AgentOrchestrator` (`ProcessModeBAsync`, mode guards on events)
- `SendTextForTtsAsync` from `IRealtimeClient` / `RealtimeClient`
- `ResponseCreateMessage`, `ResponseCreatePayload`, `ConversationItemCreateMessage` (Mode B TTS messages) — **keep if needed for function call responses**
- Mode picker in `SettingsPage.xaml` / `SettingsViewModel`
- `ModeLabel` in `MainViewModel`
- `ConversationReplyDelta` / `ConversationReplyCompleted` events
- "Thinking..." / "Speaking..." status flow (Mode B)
- `IChatClient` / `IChatCompletionsClient` DI registration for Mode B — **repurpose for deep_analysis tool**

### Code to keep/repurpose:
- `ConversationAgent` — repurpose for `deep_analysis` function execution. Remove Mode A passthrough methods, add `AnalyzeAsync(query, context)`
- `VisionAgent` — implement properly for `describe_scene` function execution
- `IChatClient` DI registration — needed for `deep_analysis` tool's Chat Completions call
- `SessionContext` — may be useful for ConversationAgent's deep_analysis context window, but the Realtime API manages its own conversation state now
- `ConversationItemCreateMessage` — needed for `function_call_output` responses

## What Gets Added

### New RealtimeClient capabilities:

```csharp
// In IRealtimeClient:

// Function calling
event EventHandler<FunctionCallInfo>? FunctionCallReceived;

// Send function result back
Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken ct = default);

// Inject image into conversation (for describe_scene)
Task SendImageAsync(byte[] jpegData, CancellationToken ct = default);
```

### New event types:

```csharp
public record FunctionCallInfo(string CallId, string Name, string Arguments);
```

### New Realtime message types:

```csharp
// Client → Server: function_call_output
public record FunctionCallOutputMessage
{
    [JsonPropertyName("type")] public string Type => "conversation.item.create";
    [JsonPropertyName("item")] public FunctionCallOutputItem Item { get; init; }
}

public record FunctionCallOutputItem
{
    [JsonPropertyName("type")] public string Type => "function_call_output";
    [JsonPropertyName("call_id")] public string CallId { get; init; }
    [JsonPropertyName("output")] public string Output { get; init; }
}

// Client → Server: image injection
public record ImageInputMessage
{
    [JsonPropertyName("type")] public string Type => "conversation.item.create";
    [JsonPropertyName("item")] public ImageInputItem Item { get; init; }
}

public record ImageInputItem
{
    [JsonPropertyName("type")] public string Type => "message";
    [JsonPropertyName("role")] public string Role => "user";
    [JsonPropertyName("content")] public ImageContentPart[] Content { get; init; }
}

public record ImageContentPart
{
    [JsonPropertyName("type")] public string Type => "input_image";
    [JsonPropertyName("image_url")] public string ImageUrl { get; init; }  // data:image/jpeg;base64,...
}
```

### Session config with tools:

```csharp
// In SessionUpdatePayload, add:
[JsonPropertyName("tools")]
public ToolDefinition[]? Tools { get; init; }

[JsonPropertyName("tool_choice")]
public string? ToolChoice { get; init; }

// Tool definition
public record ToolDefinition
{
    [JsonPropertyName("type")] public string Type => "function";
    [JsonPropertyName("name")] public string Name { get; init; }
    [JsonPropertyName("description")] public string Description { get; init; }
    [JsonPropertyName("parameters")] public JsonElement Parameters { get; init; }
}
```

### Server event parsing additions:

In `RealtimeClient.DispatchMessage()`, handle:
- `response.function_call_arguments.done` → parse name, call_id, arguments → fire `FunctionCallReceived`
- OR parse from `response.done` output items where `type == "function_call"`

Parsing from `response.done` is simpler and gives us all function calls at once.

### AgentOrchestrator changes:

```csharp
// New handler
private async void OnFunctionCallReceived(object? sender, FunctionCallInfo info)
{
    try
    {
        string result = info.Name switch
        {
            "describe_scene" => await ExecuteDescribeSceneAsync(),
            "deep_analysis" => await ExecuteDeepAnalysisAsync(info.Arguments),
            _ => JsonSerializer.Serialize(new { error = $"Unknown function: {info.Name}" })
        };

        await _realtime.SendFunctionCallOutputAsync(info.CallId, result);
        await _realtime.CreateResponseAsync();
    }
    catch (Exception ex)
    {
        await _realtime.SendFunctionCallOutputAsync(info.CallId, 
            JsonSerializer.Serialize(new { error = ex.Message }));
        await _realtime.CreateResponseAsync();
    }
}

private async Task<string> ExecuteDescribeSceneAsync()
{
    var description = await _vision.CaptureAndDescribeAsync();
    return JsonSerializer.Serialize(new { description = description ?? "Camera not available" });
}

private async Task<string> ExecuteDeepAnalysisAsync(string argumentsJson)
{
    var args = JsonSerializer.Deserialize<DeepAnalysisArgs>(argumentsJson);
    var result = await _conversation.AnalyzeAsync(args.Query, args.Context);
    return JsonSerializer.Serialize(new { analysis = result });
}
```

## Implementation Steps

### Step 1: Add function calling to RealtimeClient
- Add `ToolDefinition` type and `tools`/`tool_choice` to `SessionUpdatePayload`
- Add `FunctionCallInfo` event
- Parse function calls from `response.done` events
- Add `SendFunctionCallOutputAsync` method
- Add tool definitions to `UpdateSessionAsync`
- Register new types in `RealtimeJsonContext`

### Step 2: Remove Mode B infrastructure
- Remove `ConversationMode` enum
- Remove `IChatCompletionsClient` / `ChatCompletionsClient`
- Remove `SendTextForTtsAsync` from `IRealtimeClient` / `RealtimeClient`
- Remove `ResponseCreateMessage` / `ResponseCreatePayload` (Mode B TTS)
- Remove all Mode B code paths from `AgentOrchestrator`
- Remove `ConversationReplyDelta` / `ConversationReplyCompleted` events
- Remove mode picker from settings UI
- Remove `ModeLabel` from MainViewModel
- Simplify `UpdateSessionAsync` (always `["text", "audio"]`)

### Step 3: Wire function call handling in AgentOrchestrator
- Subscribe to `FunctionCallReceived` event
- Implement `ExecuteDescribeSceneAsync` (delegates to VisionAgent)
- Implement `ExecuteDeepAnalysisAsync` (delegates to ConversationAgent)
- Handle errors gracefully (return error as function output)

### Step 4: Repurpose ConversationAgent
- Remove `AddUserMessage` / `AddAssistantMessage` (Realtime API manages its own context)
- Remove `ProcessTranscriptAsync` / `ProcessTranscriptFullAsync` (Mode B streaming)
- Add `AnalyzeAsync(string query, string? context)` — calls Chat Completions with gpt-5.4
- Keep `IChatClient` DI registration for this

### Step 5: Implement VisionAgent properly
- Replace stub with real implementation
- `CaptureAndDescribeAsync()` → capture frame → call vision model → return description
- OR capture frame → return raw JPEG for direct image injection
- Use `IChatClient` with vision model or OpenAI Images API

### Step 6: Update tests
- Remove Mode B test fixtures
- Add function calling tests (mock RealtimeClient, verify tool dispatch)
- Add VisionAgent tests
- Update ConversationAgent tests for new `AnalyzeAsync` signature

### Step 7: Clean up AppSettings
- Remove `Mode` property
- Remove `ChatModel` setting (or keep for deep_analysis tool)
- Keep `VisionModel` setting (for VisionAgent)
- Remove `ConversationModes` from `ModelOptions`
- Clean up SettingsService/SettingsViewModel

## Future Enhancements (Not in M3)

- **Periodic camera injection**: Background task sends low-res frames every N seconds
- **MCP integration**: Add MCP server tools to session config (Realtime API supports this natively)
- **Multi-tool calls**: Handle parallel function calls in a single response
- **Streaming function results**: For long analyses, send partial results
- **Out-of-band responses**: Use `conversation: "none"` for background processing
- **Direct image injection**: Switch from text descriptions to raw images for describe_scene

## Risk Assessment

| Risk | Mitigation |
|------|-----------|
| Realtime model's reasoning quality vs gpt-5.4 | `deep_analysis` tool delegates to gpt-5.4 when needed |
| Function call adds latency for vision/analysis | Model says "Let me look..." or "Let me think..." naturally |
| Image input token costs | Start with on-demand only, not periodic |
| Tool definitions increase session config size | Minimal — a few hundred bytes |
| Realtime API doesn't support all tool patterns | We control the tool definitions; keep them simple |
