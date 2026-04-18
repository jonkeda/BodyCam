# M19 Phase 2 — Remote Sink (OpenTelemetry + Azure Monitor)

**Status:** NOT STARTED  
**Depends on:** M19 Phase 1

---

## Goal

Send structured logs (Warning+) to Azure Monitor via OpenTelemetry for post-crash diagnostics and operational visibility. Opt-in only, privacy-safe.

Application Insights SDK is deprecated — use the OpenTelemetry-based Azure Monitor Exporter instead.

---

## Implementation

### 1. NuGet Packages

```xml
<PackageReference Include="OpenTelemetry" Version="1.*" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
<PackageReference Include="Azure.Monitor.OpenTelemetry.Exporter" Version="1.*" />
```

### 2. AppSettings Extension

```csharp
public string? AzureMonitorConnectionString { get; set; }
public bool SendDiagnosticData { get; set; } // default false
```

Persist via `ISettingsService`.

### 3. MauiProgram.cs Configuration

```csharp
if (!string.IsNullOrEmpty(appSettings.AzureMonitorConnectionString) && appSettings.SendDiagnosticData)
{
    builder.Logging.AddOpenTelemetry(otel =>
    {
        otel.SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService("BodyCam", serviceVersion: AppInfo.VersionString)
            .AddAttributes(new Dictionary<string, object>
            {
                ["device.platform"] = DeviceInfo.Platform.ToString(),
                ["device.model"] = DeviceInfo.Model,
                ["os.version"] = DeviceInfo.VersionString,
                ["session.id"] = Guid.NewGuid().ToString("N")[..12]
            }));

        otel.AddAzureMonitorLogExporter(options =>
        {
            options.ConnectionString = appSettings.AzureMonitorConnectionString;
        });
    });

    // Filter: only Warning+ goes to OpenTelemetry
    builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("", LogLevel.Warning);
}
```

### 4. Tracing (Optional — for latency visibility)

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("BodyCam.*");
        tracing.AddHttpClientInstrumentation();
        tracing.AddAzureMonitorTraceExporter(options =>
        {
            options.ConnectionString = appSettings.AzureMonitorConnectionString;
        });
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("BodyCam.*");
        metrics.AddAzureMonitorMetricExporter(options =>
        {
            options.ConnectionString = appSettings.AzureMonitorConnectionString;
        });
    });
```

This is optional for Phase 2 — can be deferred to Phase 4 (Analytics) when we
add custom metrics. Include here if latency tracing is wanted early.

### 5. Privacy — Logging Discipline

Rather than post-processing telemetry, enforce at the call site. Since the remote
filter is `Warning+`, `Debug`/`Information` level messages stay local:

```csharp
// Good — safe for remote (Warning+)
_logger.LogWarning("WebSocket reconnect failed after {Attempts} attempts", attempts);

// Safe — stays local (Debug level, never exported)
_logger.LogDebug("User said: {Transcript}", transcript);
```

Rules:
- `Warning` / `Error` / `Critical` — no transcript text, no API keys, no audio
- `Debug` / `Information` — local only, can include operational detail
- Never log raw API keys at any level

### 6. Settings UI

Add to `SettingsPage.xaml` in Debug section:

```xml
<Label Text="Send diagnostic data" VerticalOptions="Center" />
<Switch IsToggled="{Binding SendDiagnosticData}" />

<Label Text="Azure Monitor Connection String" FontSize="Small" TextColor="Gray" />
<Entry Text="{Binding AzureMonitorConnectionString}"
       Placeholder="InstrumentationKey=..." />
```

Note displayed: "Only warnings and errors are sent. No transcripts or API keys."

### 7. Why OpenTelemetry over App Insights SDK

| Aspect | App Insights SDK (deprecated) | OpenTelemetry + Azure Monitor |
|--------|-------------------------------|-------------------------------|
| Status | Deprecated March 2025 | Active, recommended replacement |
| API | `TelemetryClient`, `ITelemetryInitializer` | Standard OTel `ILogger`, `ActivitySource`, `Meter` |
| Vendor lock-in | Azure only | Vendor-neutral (swap exporter for Jaeger, OTLP, etc.) |
| MEL integration | `AddApplicationInsights()` | `AddOpenTelemetry()` |
| .NET MAUI support | Partial (WorkerService package) | Full via `Azure.Monitor.OpenTelemetry.Exporter` |

### 8. Tests

| Test | Asserts |
|------|---------|
| Sensitive data not in Warning+ logs | Log templates at Warning+ don't include transcripts/keys |
| Resource attributes include session/platform | Attributes present on exported logs |
| Disabled by default | No exporter configured when opt-in false |
| Connection string empty → no crash | Graceful no-op |

---

## Exit Criteria

1. Warning+ logs appear in Azure Monitor (Log Analytics workspace) when opt-in enabled
2. Resource attributes include SessionId, Platform, AppVersion
3. No PII or sensitive data in remote telemetry
4. Off by default — user must explicitly enable
5. Zero dependency on deprecated Application Insights SDK
