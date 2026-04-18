# M19 Phase 2 — Remote Sink (Azure Application Insights)

**Status:** NOT STARTED  
**Depends on:** M19 Phase 1

---

## Goal

Send structured logs (Warning+) to Azure Application Insights for post-crash diagnostics and operational visibility. Opt-in only, privacy-safe.

---

## Implementation

### 1. NuGet Package

```xml
<PackageReference Include="Microsoft.ApplicationInsights.WorkerService" Version="2.*" />
```

Or if using Serilog:
```xml
<PackageReference Include="Serilog.Sinks.ApplicationInsights" Version="4.*" />
```

Decision: Use the direct MEL integration (`AddApplicationInsightsTelemetryWorkerService`) since we're already on MEL from Phase 1. No Serilog dependency needed.

### 2. AppSettings Extension

```csharp
public string? AppInsightsConnectionString { get; set; }
public bool SendDiagnosticData { get; set; } // default false
```

Persist via `ISettingsService`.

### 3. MauiProgram.cs Configuration

```csharp
if (!string.IsNullOrEmpty(appSettings.AppInsightsConnectionString) && appSettings.SendDiagnosticData)
{
    builder.Logging.AddApplicationInsights(
        config => config.ConnectionString = appSettings.AppInsightsConnectionString,
        options => options.IncludeScopes = true);
    
    builder.Logging.AddFilter<ApplicationInsightsLoggerProvider>("", LogLevel.Warning);
}
```

### 4. Structured Properties (TelemetryInitializer)

```csharp
public class BodyCamTelemetryInitializer : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        telemetry.Context.Session.Id = _sessionId;
        telemetry.Context.Device.OperatingSystem = DeviceInfo.Platform.ToString();
        telemetry.Context.Component.Version = AppInfo.VersionString;
        // NEVER add: API keys, transcript text, audio data
    }
}
```

### 5. Privacy Filter

Create a custom `ITelemetryProcessor` that strips any property containing sensitive patterns (API keys, transcript content):

```csharp
public class PrivacyTelemetryProcessor : ITelemetryProcessor
{
    public void Process(ITelemetry item)
    {
        if (item is ISupportProperties props)
        {
            // Remove any property that looks like an API key
            var keysToRemove = props.Properties.Keys
                .Where(k => k.Contains("key", StringComparison.OrdinalIgnoreCase) 
                         || k.Contains("secret", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var key in keysToRemove)
                props.Properties.Remove(key);
        }
        _next.Process(item);
    }
}
```

### 6. Settings UI

Add to `SettingsPage.xaml`:
- Toggle: "Send diagnostic data" (bound to `SendDiagnosticData`)
- Text field: "App Insights connection string" (bound to `AppInsightsConnectionString`)
- Note: "Only warnings and errors are sent. No transcripts or API keys."

### 7. Tests

| Test | Asserts |
|------|---------|
| Privacy filter strips key-like properties | Properties removed |
| Telemetry initializer adds session/platform | Properties present |
| Disabled by default | No telemetry sent when opt-in false |

---

## Exit Criteria

1. Warning+ logs appear in App Insights when opt-in enabled
2. Session correlation via SessionId
3. No PII or sensitive data in remote telemetry
4. Off by default — user must explicitly enable
