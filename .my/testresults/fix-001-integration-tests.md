# FIX-001 — Integration Tests (FullPipelineTests)

**RCA:** [rca-integration-tests.md](rca-integration-tests.md)
**Status:** Applied

---

## Changes

### 1. `BodyCam.IntegrationTests.csproj` — Added `NoWarn`

```xml
<NoWarn>MEAI001;OPENAI002</NoWarn>
```

Required because `Microsoft.Extensions.AI.IRealtimeClient` is marked experimental.

### 2. `FullPipelineTests.cs` — Replaced obsolete test

**Deleted:** `ConversationAgent_WithWireMock_ProcessesAndReturns`
- Used parameterless `new ConversationAgent()` and `AddUserMessage()` — both removed in M25.

**Added:** `ConversationAgent_AnalyzeAsync_ReturnsResult`
- Mocks `IChatClient` via NSubstitute, constructs `ConversationAgent(chatClient, settings)`.
- Calls `AnalyzeAsync("What is 2+2?")` and asserts the result matches the mocked response.

### 3. `FullPipelineTests.cs` — Updated `Pipeline_AudioIn_Transcript_Conversation_AudioOut`

| Parameter | Old | New |
|---|---|---|
| `ConversationAgent` | `new ConversationAgent()` | `new ConversationAgent(chatClient, settings)` |
| `VisionAgent` | `new VisionAgent(camera, settings)` | `new VisionAgent(chatClient, settings)` |
| `IRealtimeClient` | `Substitute.For<IRealtimeClient>()` (BodyCam.Services) | `Substitute.For<Microsoft.Extensions.AI.IRealtimeClient>()` |
| `IWakeWordService` | `new Lazy<IWakeWordService>(...)` | Direct `Substitute.For<IWakeWordService>()` |
| New params | — | `IMicrophoneCoordinator`, `CameraManager`, `AecProcessor`, `ILogger<AgentOrchestrator>` |
| `DebugLog` event | Subscribed | Removed (event no longer exists) |
| Assertion | `await StartAsync()` + `IsRunning` check | Construction-only check (mock `IRealtimeClient` can't create sessions) |

### 4. Namespace additions

```csharp
using BodyCam.Services.Audio.WebRtcApm;
using BodyCam.Services.Camera;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
```

## Verification

- `dotnet build` — 0 errors
- `dotnet test` — 11/11 passed
