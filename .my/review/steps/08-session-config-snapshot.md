# Step 8: Snapshot Settings on Session Start

**Priority:** P2 | **Effort:** Small | **Risk:** Settings changed mid-session cause inconsistency

---

## Problem

`AgentOrchestrator.StartAsync` reads settings directly from `ISettingsService` into `AppSettings` at session start. But the user can change settings on the Settings page while a session is running. The next `UpdateSessionAsync` or tool call could use a mix of old and new settings.

## Steps

### 8.1 Create SessionConfig record

**File:** `src/BodyCam/Models/SessionConfig.cs` (new file)

```csharp
namespace BodyCam.Models;

/// <summary>
/// Immutable snapshot of settings captured at session start.
/// Ensures consistent configuration throughout a single session.
/// </summary>
public record SessionConfig
{
    public required string RealtimeModel { get; init; }
    public required string ChatModel { get; init; }
    public required string VisionModel { get; init; }
    public required string TranscriptionModel { get; init; }
    public required string Voice { get; init; }
    public required string TurnDetection { get; init; }
    public required string NoiseReduction { get; init; }
    public required string SystemInstructions { get; init; }
}
```

### 8.2 Add SessionConfig property to AgentOrchestrator

**File:** `src/BodyCam/Orchestration/AgentOrchestrator.cs`

Add property:

```csharp
public SessionConfig? CurrentConfig { get; private set; }
```

### 8.3 Capture snapshot in StartAsync

At the top of `StartAsync`, after the existing settings-copy block, create the snapshot:

```csharp
CurrentConfig = new SessionConfig
{
    RealtimeModel = _settings.RealtimeModel,
    ChatModel = _settings.ChatModel,
    VisionModel = _settings.VisionModel,
    TranscriptionModel = _settings.TranscriptionModel,
    Voice = _settings.Voice,
    TurnDetection = _settings.TurnDetection,
    NoiseReduction = _settings.NoiseReduction,
    SystemInstructions = _settings.SystemInstructions,
};
```

### 8.4 Clear snapshot in StopAsync

At the end of `StopAsync`:

```csharp
CurrentConfig = null;
```

### 8.5 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```

**Note:** This step captures the snapshot. A follow-up would have `RealtimeClient.UpdateSessionAsync` read from `SessionConfig` instead of `AppSettings` to fully close the gap. That's a larger change that can be done later.
