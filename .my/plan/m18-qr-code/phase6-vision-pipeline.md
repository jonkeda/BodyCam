# M18 Phase 6 - Vision Pipeline

**Status:** LEGACY / EXPERIMENTAL, NOT REQUIRED FOR PHASE 5
**Depends on:** none for the current M18 scan UI

---

## Current Reality

The repository contains a cascading `VisionPipeline` implementation:

```
src/BodyCam/Services/Vision/IVisionPipelineStage.cs
src/BodyCam/Services/Vision/VisionPipeline.cs
src/BodyCam/Services/Vision/VisionPipelineResult.cs
src/BodyCam/Services/Vision/QrScanStage.cs
src/BodyCam/Services/Vision/TextDetectionStage.cs
src/BodyCam/Services/Vision/SceneDescriptionStage.cs
```

It is also registered in DI:

```
src/BodyCam/ServiceExtensions.cs
```

```csharp
services.AddSingleton<IVisionPipelineStage, QrScanStage>();
services.AddSingleton<IVisionPipelineStage, TextDetectionStage>();
services.AddSingleton<IVisionPipelineStage, SceneDescriptionStage>();
services.AddSingleton<VisionPipeline>();
```

However, the current primary camera commands do **not** use it:

| Feature | Current implementation |
|---------|------------------------|
| `look` tool | delegates to `ICameraCommandService.ExecuteAsync("look")` |
| `LookCommand` | captures a frame and calls `VisionAgent.DescribeFrameAsync` directly |
| `scan_qr_code` tool | delegates to `ICameraCommandService.ExecuteAsync("scan")` |
| `ScanCommand` | captures a frame and calls `IQrCodeScanner.ScanAsync` directly |
| Phase 5 overlay | driven by `TryShowScanResult` and `ScanResultReady`, not pipeline stages |

So this phase should not be treated as required for M18 Phase 5.

---

## What The Pipeline Does

`VisionPipeline` orders registered `IVisionPipelineStage` instances by ascending `Cost` and returns the first non-null result.

Current stages:

| Stage | Cost | Behavior |
|-------|------|----------|
| `QrScanStage` | 0 | Scans the provided JPEG with `IQrCodeScanner`; returns QR/barcode result or null |
| `TextDetectionStage` | 10 | Calls `VisionAgent` with a text-only prompt; returns text or null |
| `SceneDescriptionStage` | 100 | Calls `VisionAgent` for scene description and returns a result |

The design is still useful as an experiment or future routing strategy, but it is not the active `look` implementation.

---

## If This Is Revived Later

A future version can wire `LookCommand` or `LookTool` to `VisionPipeline`, but that change needs explicit decisions:

1. Should `look` be "first useful result wins" or a normal scene description?
2. Should scanning a QR during `look` show the Phase 5 overlay?
3. Should text detection always run before scene description, even when the user did not ask to read?
4. How should manual aim mode pass one captured frame through all stages?
5. How should provider capability checks and analytics from `LookCommand`/`ReadCommand` be preserved?

For the overlay specifically, a pipeline QR hit would need to call the same UI path as scan results:

```
ShowScanResultCard(...)
```

or raise an equivalent event to:

```
AgentOrchestrator.ScanResultReady
```

Current code does not do that.

---

## Tests That Still Cover The Experimental Code

| Area | Current test files |
|------|--------------------|
| Pipeline ordering and first-hit behavior | `src/BodyCam.Tests/Services/VisionPipelineTests.cs` |
| QR stage behavior | `src/BodyCam.Tests/Services/QrScanStageTests.cs` |

Real pipeline tests may still mention the older idea that `look` can route through `QrScanStage`; verify those against current `LookTool` before using them as acceptance criteria.

---

## Recommendation

Keep Phase 6 separate from M18 QR scanning. Phase 5 is complete without it. If the pipeline remains unused, consider removing `AddVisionPipeline()` registration and the unused services in a cleanup milestone, or move them under an explicit experiment flag.
