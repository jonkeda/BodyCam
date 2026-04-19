# M25b — GA Endpoint + MAF Realtime Migration

Migrates from the raw WebSocket JSON parsing (preview endpoint) to the standard
`Microsoft.Extensions.AI.IRealtimeClient` abstraction over the now-working Azure
GA endpoint.

---

## Background

### What M25 Planned
Replace our hand-rolled `IRealtimeClient` + 700 lines of WebSocket plumbing with
`Microsoft.Extensions.AI.IRealtimeClient` (MAF) using typed events and automatic
function invocation middleware.

### What Happened
1. Dead-code cleanup succeeded: `IRealtimeClient.cs`, `RealtimeClient.cs`,
   `RealtimeMessages.cs`, `RealtimeJsonContext.cs` all deleted. VoiceInputAgent
   moved to delegate pattern. ToolContext simplified.
2. We hit the **endpoint brick wall**: Azure preview endpoint
   (`/openai/realtime?api-version=...&deployment=...`) only returns preview-format
   event names (`response.audio.delta` etc.) that the SDK can't parse as GA types.
3. Fallback: raw WebSocket JSON parsing in `AgentOrchestrator.RunMessageLoopAsync`,
   handling both preview and GA event names manually.

### What Changed (April 19 2026)
The GA endpoint **works** on the existing East US 2 resource:
```
wss://jonk4-me1wfl8r-eastus2.cognitiveservices.azure.com/openai/v1/realtime?model=gpt-realtime-mini
```
- WebSocket handshake succeeds, session opens, closes cleanly
- Uses `model` query param (not `deployment`), no `api-version` needed
- `api-key` header still required
- Plain HTTP returns 404 (WebSocket-only — this was the false negative that
  blocked us before)

---

## Current State

| Component | Current Implementation | Target |
|---|---|---|
| DI registration | `OpenAI.Realtime.RealtimeClient` via `AzureRealtimeClient` subclass | `IRealtimeClient` via `OpenAIRealtimeClient` |
| Endpoint | `/openai/v1/realtime` (partially migrated) | Same |
| Session config | `ConfigureConversationSessionAsync` (SDK typed, partially migrated) | `RealtimeSessionOptions` on `CreateSessionAsync` |
| Message loop | `ReceiveUpdatesAsync` + SDK typed events (partially migrated) | `GetStreamingResponseAsync` + MAF typed events |
| Tool dispatch | Manual in `HandleResponseDoneAsync` | Manual (no MAF middleware exists) |
| Audio send | `session.SendInputAudioAsync(stream)` | `session.SendAsync(InputAudioBufferAppendRealtimeClientMessage)` |
| Text input | `session.AddItemAsync` + `session.StartResponseAsync` | `session.SendAsync(CreateConversationItemRealtimeClientMessage)` + `session.SendAsync(CreateResponseRealtimeClientMessage)` |
| Unit tests | `StubRealtimeClient` extends `OpenAI.Realtime.RealtimeClient` | Mock `IRealtimeClient` / `IRealtimeClientSession` |
| Real tests | `RealtimeFixture` builds `AzureRealtimeClient` | `RealtimeFixture` builds `OpenAIRealtimeClient` |

### Partial Changes Already Made (This Session)
The following files were edited toward SDK typed events (not MAF) and need to
be **replaced** with MAF equivalents:

- `AzureRealtimeClient.cs` — simplified for GA (no deployment/apiVersion)
- `ServiceExtensions.cs` — endpoint changed to `/openai/v1/realtime`
- `RealtimeFixture.cs` — endpoint changed
- `AgentOrchestrator.cs` — `ConfigureSessionAsync` uses SDK typed API,
  `RunMessageLoopAsync` uses `ReceiveUpdatesAsync` + pattern matching,
  `HandleResponseDoneAsync` takes `RealtimeResponse`

---

## Key Discovery: No FunctionInvoking Middleware for Realtime

M25 planned to use `FunctionInvokingRealtimeClient` middleware via
`.UseFunctionInvocation()`. **This does not exist** in M.E.AI 10.5.0.
There is no builder pattern (`AsBuilder()`) and no function invocation
middleware for `IRealtimeClient`.

`DelegatingRealtimeClient` exists for custom middleware, but no built-in
function dispatch. We must handle tool calls manually regardless of whether
we use MAF or the raw SDK.

### What MAF Does Give Us

1. **`IRealtimeClient` / `IRealtimeClientSession`** — clean interfaces,
   mockable without reflection hacks (`StubRealtimeClient` won't need
   `BindingFlags.NonPublic` constructor tricks)
2. **`GetStreamingResponseAsync`** — `IAsyncEnumerable<RealtimeServerMessage>`
   with typed message subtypes
