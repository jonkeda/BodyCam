# M19 Phase 3 — Crash Reporting (Sentry)

**Status:** NOT STARTED  
**Depends on:** M19 Phase 1

---

## Goal

Capture unhandled exceptions with breadcrumbs (last N log entries) and device context via Sentry. Send crash reports for post-mortem analysis.

---

## Why Sentry

| Aspect | Sentry | App Insights (deprecated) |
|--------|--------|---------------------------|
| Status | Active, MAUI-native SDK | Deprecated March 2025 |
| MAUI integration | `Sentry.Maui` — one-liner setup | Manual `UnhandledException` wiring |
| Breadcrumbs | Automatic from ILogger | Manual breadcrumb attachment |
| Source maps / symbols | Built-in dSYM/PDB upload | Separate symbolication setup |
| Free tier | 5K errors/month, 10K perf txns | Requires Azure subscription |
| Offline caching | Built-in envelope caching | No native offline support |

---

## Implementation

### 1. NuGet Package

```xml
<PackageReference Include="Sentry.Maui" Version="4.*" />
```

`Sentry.Maui` automatically:
- Hooks `MauiExceptions.UnhandledException`
- Captures XAML binding errors
- Integrates with `Microsoft.Extensions.Logging` (breadcrumbs from ILogger)
- Adds device context (OS, model, screen size)

### 2. AppSettings Extension

```csharp
public string? SentryDsn { get; set; }
public bool SendCrashReports { get; set; } // default false
```

Persist via `ISettingsService`.

### 3. MauiProgram.cs Configuration

```csharp
builder.UseSentry(options =>
{
    options.Dsn = appSettings.SentryDsn ?? "";
    options.IsGlobalModeEnabled = true;

    // Breadcrumbs from ILogger (Phase 1's InAppLoggerProvider entries)
    options.MinimumBreadcrumbLevel = LogLevel.Information;
    options.MinimumEventLevel = LogLevel.Error;

    // Privacy
    options.SendDefaultPii = false;
    options.SetBeforeSend((sentryEvent, hint) =>
    {
        // Strip any sensitive data from exception messages
        if (sentryEvent.Message?.Formatted?.Contains("sk-", StringComparison.Ordinal) == true)
        {
            sentryEvent.Message = new SentryMessage { Formatted = "[redacted - contained API key]" };
        }

        // Strip sensitive breadcrumb messages
        foreach (var crumb in sentryEvent.Breadcrumbs)
        {
            if (crumb.Message?.Contains("transcript", StringComparison.OrdinalIgnoreCase) == true)
            {
                crumb.Message = "[redacted]";
            }
        }

        return sentryEvent;
    });

    // Environment
    options.Environment = 
#if DEBUG
        "development";
#else
        "production";
#endif

    // Release tracking
    options.Release = $"bodycam@{AppInfo.VersionString}";

    // Performance (optional — disable if not needed)
    options.TracesSampleRate = 0; // no perf tracing, just crashes
    
    // Only enable if user opted in
    options.IsEnabled = appSettings.SendCrashReports 
                        && !string.IsNullOrEmpty(appSettings.SentryDsn);
});
```

### 4. Crash Context (Automatic via Sentry.Maui)

Sentry automatically captures:

| Property | Source |
|----------|--------|
| `device.family` | Device model |
| `os.name` + `os.version` | OS info |
| `app.version` | App version |
| `app.start_time` | Session duration |
| `gpu`, `screen_resolution` | Hardware info |
| Breadcrumbs | Last N ILogger entries |
| Exception chain | Full stack trace with inner exceptions |

### 5. Custom Context Tags

Add session-specific tags for filtering in Sentry dashboard:

```csharp
SentrySdk.ConfigureScope(scope =>
{
    scope.SetTag("audio.input", audioInputManager.ActiveProvider?.DisplayName ?? "none");
    scope.SetTag("audio.output", audioOutputManager.ActiveProvider?.DisplayName ?? "none");
    scope.SetTag("camera", cameraManager.ActiveProvider?.DisplayName ?? "none");
    scope.SetTag("session.active", orchestrator.IsRunning.ToString());
    scope.SetTag("provider", settingsService.Provider);
});
```

Call this when session state changes (start/stop, provider switch).

### 6. Manual Error Capture

For caught exceptions that should still be reported:

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogError(ex, "WebSocket connection failed");
    SentrySdk.CaptureException(ex);
}
```

Use sparingly — only for errors that indicate bugs, not expected failures.

### 7. Offline Caching

Sentry SDK has built-in envelope caching. If the device is offline when a crash
occurs, the crash report is stored locally and sent on next app launch.

```csharp
options.CacheDirectoryPath = FileSystem.CacheDirectory;
```

### 8. Settings UI

Add to `SettingsPage.xaml` in Debug section:

```xml
<Label Text="Send crash reports" VerticalOptions="Center" />
<Switch IsToggled="{Binding SendCrashReports}" />

<Label Text="Sentry DSN" FontSize="Small" TextColor="Gray" />
<Entry Text="{Binding SentryDsn}"
       Placeholder="https://xxx@sentry.io/yyy"
       IsPassword="False" />
```

Note: "Crash reports include device info and recent log entries. No transcripts or API keys."

### 9. Tests

| Test | Asserts |
|------|---------|
| `BeforeSend` strips API key patterns | Event message redacted |
| `BeforeSend` strips transcript breadcrumbs | Breadcrumb message redacted |
| Disabled when DSN empty | `IsEnabled` returns false |
| Disabled when opt-in false | `IsEnabled` returns false |
| Custom tags set on scope | Tags present after `ConfigureScope` |

---

## Sentry Project Setup

1. Create Sentry project at https://sentry.io (free tier: 5K errors/month)
2. Copy DSN from project settings
3. Configure alert rules: email on new issues, Slack webhook for P0
4. Upload PDB symbols for Windows builds (`sentry-cli upload-dif`)
5. Upload dSYM for iOS builds (Phase 5)

---

## Exit Criteria

1. Unhandled exceptions appear in Sentry with full stack trace
2. Breadcrumbs show last N log entries before crash
3. Device context (OS, model, version) attached to every event
4. `BeforeSend` strips API keys and transcript content
5. Offline crashes cached and sent on next launch
6. Off by default — user must explicitly enable
7. Zero dependency on deprecated Application Insights SDK
