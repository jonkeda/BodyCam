# RCA: EntryPointNotFoundException during glasses scan

## Symptom

Debug console shows:

```
Exception thrown: 'System.EntryPointNotFoundException' in BodyCam.dll
```

Appears when triggering a glasses scan, but also fires every ~1 second while the app is running.

## Root Cause

**This is a handled first-chance exception — not a bug.** The app is functioning correctly.

`AecProcessor.Initialize()` starts a 1-second statistics timer that calls `WebRtcApmInterop.GetStatistics()`. This P/Invokes into `webrtc-apm.dll` (SoundFlow.Extensions.WebRtc.Apm v1.4.0) looking for the export `webrtc_apm_get_statistics`. **That export does not exist** in the shipped native binary.

### Verified by probing the DLL exports

```
webrtc_apm_create                      OK
webrtc_apm_destroy                     OK
webrtc_apm_initialize                  OK
webrtc_apm_config_create               OK
webrtc_apm_config_set_echo_canceller   OK
webrtc_apm_config_set_noise_suppression OK
webrtc_apm_config_set_gain_controller1 OK
webrtc_apm_config_set_high_pass_filter OK
webrtc_apm_stream_config_create        OK
webrtc_apm_stream_config_destroy       OK
webrtc_apm_process_stream              OK
webrtc_apm_process_reverse_stream      OK
webrtc_apm_set_stream_delay_ms         OK
webrtc_apm_get_frame_size              OK
webrtc_apm_get_statistics              MISSING  ← only this one
```

### Why it's caught and harmless

The call site in `AecProcessor.GetStatistics()` already anticipates this:

```csharp
public ApmStatistics? GetStatistics()
{
    // ...
    try
    {
        int err = WebRtcApmInterop.GetStatistics(_apm, out var s);
        return err == 0 ? s : null;
    }
    catch (EntryPointNotFoundException)
    {
        // Native library doesn't export webrtc_apm_get_statistics
        return null;
    }
}
```

The exception is caught, `null` is returned, and the statistics timer silently produces no output. AEC processing (the actual audio path) works fine — all 16 other exports are present and functional.

### Why it appears during scan

The exception fires every second from the statistics timer started during `AecProcessor.Initialize()`, which runs at DI container build time (app startup). The user notices it during scan only because they're looking at the debug console at that point. It has **nothing to do with BLE scanning**.

## Severity

**None** — cosmetic debug noise only. No functional impact.

## Possible Improvements (optional, not required)

1. **Stop the timer after first failure** — cache the `EntryPointNotFoundException` and disable the statistics timer to eliminate the recurring first-chance exception:

   ```csharp
   private bool _statsUnavailable;

   // In timer callback:
   if (_statsUnavailable) return;
   if (GetStatistics() is { } s) { ... }
   else { _statsUnavailable = true; _statsTimer?.Stop(); }
   ```

2. **Pre-check with `NativeLibrary.TryGetExport`** — probe for the symbol once at init instead of relying on exception-driven control flow:

   ```csharp
   if (NativeLibrary.TryGetExport(
       NativeLibrary.Load("webrtc-apm"), "webrtc_apm_get_statistics", out _))
   {
       // Start stats timer
   }
   ```

3. **Build a custom `webrtc-apm.dll`** that includes the `webrtc_apm_get_statistics` export (requires rebuilding from WebRTC source).
