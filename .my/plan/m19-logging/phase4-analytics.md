# M19 Phase 4 — Usage Analytics

**Status:** NOT STARTED  
**Depends on:** M19 Phase 2 (App Insights SDK)

---

## Goal

Track feature usage with anonymous custom events for product decisions. Opt-in only, no PII.

---

## Events

| Event Name | Properties | When |
|-----------|-----------|------|
| `SessionStarted` | `model`, `voice`, `platform` | Realtime session begins |
| `SessionEnded` | `duration_seconds`, `tool_calls`, `interruptions` | Session stops |
| `ToolExecuted` | `tool_name`, `duration_ms`, `success` | Any tool call completes |
| `VisionCaptured` | `source` (phone/glasses), `duration_ms` | Frame captured + described |
| `QrScanned` | `format`, `content_type`, `success` | QR/barcode decoded |
| `WakeWordDetected` | `keyword`, `action` | Wake word triggers |
| `ProviderSwitched` | `type` (audio_in/audio_out/camera), `from`, `to` | Hot-plug or manual switch |
| `ErrorOccurred` | `category`, `message_hash` | Error logged (hashed, not raw) |

---

## Implementation

### 1. IAnalyticsService Interface

```csharp
public interface IAnalyticsService
{
    void TrackEvent(string name, IDictionary<string, string>? properties = null);
    void TrackMetric(string name, double value);
    bool IsEnabled { get; }
}
```

### 2. AppInsightsAnalyticsService

```csharp
public class AppInsightsAnalyticsService : IAnalyticsService
{
    private readonly TelemetryClient? _client;
    private readonly ISettingsService _settings;

    public bool IsEnabled => _settings.SendUsageData && _client is not null;

    public void TrackEvent(string name, IDictionary<string, string>? properties = null)
    {
        if (!IsEnabled) return;
        _client!.TrackEvent(name, properties);
    }
}
```

### 3. NullAnalyticsService (Testing/Disabled)

Returns `IsEnabled = false`, no-ops all calls.

### 4. Instrumentation Points

Add `_analytics.TrackEvent(...)` calls in:
- `AgentOrchestrator.StartAsync` / `StopAsync`
- `ToolDispatcher.ExecuteAsync`
- `VisionAgent.DescribeFrameAsync`
- `QrCodeService` (M18)
- `PorcupineWakeWordService.OnWakeWordDetected`
- `AudioInputManager.SetActiveAsync` / `AudioOutputManager.SetActiveAsync`

### 5. Settings

- `SendUsageData` bool in `ISettingsService` (default: `false`)
- Settings UI toggle: "Send anonymous usage data"
- Description: "Helps us understand which features you use. No personal data is collected."

### 6. Tests

| Test | Asserts |
|------|---------|
| Disabled by default | No events tracked |
| Events include correct properties | Property keys present |
| No PII in event properties | Content/keys excluded |

---

## Exit Criteria

1. Feature usage events appear in App Insights when opt-in enabled
2. No PII or content in any event
3. Off by default
4. `NullAnalyticsService` for tests and disabled state
