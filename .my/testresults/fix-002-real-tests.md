# FIX-002 — Real Tests (RealtimeFixture & M5ToolFixture)

**RCA:** [rca-real-tests.md](rca-real-tests.md)
**Status:** Applied

---

## Changes

### 1. `BodyCam.RealTests.csproj` — Added `NoWarn`

```xml
<NoWarn>MEAI001;OPENAI002</NoWarn>
```

### 2. `RealtimeFixture.cs` — Complete rewrite

**Old architecture:** Event-driven using custom `RealtimeClient` class.
**New architecture:** MAF `IRealtimeClient` → `IRealtimeClientSession` with streaming message loop.

| Old API | New API |
|---|---|
| `new RealtimeClient(apiKeyService, settings, dispatcher)` | `new OpenAIRealtimeClient(apiKey, model)` or Azure variant |
| `_client.ConnectAsync()` | `_client.CreateSessionAsync(options)` |
| `_client.DisconnectAsync()` | `_session.DisposeAsync()` |
| Event handlers (`RawMessageReceived`, `AudioDelta`, etc.) | `RunMessageLoopAsync()` with `GetStreamingResponseAsync()` |
| `ServerEventParser.GetType(json)` | `ExtractType(rawJson)` — parse `"type"` from raw JSON |
| `_client.SendTextInputAsync(text)` | Raw JSON via `session.SendAsync(RealtimeClientMessage)` |
| `_client.SendFunctionCallOutputAsync(callId, output)` | Raw JSON `conversation.item.create` + `response.create` |
| `_client.CancelResponseAsync()` | Raw JSON `response.cancel` |

**Key design decisions:**
- Raw JSON serialization of `RawRepresentation` for backward-compatible event type tracking (e.g., `"response.audio_transcript.delta"`)
- Function calls detected via both typed `FunctionCallContent` on `ResponseOutputItemDone` AND fallback raw JSON parsing of `response.function_call_arguments.done`
- `BuildClient()`, `LoadSettings()`, `LoadApiKey()`, `SerializeRaw()`, `ExtractType()` made `internal static` for reuse by `M5ToolFixture`
- `BuildSessionOptions()` made `protected virtual` for override capability

### 3. `M5ToolFixture.cs` — Complete rewrite

Same MAF migration as `RealtimeFixture`, plus:
- Reuses `RealtimeFixture.BuildClient()`, `LoadSettings()`, `LoadApiKey()`, `SerializeRaw()`, `ExtractType()`
- Tool registration via `session.update` raw JSON with exact tool schemas from `ToolDispatcher.GetToolDefinitions()`
- Stub `AITool` instances created via `AIFunctionFactory.Create()` for MAF session options
- `StubChatClient` retained for tool dependency injection

### 4. `TranscriptOrderingTests.cs` — Updated `CancelResponseAsync` call

```csharp
// Old
await _fixture.Client.CancelResponseAsync();
// New
await _fixture.CancelResponseAsync();
```

The `Client` property was removed since `IRealtimeClient` is now internal to the fixture.

## Verification

- `dotnet build` — 0 errors
- Tests require live API keys — verified build-only