3. **`RealtimeSessionOptions`** — typed session config (voice, tools,
   instructions, VAD) instead of raw JSON or SDK-specific types
4. **`RealtimeServerMessageType`** — well-known constants for message dispatch
5. **Provider-agnostic** — `IRealtimeClient` works with any provider, not just
   OpenAI. Testing with mock sessions is straightforward.

---

## API Surface Reference

### MAF Abstractions (M.E.AI 10.5.0)
```
IRealtimeClient
  CreateSessionAsync(RealtimeSessionOptions, CancellationToken) → IRealtimeClientSession

IRealtimeClientSession
  Options → RealtimeSessionOptions
  GetStreamingResponseAsync(CancellationToken) → IAsyncEnumerable<RealtimeServerMessage>
  SendAsync(RealtimeClientMessage, CancellationToken) → Task

RealtimeSessionOptions
  Instructions, Model, Voice, Tools, OutputModalities
  VoiceActivityDetection { Enabled, AllowInterruption }
  TranscriptionOptions, InputAudioFormat, OutputAudioFormat
  RawRepresentationFactory → Func (for provider-specific overrides)

RealtimeServerMessage → subtypes:
  OutputTextAudioRealtimeServerMessage  (Audio, Text, ItemId, ContentIndex)
  InputAudioTranscriptionRealtimeServerMessage  (Transcription, ItemId)
  ResponseOutputItemRealtimeServerMessage  (Item, ResponseId)
  ResponseCreatedRealtimeServerMessage  (Items, Status, ResponseId)
  ErrorRealtimeServerMessage  (Error)

RealtimeServerMessageType (well-known):
  OutputAudioDelta, OutputAudioDone
  OutputTextDelta, OutputTextDone
  OutputAudioTranscriptionDelta, OutputAudioTranscriptionDone
  InputAudioTranscriptionCompleted, InputAudioTranscriptionDelta, InputAudioTranscriptionFailed
  ResponseCreated, ResponseDone
  ResponseOutputItemAdded, ResponseOutputItemDone
  ConversationItemAdded, ConversationItemDone
  Error, RawContentOnly

NOT in MAF (need custom type or RawRepresentation):
  SpeechStarted, SpeechStopped

RealtimeClientMessage → subtypes:
  InputAudioBufferAppendRealtimeClientMessage  (Content: DataContent)
  InputAudioBufferCommitRealtimeClientMessage
  CreateConversationItemRealtimeClientMessage  (Item: RealtimeConversationItem)
  CreateResponseRealtimeClientMessage  (Instructions, Tools, ...)
  SessionUpdateRealtimeClientMessage
```

### MAF.OpenAI (M.E.AI.OpenAI 10.5.0)
```
OpenAIRealtimeClient : IRealtimeClient
  ctor(string apiKey, string model)
  ctor(RealtimeClient realtimeClient, string model)
```
The second constructor takes an `OpenAI.Realtime.RealtimeClient` — this is how
we inject our `AzureRealtimeClient` for Azure endpoint support.

---

## Plan

### Step 1 — DI: Register `IRealtimeClient` via `OpenAIRealtimeClient`

**File:** `ServiceExtensions.cs`

Replace the `OpenAI.Realtime.RealtimeClient` registration with
`Microsoft.Extensions.AI.IRealtimeClient`.

```csharp
services.AddSingleton<IRealtimeClient>(sp =>
{
    var settings = sp.GetRequiredService<AppSettings>();
    var apiKeyService = sp.GetRequiredService<IApiKeyService>();
    var apiKey = apiKeyService.GetApiKeyAsync().GetAwaiter().GetResult()
        ?? throw new InvalidOperationException("API key not configured.");

    if (settings.Provider == OpenAiProvider.Azure)
    {
        // AzureRealtimeClient handles the api-key header injection
        var rtOptions = new RealtimeClientOptions
        {
            Endpoint = new Uri($"{settings.AzureEndpoint!.TrimEnd('/')}/openai/v1/realtime")
        };
        var sdkClient = new AzureRealtimeClient(apiKey, rtOptions);
        return new OpenAIRealtimeClient(sdkClient, settings.AzureRealtimeDeploymentName!);
    }
    else
    {
        return new OpenAIRealtimeClient(apiKey, settings.RealtimeModel);
    }
});
```

`AzureRealtimeClient` stays — it's still needed to inject the `api-key` header
on Azure endpoints.

**Verify:** Build succeeds. Nothing consumes `IRealtimeClient` yet (orchestrator
still uses `RealtimeClient` directly from previous partial migration).

### Step 2 — Orchestrator: Switch to MAF Session

