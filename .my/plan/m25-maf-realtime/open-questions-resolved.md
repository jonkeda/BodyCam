# M25 — Open Questions Resolved

Answers to the 5 open questions from [phase1-sdk-migration.md](phase1-sdk-migration.md), verified against the installed NuGet packages (M.E.AI Abstractions 10.5.0, M.E.AI.OpenAI 10.5.0, OpenAI 2.10.0, Azure.AI.OpenAI 2.1.0).

---

## Q1: How does `IRealtimeClientSession.SendAsync()` accept audio data?

**Answer: `InputAudioBufferAppendRealtimeClientMessage` wrapping a `DataContent`.**

M.E.AI defines a dedicated client message type:

```csharp
// Constructor takes DataContent (M.E.AI's abstraction for binary + media type)
var audioMsg = new InputAudioBufferAppendRealtimeClientMessage(
    new DataContent(pcmBytes, "audio/pcm"));
await session.SendAsync(audioMsg, ct);
```

There's also `InputAudioBufferCommitRealtimeClientMessage` for committing the buffer:

```csharp
await session.SendAsync(new InputAudioBufferCommitRealtimeClientMessage(), ct);
```

**For receiving audio**, the server sends `OutputTextAudioRealtimeServerMessage` (shared type for text and audio). The `Audio` property is a `DataContent` when the type is `OutputAudioDelta`:

```csharp
case var t when t == RealtimeServerMessageType.OutputAudioDelta:
    var msg = (OutputTextAudioRealtimeServerMessage)message;
    byte[] pcm = msg.Audio.Data.ToArray();  // DataContent → ReadOnlyMemory<byte>
    await _voiceOut.PlayAudioDeltaAsync(pcm);
    break;
```

**Impact on phase doc**: Wave 3 (VoiceInputAgent) is straightforward — wrap `byte[]` in `DataContent`, send via `InputAudioBufferAppendRealtimeClientMessage`.

---

## Q2: Does `RealtimeServerMessageType` include SpeechStarted/SpeechStopped?

**Answer: No. These are OpenAI-specific events not in the MAF abstraction.**

The well-known `RealtimeServerMessageType` values are:

| Type | Present |
|---|---|
| `InputAudioTranscriptionCompleted` | ✅ |
| `InputAudioTranscriptionDelta` | ✅ |
| `InputAudioTranscriptionFailed` | ✅ |
| `OutputTextDelta` / `OutputTextDone` | ✅ |
| `OutputAudioTranscriptionDelta` / `OutputAudioTranscriptionDone` | ✅ |
| `OutputAudioDelta` / `OutputAudioDone` | ✅ |
| `ResponseCreated` / `ResponseDone` | ✅ |
| `ResponseOutputItemAdded` / `ResponseOutputItemDone` | ✅ |
| `ConversationItemAdded` / `ConversationItemDone` | ✅ |
| `Error` | ✅ |
| **SpeechStarted** | ❌ Not in MAF |
| **SpeechStopped** | ❌ Not in MAF |

However, `RealtimeServerMessageType` is an **extensible struct** with a `Value` property and a constructor that takes any string. The OpenAI SDK provider passes through all events, and the underlying OpenAI types are available via `RawRepresentation`.

**Solution**: Use custom type values for speech events:

```csharp
// Create custom type constants
private static readonly RealtimeServerMessageType SpeechStarted =
    new("input_audio_buffer.speech_started");
private static readonly RealtimeServerMessageType SpeechStopped =
    new("input_audio_buffer.speech_stopped");

// In the dispatch loop
case var t when t == SpeechStarted:
    await HandleSpeechStarted(session, ct);
    break;
```

Or access the underlying SDK type:
```csharp
if (message.RawRepresentation is RealtimeServerUpdateInputAudioBufferSpeechStarted)
{
    await HandleSpeechStarted(session, ct);
}
```

The `RealtimeServerMessageType(string)` constructor approach is cleaner — it keeps us in the MAF abstraction.

**Impact on phase doc**: Wave 2.7 is resolved. Use custom `RealtimeServerMessageType` constants. No `RawRepresentation` needed.

**Bonus**: MAF has `VoiceActivityDetectionOptions` on `RealtimeSessionOptions`:
```csharp
VoiceActivityDetection = new VoiceActivityDetectionOptions
{
    Enabled = true,
    AllowInterruption = true  // Controls barge-in behavior
}
```
This maps our `_settings.TurnDetection` setting. However, `NoiseReduction` is not a MAF property — it will need `RawRepresentationFactory`.

---

## Q3: Is there a MAF-level API for `conversation.item.truncate`?

