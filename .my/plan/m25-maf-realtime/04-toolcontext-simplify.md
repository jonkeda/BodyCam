# Step 04 — Simplify ToolContext

Remove the unused `RealtimeClient` property from `ToolContext`. No tool accesses it — confirmed by audit.

**Depends on:** Step 02 (orchestrator no longer passes `_realtime` to `CreateToolContext()`)  
**Touches:** `Tools/ToolContext.cs`, `Orchestration/AgentOrchestrator.cs`  
**Tests affected:** 18 test files (updated in step 06)

---

## What to Do

### 4.1 — Remove `RealtimeClient` property

```
src/BodyCam/Tools/ToolContext.cs
```

**Current:**
```csharp
using BodyCam.Models;
using BodyCam.Services;

namespace BodyCam.Tools;

public sealed class ToolContext
{
    public required Func<CancellationToken, Task<byte[]?>> CaptureFrame { get; init; }
    public required SessionContext Session { get; init; }
    public required Action<string> Log { get; init; }
    public required IRealtimeClient RealtimeClient { get; init; }
}
```

**Replace with:**
```csharp
using BodyCam.Models;

namespace BodyCam.Tools;

public sealed class ToolContext
{
    public required Func<CancellationToken, Task<byte[]?>> CaptureFrame { get; init; }
    public required SessionContext Session { get; init; }
    public required Action<string> Log { get; init; }
}
```

**Changes:**
- Remove `using BodyCam.Services;` (no longer needed)
- Remove `public required IRealtimeClient RealtimeClient { get; init; }` line

### 4.2 — Verify `CreateToolContext()` in AgentOrchestrator

Already updated in step 02.10 — confirm the initializer no longer sets `RealtimeClient`:

```csharp
private ToolContext CreateToolContext() => new()
{
    CaptureFrame = _cameraManager.CaptureFrameAsync,
    Session = Session,
    Log = msg => _logger.LogInformation("{ToolMessage}", msg),
};
```

---

## Why This Is Safe

Grep for `context.RealtimeClient` or `RealtimeClient` usage in any tool file:

```
Tools/DescribeSceneTool.cs    — uses context.CaptureFrame, context.Session, context.Log
Tools/DeepAnalysisTool.cs     — uses context.CaptureFrame, context.Session
Tools/ReadTextTool.cs         — uses context.CaptureFrame, context.Session
Tools/TakePhotoTool.cs        — uses context.CaptureFrame
Tools/FindObjectTool.cs       — uses context.CaptureFrame, context.Session
Tools/StartSceneWatchTool.cs  — uses context.CaptureFrame, context.Session, context.Log
Tools/SaveMemoryTool.cs       — uses context.Session
Tools/RecallMemoryTool.cs     — uses context.Session
Tools/NavigateToTool.cs       — no context usage
Tools/MakePhoneCallTool.cs    — no context usage
Tools/SendMessageTool.cs      — no context usage
Tools/LookupAddressTool.cs    — no context usage
Tools/SetTranslationModeTool.cs — uses context.Session
```

**Zero tools use `context.RealtimeClient`.**

---

## Acceptance Criteria

- [ ] `ToolContext` no longer has `RealtimeClient` property
- [ ] `using BodyCam.Services` removed from `ToolContext.cs`
- [ ] `CreateToolContext()` no longer sets `RealtimeClient`
- [ ] No tool code references `context.RealtimeClient`
- [ ] Build compiles (tests will fail until step 06 updates them)
