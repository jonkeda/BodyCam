# RCA — Integration Tests Build Failure (FullPipelineTests)

**Date:** 2026-04-19
**Project:** `BodyCam.IntegrationTests`
**Symptom:** 7 build errors in `Orchestration/FullPipelineTests.cs`

---

## Root Cause

The M25 MAF (Microsoft.Extensions.AI) Realtime migration changed several production APIs that `FullPipelineTests.cs` depends on, but the integration tests were never updated to match.

### Specific Breaking Changes

| What Changed | Old API | New API |
|---|---|---|
| `ConversationAgent` constructor | Parameterless `new ConversationAgent()` | `new ConversationAgent(IChatClient, AppSettings)` |
| `ConversationAgent.AddUserMessage()` | Existed — added user message to session | Removed — agent now only has `AnalyzeAsync()` |
| `IRealtimeClient` | `BodyCam.Services.IRealtimeClient` (custom) | `Microsoft.Extensions.AI.IRealtimeClient` (MAF) |
| `AgentOrchestrator` constructor | 9 params (ended with `Lazy<IWakeWordService>`) | 13 params — added `IMicrophoneCoordinator`, `CameraManager`, `AecProcessor`, `ILogger<AgentOrchestrator>` |
| `AgentOrchestrator.DebugLog` event | Existed | Removed |
| `VisionAgent` constructor | `(ICameraService, AppSettings)` | `(IChatClient, AppSettings)` |

### Affected Tests

1. **`ConversationAgent_WithWireMock_ProcessesAndReturns`** — Uses old parameterless constructor and `AddUserMessage()`. The test's intent (validating a pipeline integration point) is obsolete since `ConversationAgent` is now a deep-analysis agent, not a message router.

2. **`Pipeline_AudioIn_Transcript_Conversation_AudioOut`** — Uses old `IRealtimeClient`, old constructors for `ConversationAgent` and `VisionAgent`, old `AgentOrchestrator` constructor missing 4 params, and subscribes to removed `DebugLog` event.

## Fix

1. **Delete `ConversationAgent_WithWireMock_ProcessesAndReturns`** — The test validates behavior that no longer exists. `ConversationAgent` is now a chat-completions deep-analysis agent; a new test for `AnalyzeAsync()` already exists in `BodyCam.Tests`.

2. **Update `Pipeline_AudioIn_Transcript_Conversation_AudioOut`** — Rewrite to use current constructor signatures:
   - Mock `IChatClient` for `ConversationAgent` and `VisionAgent`
   - Mock `Microsoft.Extensions.AI.IRealtimeClient` instead of old `BodyCam.Services.IRealtimeClient`
   - Add mocks for `IMicrophoneCoordinator`, `CameraManager`, `AecProcessor`, `ILogger<AgentOrchestrator>`
   - Remove `DebugLog` subscription (event no longer exists)
   - Change `IWakeWordService` from `Lazy<>` wrapper to direct injection