**File:** `AgentOrchestrator.cs`

Change the constructor dependency from `OpenAI.Realtime.RealtimeClient` to
`Microsoft.Extensions.AI.IRealtimeClient`. Replace the field and session types.

#### 2.1 Constructor
```csharp
// Before
private readonly RealtimeClient _realtimeClient;
private RealtimeSessionClient? _sessionClient;

// After
private readonly IRealtimeClient _realtimeFactory;
private IRealtimeClientSession? _session;
```

#### 2.2 StartAsync
Replace `StartConversationSessionAsync` + `ConfigureSessionAsync` with
`CreateSessionAsync(options)`:

```csharp
var options = BuildSessionOptions();
_session = await _realtimeFactory.CreateSessionAsync(options, _cts.Token);
_messageLoop = Task.Run(() => RunMessageLoopAsync(_session, _cts.Token));
```

New `BuildSessionOptions()` method maps `AppSettings` → `RealtimeSessionOptions`
with tools, voice, instructions, VAD.

#### 2.3 Message Loop
Replace `ReceiveUpdatesAsync` + SDK pattern matching with
`GetStreamingResponseAsync` + MAF type dispatch:

```csharp
await foreach (var msg in session.GetStreamingResponseAsync(ct))
{
    switch (msg.Type)
    {
        case var t when t == RealtimeServerMessageType.OutputAudioDelta: ...
        case var t when t == RealtimeServerMessageType.OutputTextDelta: ...
        case var t when t == RealtimeServerMessageType.OutputAudioTranscriptionDone: ...
        case var t when t == RealtimeServerMessageType.OutputTextDone: ...
        case var t when t == RealtimeServerMessageType.InputAudioTranscriptionCompleted: ...
        case var t when t == RealtimeServerMessageType.ResponseOutputItemAdded: ...
        case var t when t == RealtimeServerMessageType.ResponseDone: ...
        case var t when t == SpeechStarted: ...  // custom RealtimeServerMessageType
        case var t when t == RealtimeServerMessageType.Error: ...
    }
}
```

Message data accessed via cast to typed subtypes:
- `OutputTextAudioRealtimeServerMessage` for audio bytes (`.Audio`) and text (`.Text`)
- `InputAudioTranscriptionRealtimeServerMessage` for user transcript
- `ResponseCreatedRealtimeServerMessage` for response done + output items

#### 2.4 Audio Send
Replace `session.SendInputAudioAsync(stream)` with:
```csharp
var audioMsg = new InputAudioBufferAppendRealtimeClientMessage
{
    Content = new DataContent(pcmBytes, "audio/pcm")
};
await _session.SendAsync(audioMsg, ct);
```

#### 2.5 Text Input
Replace `AddItemAsync` + `StartResponseAsync` with MAF message types:
```csharp
await _session.SendAsync(new CreateConversationItemRealtimeClientMessage
{
    Item = new RealtimeConversationItem { ... }
}, ct);
await _session.SendAsync(new CreateResponseRealtimeClientMessage(), ct);
```

