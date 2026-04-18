# ToolDispatcher — How It Works

## Overview

`ToolDispatcher` is the central registry and router for all device-side tools the LLM can invoke via function calling. It sits between the OpenAI Realtime API and the individual `ITool` implementations.

```
OpenAI Realtime API
        │
        ├── session.update ← GetToolDefinitions() sends tool schemas
        │
        └── response.done  → FunctionCallReceived event
                                    │
                              AgentOrchestrator
                                    │
                              ToolDispatcher.ExecuteAsync(name, args, ctx, ct)
                                    │
                              ITool.ExecuteAsync(args, ctx, ct)
```

## Registration

All tools are registered as `ITool` singletons in DI. `ToolDispatcher` collects them via `IEnumerable<ITool>` and indexes by name (case-insensitive):

```csharp
// MauiProgram.cs
builder.Services.AddSingleton<ITool, ReadTextTool>();
builder.Services.AddSingleton<ITool, TakePhotoTool>();
builder.Services.AddSingleton<ITool, SaveMemoryTool>();
// ... more tools ...
builder.Services.AddSingleton<ToolDispatcher>();
```

```csharp
// ToolDispatcher constructor
public ToolDispatcher(IEnumerable<ITool> tools)
{
    _tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
    foreach (var tool in tools)
        _tools[tool.Name] = tool;
}
```

Adding a new tool requires only the `ITool` DI registration — no other wiring.

## Three Responsibilities

### 1. Schema Export — `GetToolDefinitions()`

Returns enabled tools as `ToolDefinitionDto` list. Called by `RealtimeClient.UpdateSessionAsync` to send tool schemas to OpenAI in the `session.update` message:

```
ToolDispatcher.GetToolDefinitions()
    → [{ name, description, parametersJson }]
        → RealtimeClient maps to ToolDefinition[]
            → sent as session.update.tools
```

The LLM uses these schemas to decide when and how to call tools.

### 2. Execution — `ExecuteAsync()`

Routes a function call to the correct tool by name:

```
ExecuteAsync("read_text", """{"focus":"sign"}""", context, ct)
    1. Look up "read_text" in _tools dictionary
    2. Check tool.IsEnabled
    3. Call tool.ExecuteAsync(argumentsJson, context, ct)
    4. Return result.Json (string sent back to the API)
```

Error cases return JSON error envelopes (not exceptions):
- Unknown tool → `{ "error": "Unknown function: xyz" }`
- Disabled tool → `{ "error": "Tool 'xyz' is currently disabled." }`

### 3. Wake Word Entries — `BuildWakeWordEntries()`

Aggregates wake word bindings from tools that define them, plus system wake words ("Hey BodyCam", "Go to sleep"). Used by `PorcupineWakeWordService` to register all keyword models.

## Call Sites

### AgentOrchestrator — Function Calling (primary path)

When the LLM decides to call a function, `RealtimeClient` fires `FunctionCallReceived`. The orchestrator handles it:

```csharp
private async void OnFunctionCallReceived(object? sender, FunctionCallInfo info)
{
    var context = CreateToolContext();
    var result = await _dispatcher.ExecuteAsync(
        info.Name, info.Arguments, context, _cts?.Token ?? CancellationToken.None);
    await _realtime.SendFunctionCallOutputAsync(info.CallId, result);
}
```

### AgentOrchestrator — Wake Word Tool Invocation

When a wake word triggers `WakeWordAction.InvokeTool`, the orchestrator calls the tool directly without LLM involvement:

```csharp
case WakeWordAction.InvokeTool:
    var context = CreateToolContext();
    var result = await _dispatcher.ExecuteAsync(
        e.ToolName, null, context, _cts?.Token ?? CancellationToken.None);
    break;
```

Arguments are `null` here — the tool uses defaults or its `WakeWordBinding.InitialPrompt`.

### RealtimeClient — Session Configuration

`RealtimeClient` reads tool definitions to include in the WebSocket session:

```csharp
private ToolDefinition[] GetToolDefinitions()
{
    return _dispatcher.GetToolDefinitions()
        .Select(dto => new ToolDefinition
        {
            Type = dto.Type,
            Name = dto.Name,
            Description = dto.Description,
            Parameters = JsonDocument.Parse(dto.ParametersJson).RootElement
        })
        .ToArray();
}
```

## ITool + ToolBase\<TArgs\>

`ITool` is the raw interface. Most tools extend `ToolBase<TArgs>` which handles JSON deserialization:

```
ITool.ExecuteAsync(string? argumentsJson, ToolContext, CancellationToken)
  └── ToolBase<TArgs>.ExecuteAsync  ← deserializes argumentsJson → TArgs
        └── abstract ExecuteAsync(TArgs args, ToolContext, CancellationToken)
              └── concrete tool implementation (ReadTextTool, etc.)
```

`ToolBase<TArgs>` also auto-generates the JSON schema from `TArgs` via `SchemaGenerator.Generate<TArgs>()`, which uses reflection over `[Description]` attributes and property types.

## ToolContext

Every tool execution receives a `ToolContext` built by `AgentOrchestrator.CreateToolContext()`:

| Property | Type | Source |
|----------|------|--------|
| `CaptureFrame` | `Func<CancellationToken, Task<byte[]?>>` | `CameraManager.CaptureFrameAsync` |
| `Session` | `SessionContext` | `AgentOrchestrator.Session` |
| `Log` | `Action<string>` | Routes to `DebugLog` event |
| `RealtimeClient` | `IRealtimeClient` | The active WebSocket connection |

## ToolResult

Tools return `ToolResult.Success(data)` or `ToolResult.Fail(error)`. Both produce a `.Json` string that gets sent back to the Realtime API as the function call output.

## Lookup Helpers

- `GetTool(name)` — returns `ITool?`, used for inspection/testing
- `ToolNames` — returns all registered tool names