**Answer: No dedicated type. Send via `RealtimeClientMessage` with `RawRepresentation`.**

MAF defines these client message types:
- `InputAudioBufferAppendRealtimeClientMessage`
- `InputAudioBufferCommitRealtimeClientMessage`
- `CreateConversationItemRealtimeClientMessage`
- `CreateResponseRealtimeClientMessage`
- `SessionUpdateRealtimeClientMessage`

No truncation message. The OpenAI SDK has `RealtimeClientCommandConversationItemTruncate` in the `OpenAI.Realtime` namespace.

**Solution**: Create a `RealtimeClientMessage` with the SDK command as `RawRepresentation`:

```csharp
private async Task TruncateAudioAsync(
    IRealtimeClientSession session, string itemId, int audioEndMs, CancellationToken ct)
{
    // Use OpenAI SDK type directly as RawRepresentation
    var truncateCmd = new RealtimeClientCommandConversationItemTruncate(itemId, 0, audioEndMs);
    var msg = new RealtimeClientMessage { RawRepresentation = truncateCmd };
    await session.SendAsync(msg, ct);
}
```

The `OpenAIRealtimeClientSession.SendAsync()` implementation checks for `RawRepresentation` and dispatches SDK commands directly.

**Alternative**: If `SendAsync` doesn't handle arbitrary `RawRepresentation`, drop to the underlying `RealtimeSessionClient` via `session.GetService<RealtimeSessionClient>()`.

**Impact on phase doc**: Wave 2.5 (`HandleSpeechStarted`) needs the truncation helper. Not a blocker — same pattern as speech events.

---

## Q4: How to map `ToolDispatcher.GetToolDefinitions()` → `AITool` instances?

**Answer: Use `AIFunctionFactory.Create()` + `AsOpenAIRealtimeFunctionTool()` extension.**

M.E.AI uses `AITool` / `AIFunction` from the chat abstractions. The `RealtimeSessionOptions.Tools` property is `IList<AITool>`.

Our current `ToolDispatcher.GetToolDefinitions()` returns `ToolDefinitionDto` (name, description, parametersJson). We need to convert these to `AITool`.

**Option A — Direct AIFunction creation (preferred)**:
```csharp
var tools = _dispatcher.GetToolDefinitions()
    .Select(dto => AIFunctionFactory.Create(
        name: dto.Name,
        description: dto.Description,
        parameters: JsonDocument.Parse(dto.ParametersJson).RootElement,
        // No actual implementation — we handle invocation ourselves
        implementation: (args, ct) => Task.FromResult<object?>(null)))
    .Cast<AITool>()
    .ToList();
```

**Option B — Use `FunctionInvokingRealtimeClient` middleware (game-changer)**:

M.E.AI 10.5.0 includes `FunctionInvokingRealtimeClient` — a middleware that **automatically intercepts function calls, invokes `AITool` implementations, and sends results back**. This would eliminate our entire `OnFunctionCallReceived` handler + `SendFunctionCallOutputAsync` dance.

```csharp
// DI registration with middleware pipeline
services.AddSingleton<Microsoft.Extensions.AI.IRealtimeClient>(sp =>
{
    var openaiClient = new OpenAIRealtimeClient(sdkClient, model);
    return openaiClient.AsBuilder()
        .UseFunctionInvocation(sp.GetRequiredService<ILoggerFactory>())
        .UseLogging(sp.GetRequiredService<ILoggerFactory>())
        .Build(sp);
});
```

With this, tools need real `AIFunction` implementations. We'd wrap `ToolDispatcher.ExecuteAsync`:
```csharp
var tools = _dispatcher.ToolNames.Select(name =>
{
    var tool = _dispatcher.GetTool(name)!;
    return AIFunctionFactory.Create(
        name: tool.Name,
        description: tool.Description,
        returnDescription: "JSON result",
        parameters: /* from tool.ParameterSchema */,
        implementation: async (args, ct) =>
        {
            var context = CreateToolContext();
            return await _dispatcher.ExecuteAsync(tool.Name, args?.ToString(), context, ct);
        });
}).Cast<AITool>().ToList();
```

The `FunctionInvokingRealtimeClientSession` will:
1. Detect `ResponseOutputItemDone` messages with function calls
2. Extract call_id, name, arguments
3. Invoke the matching `AITool`
4. Send `CreateConversationItemRealtimeClientMessage` with the result
5. Trigger a new response

**This replaces 30+ lines of our orchestrator code.**

**Impact on phase doc**: Wave 2 gets significantly simpler. The `OnFunctionCallReceived` handler, `SendFunctionCallOutputAsync`, and `CreateResponseAsync` dance are all handled by middleware. We should use Option B.

---

