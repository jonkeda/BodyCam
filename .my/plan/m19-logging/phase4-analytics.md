# M19 Phase 4 — Usage Analytics (OpenTelemetry Metrics & Traces)

**Status:** NOT STARTED  
**Depends on:** M19 Phase 2 (OpenTelemetry + Azure Monitor)

---

## Goal

Track feature usage with anonymous custom events and metrics via OpenTelemetry for product decisions. Opt-in only, no PII. Data flows to Azure Monitor via the exporter configured in Phase 2.

---

## Events (via ActivitySource)

Use `System.Diagnostics.ActivitySource` for distributed tracing / custom events:

```csharp
private static readonly ActivitySource Source = new("BodyCam.Analytics");
```

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

## Metrics (via System.Diagnostics.Metrics)

Use `System.Diagnostics.Metrics.Meter` for numeric counters and histograms:

```csharp
private static readonly Meter Meter = new("BodyCam.Analytics");

private static readonly Counter<long> SessionCount = Meter.CreateCounter<long>("bodycam.sessions.count");
private static readonly Counter<long> ToolCallCount = Meter.CreateCounter<long>("bodycam.tools.count");
private static readonly Counter<long> ErrorCount = Meter.CreateCounter<long>("bodycam.errors.count");
private static readonly Histogram<double> SessionDuration = Meter.CreateHistogram<double>("bodycam.sessions.duration_seconds");
private static readonly Histogram<double> ToolLatency = Meter.CreateHistogram<double>("bodycam.tools.duration_ms");
```

---

## Implementation

### 1. IAnalyticsService Interface

```csharp
public interface IAnalyticsService
{
    void TrackEvent(string name, IDictionary<string, string>? properties = null);
    void TrackMetric(string name, double value, IDictionary<string, string>? tags = null);
    bool IsEnabled { get; }
}
```

### 2. OpenTelemetryAnalyticsService

```csharp
public class OpenTelemetryAnalyticsService : IAnalyticsService
{
    private static readonly ActivitySource Source = new("BodyCam.Analytics");
    private static readonly Meter Meter = new("BodyCam.Analytics");
    private static readonly Counter<long> EventCounter = Meter.CreateCounter<long>("bodycam.events");

    private readonly ISettingsService _settings;

    public bool IsEnabled => _settings.SendUsageData;

    public void TrackEvent(string name, IDictionary<string, string>? properties = null)
    {
        if (!IsEnabled) return;

        using var activity = Source.StartActivity(name, ActivityKind.Internal);
        if (activity is not null && properties is not null)
        {
            foreach (var (key, value) in properties)
                activity.SetTag(key, value);
        }

        EventCounter.Add(1, new KeyValuePair<string, object?>("event.name", name));
    }

    public void TrackMetric(string name, double value, IDictionary<string, string>? tags = null)
    {
        if (!IsEnabled) return;
        // Use specific histograms/counters registered in the constructor
        // or a generic approach with ObservableGauge
    }
}
```

### 3. NullAnalyticsService (Testing/Disabled)

```csharp
public class NullAnalyticsService : IAnalyticsService
{
    public bool IsEnabled => false;
    public void TrackEvent(string name, IDictionary<string, string>? properties = null) { }
    public void TrackMetric(string name, double value, IDictionary<string, string>? tags = null) { }
}
```

### 4. OpenTelemetry Registration (extends Phase 2)

In `MauiProgram.cs`, add the analytics sources to the existing OTel setup:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("BodyCam.Analytics");
        tracing.AddAzureMonitorTraceExporter(options =>
        {
            options.ConnectionString = appSettings.AzureMonitorConnectionString;
        });
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("BodyCam.Analytics");
        metrics.AddAzureMonitorMetricExporter(options =>
        {
            options.ConnectionString = appSettings.AzureMonitorConnectionString;
        });
    });
```

### 5. Instrumentation Points

Add `_analytics.TrackEvent(...)` calls in:

| Location | Event |
|----------|-------|
| `AgentOrchestrator.StartAsync` | `SessionStarted` |
| `AgentOrchestrator.StopAsync` | `SessionEnded` + duration metric |
| `ToolDispatcher.ExecuteAsync` | `ToolExecuted` + latency metric |
| `VisionAgent.DescribeFrameAsync` | `VisionCaptured` |
| `QrCodeService` (M18) | `QrScanned` |
| `PorcupineWakeWordService.OnWakeWordDetected` | `WakeWordDetected` |
| `AudioInputManager.SetActiveAsync` | `ProviderSwitched` |
| `AudioOutputManager.SetActiveAsync` | `ProviderSwitched` |
| Error logging (ILogger integration) | `ErrorOccurred` |

### 6. DI Registration

```csharp
if (appSettings.SendUsageData && !string.IsNullOrEmpty(appSettings.AzureMonitorConnectionString))
    services.AddSingleton<IAnalyticsService, OpenTelemetryAnalyticsService>();
else
    services.AddSingleton<IAnalyticsService, NullAnalyticsService>();
```

### 7. Settings

- `SendUsageData` bool in `ISettingsService` (default: `false`)
- Settings UI toggle: "Send anonymous usage data"
- Description: "Helps us understand which features you use. No personal data is collected."

### 8. Dashboard

Azure Monitor workbook or Grafana dashboard showing:
- Sessions per day
- Tool usage distribution (bar chart)
- Error rate trend
- Average session duration
- Provider popularity

Setup guide in `docs/analytics-dashboard.md` (future).

### 9. Tests

| Test | Asserts |
|------|---------|
| Disabled by default | No events tracked, `IsEnabled == false` |
| Events include correct properties | Property keys present on Activity tags |
| No PII in event properties | Content/keys excluded |
| `NullAnalyticsService` no-ops | No exceptions, no side effects |
| Metrics increment correctly | Counter/histogram values increase |

---

## Exit Criteria

1. Feature usage events appear in Azure Monitor when opt-in enabled
2. Metrics (session count, tool latency) visible in Azure Monitor Metrics
3. No PII or content in any event or metric tag
4. Off by default
5. `NullAnalyticsService` for tests and disabled state
6. Zero dependency on deprecated Application Insights `TelemetryClient`
