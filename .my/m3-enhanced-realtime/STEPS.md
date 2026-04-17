# STEPS — M3 Enhanced Realtime

Single-pipeline Realtime API with native function calling. Removes Mode B.

| Step | Title | Depends On |
|------|-------|------------|
| 1 | [Remove Mode B Infrastructure](STEP-01-REMOVE-MODE-B.md) | — |
| 2 | [Add Function Calling to RealtimeClient](STEP-02-FUNCTION-CALLING.md) | 1 |
| 3 | [Wire Function Dispatch in AgentOrchestrator](STEP-03-FUNCTION-DISPATCH.md) | 2 |
| 4 | [Repurpose ConversationAgent for Deep Analysis](STEP-04-CONVERSATION-AGENT.md) | 1 |
| 5 | [Implement VisionAgent](STEP-05-VISION-AGENT.md) | 2 |
| 6 | [Update Tests](STEP-06-UPDATE-TESTS.md) | 1–5 |

## Build & Verify

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore -v q
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -v q
```
