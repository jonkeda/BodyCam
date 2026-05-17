# M33 Phase 4 — Wave 2: Default Gesture-to-Action Mapping

**Parent:** [`../phase4-button-provider.md`](../phase4-button-provider.md)
**Siblings:** [wave1](wave1-heycyan-button-provider.md) · [wave3](wave3-settings-ui.md) · [wave4](wave4-tests.md)
**Depends on:** Wave 1 (`HeyCyanButtonProvider`), M14 Phase 1
(`ActionMap`, `IButtonMappingStore`, `ButtonAction`).

## Goal

Seed a sane out-of-the-box mapping for the single HeyCyan glasses button
into the central `ActionMap`, while preserving any user overrides already
stored in `IButtonMappingStore`. Per the M33 overview, defaults are:

| Gesture     | Default `ButtonAction`              | Rationale                               |
|-------------|-------------------------------------|-----------------------------------------|
| `Tap`       | `ToggleConversation` (start/stop)   | Most common, lowest-effort gesture      |
| `DoubleTap` | `CapturePhoto`                      | Discoverable visual action              |
| `LongPress` | `EndSession`                        | Hard-to-trigger, irreversible action    |

These match the M17 Phase 2 placeholder defaults and the QCSDK reference
demo expectations.

## Steps

1. **Add a defaults helper** at
   `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanButtonDefaults.cs`:

    ```csharp
    namespace BodyCam.Services.Glasses.HeyCyan;

    public static class HeyCyanButtonDefaults
    {
        public const string ProviderId = HeyCyanButtonProvider.ProviderIdConst;
        public const string ButtonId   = HeyCyanButtonProvider.ButtonIdConst;

        /// <summary>
        /// Seed default mappings for the HeyCyan glasses button. Existing
        /// user overrides (already present in <see cref="IButtonMappingStore"/>)
        /// are NOT overwritten — only unset entries are populated.
        /// </summary>
        public static void SeedDefaults(ActionMap map)
        {
            map.SetIfUnset(ProviderId, ButtonId, ButtonGesture.Tap,
                           ButtonAction.ToggleConversation);
            map.SetIfUnset(ProviderId, ButtonId, ButtonGesture.DoubleTap,
                           ButtonAction.CapturePhoto);
            map.SetIfUnset(ProviderId, ButtonId, ButtonGesture.LongPress,
                           ButtonAction.EndSession);
        }

        /// <summary>
        /// The fixed set of gestures the HeyCyan firmware can produce.
        /// Used by the settings UI (Wave 3) to render exactly three rows.
        /// </summary>
        public static IReadOnlyList<ButtonGesture> SupportedGestures { get; } =
            new[] { ButtonGesture.Tap, ButtonGesture.DoubleTap, ButtonGesture.LongPress };
    }
    ```

    `SetIfUnset` is the existing M14 helper that no-ops when the key already
    has a stored mapping. If `ActionMap` does not yet expose `SetIfUnset`,
    add it as a one-liner: check `_store.TryGet(...)`, only call `Set(...)`
    when absent.

2. **Wire into application startup.** Inside the existing M14
   `ButtonMappingsBootstrapper` (or equivalent `MauiAppBuilder` extension),
   call `HeyCyanButtonDefaults.SeedDefaults(actionMap)` once during DI
   container build, *after* the user's persisted store has been loaded:

    ```csharp
    // MauiProgram.cs (or BodyCamServiceCollectionExtensions)
    public static IServiceCollection AddHeyCyanButtonDefaults(this IServiceCollection services)
    {
        services.AddSingleton<IStartupTask, HeyCyanButtonDefaultsStartupTask>();
        return services;
    }

    internal sealed class HeyCyanButtonDefaultsStartupTask : IStartupTask
    {
        private readonly ActionMap _map;
        public HeyCyanButtonDefaultsStartupTask(ActionMap map) => _map = map;
        public Task RunAsync(CancellationToken ct)
        {
            HeyCyanButtonDefaults.SeedDefaults(_map);
            return Task.CompletedTask;
        }
    }
    ```

    Use whichever startup-task abstraction the app already uses (M14 Phase 1
    introduced one for action-map bootstrapping; reuse it).

3. **Idempotency.** Seeding must be safe to run on every launch. Because
   `SetIfUnset` short-circuits when a value is present, the first launch
   populates defaults; subsequent launches are no-ops. **Never** clear or
   overwrite the store on startup.

4. **Migration / app-upgrade behavior.** If a future build adds a new
   gesture to `HeyCyanButtonGesture`, the new gesture's default goes here
   and existing user mappings remain untouched. Removing a gesture is a
   breaking change — leave stale entries in the store; they simply never
   fire.

5. **Action handler registration.** `ToggleConversation`, `CapturePhoto`,
   and `EndSession` are already registered as M14 `ButtonAction` enum
   members and resolved by `BodyCamSession` (M14 Phase 2). No new handlers
   needed in this wave; if any are missing in the current branch, file a
   blocker against M14 Phase 2 rather than adding them here.

6. **Logging.** Have the startup task log at `Information` level:
   "HeyCyan button defaults applied: Tap→ToggleConversation,
   DoubleTap→CapturePhoto, LongPress→EndSession (existing overrides
   preserved)". This is invaluable when diagnosing user reports.

## Verify

- [ ] `HeyCyanButtonDefaults.SeedDefaults` is called exactly once per app
      launch
- [ ] First launch: store contains the three default mappings
- [ ] Second launch (no user changes): store unchanged, no overwrites
- [ ] User remap of `Tap` → `Look` survives a restart and a defaults reseed
- [ ] All three defaults resolve to a registered handler in `BodyCamSession`
      (no `ButtonAction.Unhandled` warnings in the log)
- [ ] `SupportedGestures` returns exactly `{ Tap, DoubleTap, LongPress }`
- [ ] Generic M14 providers' default mappings (keyboard, BTHome, GATT) are
      unaffected by this seeding
