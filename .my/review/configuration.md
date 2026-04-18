# Configuration & Bootstrap Review

## Summary

`AppSettings` is a mutable object shared across layers. Changes made in SettingsViewModel propagate to AppSettings immediately but are not picked up by a running session. The bootstrap method (MauiProgram.cs) is long and unstructured.

---

## 1. Mutable Shared AppSettings

**Risk: Medium**

`AppSettings` is registered as a singleton and injected into `RealtimeClient`, `AgentOrchestrator`, `VisionAgent`, and others. When the user changes a setting (e.g., model, voice, system instructions) in SettingsPage, `SettingsViewModel` writes to both `ISettingsService` (persistent) and `AppSettings` (runtime).

**Problem:** If a session is active, the running `RealtimeClient` was configured with the old values at `ConnectAsync` time. The new values sit in `AppSettings` unused until the next session.

**Scenarios:**
- User changes voice mid-session → no effect until restart
- User changes model mid-session → no effect (not obviously wrong, but confusing)
- User changes system instructions → no effect until restart

**Current mitigation:** `AgentOrchestrator.StartAsync` reads fresh AppSettings on each session start, so restarting picks up changes. This is acceptable but undocumented.

**Proposed improvements:**

Option A — **Document the behavior.** Add a note in SettingsPage: "Changes take effect on next session."

Option B — **Snapshot settings on session start.** Create a frozen copy:
```csharp
public record SessionConfig(string Model, string Voice, string Instructions, ...);

// In AgentOrchestrator.StartAsync:
var config = SessionConfig.FromAppSettings(_appSettings);
await _realtime.ConnectAsync(config, ct);
```

This makes it explicit that mid-session changes are ignored and removes the mutation risk.

Option C — **Live update via session.update.** Send a `session.update` message to the Realtime API when settings change. Only some settings support this (voice, instructions, tools — not model).

**Recommendation:** Option B. Snapshot on start, document the behavior.

---

## 2. MauiProgram.cs Bootstrap Size

**Risk: Low (maintainability)**

The `CreateMauiApp` method registers ~40 services in a single method. It mixes concerns: platform providers, managers, agents, tools, view models, configuration.

**Proposed fix:** Extract into extension methods:
```csharp
public static MauiAppBuilder AddBodyCamAudio(this MauiAppBuilder builder) { ... }
public static MauiAppBuilder AddBodyCamCamera(this MauiAppBuilder builder) { ... }
public static MauiAppBuilder AddBodyCamTools(this MauiAppBuilder builder) { ... }
public static MauiAppBuilder AddBodyCamAgents(this MauiAppBuilder builder) { ... }
```

Groups registrations by domain and makes the bootstrap method scannable.

---

## 3. Tool Settings Not Live-Reloaded

**Risk: Low**

`SettingsViewModel` loads tool settings once at construction. If a tool's settings structure changes at runtime (unlikely but possible with dynamic tool registration), the UI won't reflect it.

No fix needed — tools are statically registered. Mentioning for completeness.

---

## 4. ModelOptions as String Arrays

**Risk: Low (maintainability)**

`ModelOptions` defines available models, voices, and turn detection modes as `string[]`. This means typos in ViewModel bindings won't be caught at compile time.

**Proposed fix:** Use enums with display attributes, or at minimum, constants:
```csharp
public static class Voices
{
    public const string Alloy = "alloy";
    public const string Ash = "ash";
    // ...
}
```

---

## Priority

| Fix | Effort | Impact |
|-----|--------|--------|
| Document settings-take-effect behavior | Trivial | User clarity |
| Snapshot settings on session start | Small | Eliminates mutation risk |
| Extract DI registrations | Small | Maintainability |
| Constants for model options | Trivial | Type safety |
