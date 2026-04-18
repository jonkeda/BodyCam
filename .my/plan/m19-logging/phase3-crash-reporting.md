# M19 Phase 3 — Crash Reporting

**Status:** NOT STARTED  
**Depends on:** M19 Phase 2 (App Insights SDK)

---

## Goal

Capture unhandled exceptions with breadcrumbs (last N log entries) and device context. Send to App Insights for post-mortem analysis.

---

## Implementation

### 1. Unhandled Exception Handler

```csharp
// MauiProgram.cs or App.xaml.cs
MauiExceptions.UnhandledException += (sender, args) =>
{
    var logger = services.GetRequiredService<ILogger<App>>();
    logger.LogCritical(args.ExceptionObject as Exception, "Unhandled exception");
    
    // Flush telemetry before crash
    var telemetry = services.GetService<TelemetryClient>();
    telemetry?.Flush();
    Task.Delay(1000).Wait(); // allow flush to complete
};
```

### 2. Breadcrumb Trail

`InAppLogSink` already keeps last 500 entries. On crash:
- Read last 20 entries from ring buffer
- Attach as custom properties to the crash telemetry
- Format: `breadcrumb_0` through `breadcrumb_19` with timestamp + level + message

### 3. Crash Context Properties

| Property | Source |
|----------|--------|
| `device_platform` | `DeviceInfo.Platform` |
| `device_model` | `DeviceInfo.Model` |
| `os_version` | `DeviceInfo.VersionString` |
| `app_version` | `AppInfo.VersionString` |
| `session_duration` | Time since app start |
| `is_session_active` | `AgentOrchestrator.IsRunning` |
| `active_audio_provider` | Current mic/speaker provider ID |
| `active_camera_provider` | Current camera provider ID |

### 4. Tests

| Test | Asserts |
|------|---------|
| Breadcrumb extraction from sink | Last N entries captured correctly |
| Crash context includes required fields | All properties present |
| No sensitive data in crash report | Keys/transcripts excluded |

---

## Exit Criteria

1. Unhandled exceptions logged as Critical with breadcrumbs
2. Crash appears in App Insights with full context
3. Telemetry flushed before process exit
4. No PII in crash reports
