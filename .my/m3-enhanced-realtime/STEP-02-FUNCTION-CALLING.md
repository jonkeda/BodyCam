# Step 2: Add Function Calling to RealtimeClient

Add tool definitions to the session config and handle function call events from the Realtime API.

## Depends On: Step 1

## Files Modified

### 1. `src/BodyCam/Services/Realtime/RealtimeMessages.cs`

**Add** tool definition types and function call output message types:

```csharp
// --- Tool definitions for session.update ---

internal record ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; init; }
}

// --- Function call output (client → server) ---

internal record FunctionCallOutputMessage : RealtimeMessage
{
    [JsonPropertyName("item")]
    public FunctionCallOutputItem Item { get; init; } = new();
}

internal record FunctionCallOutputItem
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function_call_output";

    [JsonPropertyName("call_id")]
    public string CallId { get; init; } = "";

    [JsonPropertyName("output")]
    public string Output { get; init; } = "";
}
```

**Add** `Tools` and `ToolChoice` to `SessionUpdatePayload`:

```csharp
internal record SessionUpdatePayload
{
    // ... existing properties ...

    [JsonPropertyName("tools")]
    public ToolDefinition[]? Tools { get; init; }

    [JsonPropertyName("tool_choice")]
    public string? ToolChoice { get; init; }
}
```

**Add** function call parsing to `ServerEventParser`:

```csharp
/// <summary>
/// Parses function call info from a response.done event.
/// Returns a list because a response can contain multiple function calls.
/// </summary>
public static List<(string callId, string name, string arguments)> ParseFunctionCalls(string json)
{
    var results = new List<(string, string, string)>();
    try
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("response", out var resp) &&
            resp.TryGetProperty("output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "function_call" &&
                    item.TryGetProperty("call_id", out var callIdProp) &&
                    item.TryGetProperty("name", out var nameProp) &&
                    item.TryGetProperty("arguments", out var argsProp))
                {
                    results.Add((
                        callIdProp.GetString() ?? "",
                        nameProp.GetString() ?? "",
                        argsProp.GetString() ?? ""
                    ));
                }
            }
        }
    }
    catch { }
    return results;
}
```

### 2. `src/BodyCam/Services/Realtime/RealtimeJsonContext.cs`

**Add** new serializable types:

```csharp
[JsonSerializable(typeof(FunctionCallOutputMessage))]
```

Note: `ToolDefinition` is serialized as part of `SessionUpdateMessage` (via `SessionUpdatePayload.Tools`) so it's already covered. But the `FunctionCallOutputMessage` uses `conversation.item.create` type — we can reuse the existing `ConversationItemCreateMessage` registration OR add a new one. Since `FunctionCallOutputItem` has a different shape, use `FunctionCallOutputMessage`.

### 3. `src/BodyCam/Models/RealtimeModels.cs`

**Add** `FunctionCallInfo` record:

```csharp
/// <summary>
/// Information about a function call requested by the Realtime API.
/// </summary>
public record FunctionCallInfo(string CallId, string Name, string Arguments);
```

### 4. `src/BodyCam/Services/IRealtimeClient.cs`

**Add** function calling event and method:

```csharp
/// <summary>
/// Fired when the Realtime API requests a function call.
/// Parsed from response.done events containing function_call output items.
/// </summary>
event EventHandler<FunctionCallInfo>? FunctionCallReceived;

/// <summary>
/// Sends the result of a function call back to the Realtime API.
/// Creates a conversation item with type function_call_output, then triggers response.create.
/// </summary>
Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken ct = default);
```

### 5. `src/BodyCam/Services/RealtimeClient.cs`

**Add** event declaration:

```csharp
public event EventHandler<FunctionCallInfo>? FunctionCallReceived;
```

**Add** `SendFunctionCallOutputAsync` method:

```csharp
public async Task SendFunctionCallOutputAsync(string callId, string output, CancellationToken ct = default)
{
    var msg = new FunctionCallOutputMessage
    {
        Type = "conversation.item.create",
        Item = new FunctionCallOutputItem
        {
            CallId = callId,
            Output = output
        }
    };
    await SendJsonAsync(msg, RealtimeJsonContext.Default.FunctionCallOutputMessage, ct);

    // Trigger the model to continue with the function result
    await CreateResponseAsync(ct);
}
```

**Modify** `DispatchMessage` — in the `response.done` case, after existing parsing, check for function calls:

```csharp
case "response.done":
{
    var (responseId, itemId, outputTranscript, inputTranscript) =
        ServerEventParser.ParseResponseDone(json);
    if (responseId is not null)
    {
        ResponseDone?.Invoke(this, new RealtimeResponseInfo
        {
            ResponseId = responseId,
            ItemId = itemId,
            OutputTranscript = outputTranscript,
            InputTranscript = inputTranscript
        });
    }

    // Check for function calls in the response output
    var functionCalls = ServerEventParser.ParseFunctionCalls(json);
    foreach (var (callId, name, arguments) in functionCalls)
    {
        FunctionCallReceived?.Invoke(this, new FunctionCallInfo(callId, name, arguments));
    }
    break;
}
```

**Modify** `UpdateSessionAsync` — add tool definitions:

```csharp
public async Task UpdateSessionAsync(CancellationToken ct = default)
{
    var tools = GetToolDefinitions();

    var msg = new SessionUpdateMessage
    {
        Type = "session.update",
        Session = new SessionUpdatePayload
        {
            Modalities = new[] { "text", "audio" },
            Voice = _settings.Voice,
            Instructions = _settings.SystemInstructions,
            InputAudioFormat = "pcm16",
            OutputAudioFormat = "pcm16",
            InputAudioTranscription = new InputAudioTranscription { Model = _settings.TranscriptionModel },
            TurnDetection = new TurnDetectionConfig { Type = _settings.TurnDetection },
            Tools = tools.Length > 0 ? tools : null,
            ToolChoice = tools.Length > 0 ? "auto" : null
        }
    };
    await SendJsonAsync(msg, RealtimeJsonContext.Default.SessionUpdateMessage, ct);
}

private static ToolDefinition[] GetToolDefinitions()
{
    return
    [
        new ToolDefinition
        {
            Name = "describe_scene",
            Description = "Capture what the camera currently sees. Use when the user asks about their surroundings, asks you to look at something, or when visual context would help.",
            Parameters = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {},
                    "required": []
                }
                """).RootElement
        },
        new ToolDefinition
        {
            Name = "deep_analysis",
            Description = "Perform deep analysis using a more capable reasoning model. Use for complex questions, code generation, detailed explanations, or tasks needing extended reasoning.",
            Parameters = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "query": {
                            "type": "string",
                            "description": "The question or task to analyze in depth"
                        },
                        "context": {
                            "type": "string",
                            "description": "Relevant context from the conversation"
                        }
                    },
                    "required": ["query"]
                }
                """).RootElement
        }
    ];
}
```

**Note:** `GetToolDefinitions()` uses `JsonDocument.Parse` to create `JsonElement` values for the parameters schema. These are static so the overhead is minimal. If desired, cache them as `static readonly` fields.

## Verification

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore -v q
```