#### 2.6 Tool Dispatch
**Still manual.** On `ResponseDone`, iterate `ResponseCreatedRealtimeServerMessage.Items`,
check for function calls via `RawRepresentation` (MAF doesn't expose function call
items as a first-class type — need to cast to SDK's `RealtimeFunctionCallItem`).

Alternatively, handle tool calls on `ResponseOutputItemDone` by checking item type
via `RawRepresentation`.

#### 2.7 Speech Interruption
`SpeechStarted` is NOT a well-known MAF type. Two options:
1. Custom `RealtimeServerMessageType("input_audio_buffer.speech_started")`
2. Check `msg.Type == RealtimeServerMessageType.RawContentOnly` and inspect
   `msg.RawRepresentation`

Use option 1 — cleaner.

### Step 3 — VoiceInputAgent Audio Sink Update

**File:** `VoiceInputAgent.cs`

The audio sink delegate changes from:
```csharp
// Before (raw SDK)
_voiceIn.SetAudioSink(async (pcm, ct) =>
{
    using var ms = new MemoryStream(pcm);
    await _sessionClient!.SendInputAudioAsync(ms, ct);
});
```
To:
```csharp
// After (MAF)
_voiceIn.SetAudioSink(async (pcm, ct) =>
{
    await _session!.SendAsync(
        new InputAudioBufferAppendRealtimeClientMessage
        {
            Content = new DataContent(pcm, "audio/pcm")
        }, ct);
});
```

No changes to `VoiceInputAgent.cs` itself — only the delegate wired in the
orchestrator changes.

### Step 4 — Update StubRealtimeClient for Unit Tests

**File:** `BodyCam.Tests/TestInfrastructure/StubRealtimeClient.cs`

Replace the `OpenAI.Realtime.RealtimeClient` subclass with an
`IRealtimeClient` implementation that returns a stub session.

```csharp
internal sealed class StubRealtimeClient : IRealtimeClient
{
    public Task<IRealtimeClientSession> CreateSessionAsync(
        RealtimeSessionOptions options, CancellationToken ct)
    {
        return Task.FromResult<IRealtimeClientSession>(new StubSession(options));
    }

    public object? GetService(Type serviceType, object? serviceKey) => null;

    private sealed class StubSession : IRealtimeClientSession
    {
        public RealtimeSessionOptions Options { get; }
        public StubSession(RealtimeSessionOptions options) => Options = options;

        public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            yield break;
        }

        public Task SendAsync(RealtimeClientMessage message, CancellationToken ct)
            => Task.CompletedTask;

        public object? GetService(Type serviceType, object? serviceKey) => null;
    }
}
```

No more `BindingFlags.NonPublic` constructor reflection. No more `NullWebSocket`.

### Step 5 — Update RealtimeFixture for Real Tests

**File:** `BodyCam.RealTests/Fixtures/RealtimeFixture.cs`

Replace `BuildClient` to return `IRealtimeClient`:
```csharp
internal static IRealtimeClient BuildClient(string apiKey, AppSettings settings)
{
    if (settings.Provider == OpenAiProvider.Azure)
    {
        var rtOpts = new RealtimeClientOptions
        {
            Endpoint = new Uri($"{settings.AzureEndpoint!.TrimEnd('/')}/openai/v1/realtime")
        };
        var sdkClient = new AzureRealtimeClient(apiKey, rtOpts);
        return new OpenAIRealtimeClient(sdkClient, settings.AzureRealtimeDeploymentName!);
    }
    return new OpenAIRealtimeClient(apiKey, settings.RealtimeModel);
}
```

Update `OrchestratorFixture.cs` to match new orchestrator constructor.

### Step 6 — Delete AzureRealtimeClient (Maybe)

If `OpenAIRealtimeClient(RealtimeClient, model)` constructor handles Azure
auth through the underlying `RealtimeClient` properly, `AzureRealtimeClient`
may still be needed for the `api-key` header. Keep it unless proven
unnecessary.

### Step 7 — Build + Test Verification

```powershell
# Build all targets
dotnet build src/BodyCam/BodyCam.csproj -v q

# Unit tests (383 expected)
dotnet test src/BodyCam.Tests/ -v q

# Real API tests
dotnet test src/BodyCam.RealTests/ -v n
```

All must pass. Pay special attention to:
- Tool call tests (describe_scene triggered via text)
- Audio tests (speech recognition, audio output)
- Reconnection behavior

---

## Risk Assessment

| Risk | Mitigation |
|---|---|
| MAF `GetStreamingResponseAsync` doesn't surface speech events | Use `RawContentOnly` type + `RawRepresentation` fallback |
| Tool call items not exposed in MAF typed messages | Cast `RawRepresentation` to SDK's `RealtimeFunctionCallItem` |
| `OpenAIRealtimeClient(RealtimeClient, model)` doesn't pass `api-key` header correctly | `AzureRealtimeClient` subclass handles this in `StartSessionAsync` |
| Audio format mismatch (MAF uses `DataContent` vs raw PCM) | Set `InputAudioFormat` in `RealtimeSessionOptions`, use `"audio/pcm"` media type |
| Test count drops | Adjust expectations — no test files are being deleted |

---

## What M25 Steps Are Already Done

| M25 Step | Status | Notes |
|---|---|---|
| 01 — DI Registration | **Redo** | Was for raw SDK, needs MAF version |
| 02 — Orchestrator Rewrite | **Redo** | Was partially done for raw SDK typed events |
| 03 — VoiceInputAgent | **Done** | Already on delegate pattern |
| 04 — ToolContext Simplify | **Done** | `RealtimeClient` property already removed |
| 05 — Delete Dead Code | **Done** | All hand-rolled realtime files deleted |
| 06 — Update Tests | **Partial** | StubRealtimeClient needs MAF rewrite |
| 07 — Build Verification | **Redo** | Full verification after migration |

---

## Execution Order

1. **DI registration** (step 1) — register `IRealtimeClient`, build
2. **Orchestrator** (step 2) — rewrite StartAsync/StopAsync/RunMessageLoop, build
3. **Stub tests** (step 4) — update StubRealtimeClient, run unit tests
4. **Real test fixture** (step 5) — update RealtimeFixture, run real tests
5. **Cleanup** (step 6) — evaluate if AzureRealtimeClient can be simplified further
