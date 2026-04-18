# M19 — Logging, Crash Reporting & Analytics

**Status:** PLANNING  
**Goal:** Replace ad-hoc `DebugLog` string concatenation with structured logging (Microsoft.Extensions.Logging), persist logs to remote sinks, add crash reporting, and lay groundwork for usage analytics.

**Depends on:** None (cross-cutting, can be done at any time).

---

## Why This Matters

The current logging system is a `string` property on `MainViewModel` that grows without bound, has no log levels, no structure, no persistence, and is lost on app restart. The `AgentOrchestrator` fires ~20 `DebugLog?.Invoke(this, "message")` calls — all unleveled, all unstructured, all gone the moment the app closes.

**Current problems:**
1. **No persistence** — logs lost on restart, can't diagnose post-crash issues
2. **No log levels** — "Connected" and "Playback error" have equal weight
3. **No structured data** — just string interpolation, impossible to query
4. **No crash reporting** — unhandled exceptions vanish silently
5. **No analytics** — no visibility into feature usage, error rates, or performance
6. **Unbounded growth** — `DebugLog += msg` allocates new strings forever (memory leak)

---

## Current State

```
AgentOrchestrator.DebugLog event (EventHandler<string>)
  └─ MainViewModel subscribes → DebugLog += $"[{time}] {msg}\n"
     └─ Bound to Label in debug overlay (MainPage.xaml)
```

~29 call sites in `AgentOrchestrator` emit plain strings via `DebugLog?.Invoke()`.  
`MainViewModel` has 2 additional direct `DebugLog +=` calls for errors.

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│  Application Code                                │
│  AgentOrchestrator, Services, ViewModels          │
│  └─ ILogger<T> injected via DI                   │
├─────────────────────────────────────────────────┤
│  Microsoft.Extensions.Logging                     │
│  ├─ Log levels: Trace, Debug, Info, Warn, Error  │
│  ├─ Structured: LogInformation("{Event}", data)  │
│  └─ Scopes: BeginScope("Session {Id}", sid)      │
├─────────────────────────────────────────────────┤
│  Sinks (ILoggerProvider)                          │
│  ├─ Debug Console (in-app overlay — replaces     │
│  │   current DebugLog string)                     │
│  ├─ Remote: Azure App Insights / Seq             │
│  └─ (Phase 3) Crash: Sentry / AppCenter         │
├─────────────────────────────────────────────────┤
│  Analytics (Phase 4)                              │
│  └─ Custom events via TelemetryClient            │
└─────────────────────────────────────────────────┘
```

### Key Design Decisions

1. **Use `Microsoft.Extensions.Logging`** (MEL) — already in MAUI, no new dependency for the core abstraction
2. **Inject `ILogger<T>`** everywhere — standard .NET pattern, testable, mockable
3. **Keep the debug overlay** — wire a custom `ILoggerProvider` that feeds the in-app UI (replaces DebugLog string)
4. **Remote sink via Serilog or App Insights SDK** — configurable, off by default
5. **No logging of sensitive data** — API keys, audio content, user speech transcripts excluded from remote sinks

---

## Phases

### Phase 1: Core ILogger Integration

Replace `DebugLog` event with `ILogger<T>` throughout the codebase.

- Add `ILogger<AgentOrchestrator>` injection, replace all `DebugLog?.Invoke()` calls
- Add `ILogger<T>` to services that currently have no logging
- Create `InAppLoggerProvider` — custom `ILoggerProvider` that buffers last N entries for the debug overlay
- Wire `InAppLoggerProvider` into MAUI logging pipeline
- Update `MainViewModel` to consume `InAppLoggerProvider` instead of event subscription
- Remove `AgentOrchestrator.DebugLog` event

### Phase 2: Remote Sink (Azure App Insights)

Send logs to a remote service for post-crash analysis.

- Add Azure Application Insights SDK (or Serilog + Serilog.Sinks.ApplicationInsights)
- Configure connection string via `AppSettings` / `ISettingsService`
- Filter: only Warning+ to remote by default (configurable)
- Structured properties: SessionId, DevicePlatform, AppVersion
- Privacy: exclude transcript text, API keys from remote telemetry
- Settings toggle: "Send diagnostic data" (opt-in, off by default)

### Phase 3: Crash Reporting

Capture unhandled exceptions with context.

- Configure `MauiExceptions.UnhandledException` handler
- Log crash with full stack trace + last N log entries as breadcrumbs
- Send to App Insights (or Sentry if preferred)
- Include: device info, OS version, app version, session duration
- Exclude: user content, API keys, audio data

### Phase 4: Usage Analytics

Track feature usage for product decisions.

- Custom events: "SessionStarted", "ToolExecuted", "QrScanned", "VisionCaptured"
- Metrics: session duration, tool call count, error rate
- Privacy: no PII, no content, only event names + counts
- Settings toggle: "Send usage data" (opt-in, off by default)
- Dashboard setup guide (App Insights workbook or Seq dashboard)

### Phase 5: iOS Platform Support

- Verify App Insights SDK on iOS
- iOS crash symbolication setup
- Background logging with iOS lifecycle constraints

---

## Integration Points

| System | Change |
|--------|--------|
| **AgentOrchestrator** | `DebugLog` event → `ILogger<AgentOrchestrator>` |
| **RealtimeClient** | Add `ILogger<RealtimeClient>` for WebSocket lifecycle |
| **AudioInputManager / OutputManager** | Log provider switches, hot-plug events |
| **CameraManager** | Log capture attempts, failures, provider switches |
| **ToolDispatcher** | Log tool execution (name, duration, success/fail) |
| **MainViewModel** | Consume `InAppLoggerProvider` for debug overlay |
| **MauiProgram.cs** | Configure logging pipeline + providers |
| **AppSettings** | Remote logging toggle, connection string |
| **SettingsViewModel** | UI for logging preferences |

---

## Privacy Requirements

| Data | In-App | Remote | Justification |
|------|--------|--------|---------------|
| Log messages (info/warn/error) | Yes | Warning+ only | Diagnostics |
| Session ID (anonymous GUID) | Yes | Yes | Correlate logs |
| Device platform + OS version | No | Yes | Bug triage |
| App version | No | Yes | Version tracking |
| API keys | Never | Never | Security |
| Transcript text | Yes | Never | Privacy |
| Audio data | Never | Never | Privacy |
| Camera frames | Never | Never | Privacy |

---

## Success Criteria

1. All `DebugLog?.Invoke()` calls replaced with `ILogger<T>` calls with appropriate levels
2. Debug overlay still works (fed by `InAppLoggerProvider`)
3. Remote sink receives Warning+ logs when opt-in enabled
4. Unhandled exceptions captured with breadcrumbs
5. No sensitive data in remote telemetry
6. Memory usage stable (no unbounded string growth)