## Q5: Does `AzureOpenAIClient.AsRealtimeClient()` work for Realtime?

**Answer: No `AsRealtimeClient()` extension exists. But Azure works through inheritance.**

The M.E.AI.OpenAI package does NOT have an `AsRealtimeClient()` extension method (searched both packages — not found). Instead, `OpenAIRealtimeClient` has two constructors:

1. `OpenAIRealtimeClient(string apiKey, string model)` — simple, OpenAI-only
2. `OpenAIRealtimeClient(OpenAI.Realtime.RealtimeClient realtimeClient, string model)` — wraps an SDK client

`AzureOpenAIClient` inherits from `OpenAIClient`. While `AzureOpenAIClient` doesn't override `GetRealtimeClient()` (not in its XML docs), the base `OpenAIClient.GetRealtimeClient()` method IS available via inheritance.

**Solution for Azure**:
```csharp
if (settings.Provider == OpenAiProvider.Azure)
{
    var azureClient = new AzureOpenAIClient(
        new Uri(settings.AzureEndpoint!),
        new ApiKeyCredential(apiKey));

    // GetRealtimeClient() inherited from OpenAIClient
    var sdkRealtimeClient = azureClient.GetRealtimeClient();
    return new OpenAIRealtimeClient(sdkRealtimeClient, settings.AzureRealtimeDeploymentName!);
}
else
{
    return new OpenAIRealtimeClient(apiKey, settings.RealtimeModel);
}
```

**Risk**: `AzureOpenAIClient.GetRealtimeClient()` returns the base `RealtimeClient` — it may not include Azure-specific headers (api-version, deployment routing). Need to test.

**Fallback**: If Azure Realtime doesn't work through the inherited method, construct the SDK `RealtimeClient` manually with Azure-specific options:
```csharp
var sdkRealtimeClient = new OpenAI.Realtime.RealtimeClient(
    model: settings.AzureRealtimeDeploymentName,
    new ApiKeyCredential(apiKey),
    new RealtimeClientOptions
    {
        Endpoint = new Uri($"{settings.AzureEndpoint}/openai/realtime?api-version={settings.AzureApiVersion}")
    });
```

**Impact on phase doc**: Wave 1 DI registration needs Azure path testing. Not a blocker — current hand-rolled code already handles Azure via custom URI + headers. The SDK should handle this too.

---

## Summary of Findings

| Question | Answer | Impact |
|---|---|---|
| Audio format | `InputAudioBufferAppendRealtimeClientMessage(DataContent)` | Clean — wrap `byte[]` in `DataContent` |
| Speech events | Not in MAF; use `new RealtimeServerMessageType("...")` | Clean — extensible struct handles it |
| Truncation | Not in MAF; use `RawRepresentation` with SDK command | Minor workaround needed |
| Tool definitions | `AIFunctionFactory.Create()` + **`FunctionInvokingRealtimeClient` middleware** | **Eliminates 30+ lines of function call plumbing** |
| Azure | `AzureOpenAIClient` inherits `GetRealtimeClient()` | Needs testing; fallback available |

### New Discovery: `FunctionInvokingRealtimeClient` Middleware

The biggest win is `FunctionInvokingRealtimeClient`. It's a `DelegatingRealtimeClient` that wraps any `IRealtimeClient` and automatically:
- Detects function calls in the response stream
- Invokes the matching `AITool` implementation
- Sends the result back to the API
- Triggers continuation

This eliminates our entire manual function call pipeline (`OnFunctionCallReceived` → `ToolDispatcher.ExecuteAsync` → `SendFunctionCallOutputAsync` → `CreateResponseAsync`).

**Builder pipeline** for DI:
```csharp
services.AddSingleton<IRealtimeClient>(sp =>
    new OpenAIRealtimeClient(apiKey, model)
        .AsBuilder()
        .UseFunctionInvocation(sp.GetRequiredService<ILoggerFactory>())
        .UseLogging(sp.GetRequiredService<ILoggerFactory>())
        .Build(sp));
```

### Phase Doc Updates Needed

1. **Wave 1** — DI registration uses builder pipeline with `.UseFunctionInvocation()` and `.UseLogging()`
2. **Wave 2** — Orchestrator dispatch loop is simpler: no `OnFunctionCallReceived` handler; add speech events via custom `RealtimeServerMessageType`; truncation via `RawRepresentation`
3. **Wave 2.3** — `BuildSessionOptions` maps `VoiceActivityDetection` for turn detection; uses `RawRepresentationFactory` for noise reduction
4. **Wave 4** — `ToolContext.RealtimeClient` removal confirmed (no tool uses it); tools registered as `AITool` on session options
