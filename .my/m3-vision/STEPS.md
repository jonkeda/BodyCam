# STEPS — M3 Vision Pipeline

Camera capture, vision model integration, and UI preview.

| Step | Title | Depends On |
|------|-------|------------|
| 1 | [Windows Camera Service (MediaCapture)](STEP-01-WINDOWS-CAMERA.md) | — |
| 2 | [Vision Agent Enhancements](STEP-02-VISION-AGENT.md) | 1 |
| 3 | [Orchestrator Vision Triggers](STEP-03-ORCHESTRATOR-VISION.md) | 2 |
| 4 | [Camera Preview UI](STEP-04-CAMERA-UI.md) | 1 |
| 5 | [Android Camera Service](STEP-05-ANDROID-CAMERA.md) | 1 |
| 6 | [Update Tests](STEP-06-UPDATE-TESTS.md) | 1–5 |
| 7 | [Vision RealTests](STEP-07-REALTESTS.md) | 1–6 |
| 8 | [CameraView Migration (RCA-007)](STEP-08-CAMERA-VIEW-MIGRATION.md) | 1–7 |

## Build & Verify

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 --no-restore -v q
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -v q
```
