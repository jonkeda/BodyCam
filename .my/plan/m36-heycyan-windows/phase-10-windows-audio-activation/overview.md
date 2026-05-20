# Phase 10 - Windows HeyCyan Audio Endpoint Activation

**Date:** 2026-05-19  
**Status:** Design first  
**Depends on:** Phase 7 Classic Bluetooth pairing, RCA 003  
**Scope:** Windows-only workflow to make HeyCyan microphone/speaker endpoints active and understandable in the app.

## Goal

Make it practical to get the HeyCyan glasses microphone and speaker working on Windows.

The app can already:

- Connect to the glasses over BLE/GATT.
- Discover/pair/query Classic Bluetooth profiles.
- Register HeyCyan audio providers when Windows exposes usable MMDevice endpoints.
- Show fake/stored image capture for Windows photo testing.

The missing piece is a user-facing workflow for the case where Windows has Classic Bluetooth profile nodes for `M01 Pro_E6C9`, but the actual audio endpoints are `NotPresent`, `Unplugged`, `Disabled`, or missing from MMDevice enumeration.

## Non-Goals

- Do not spend more time trying to make Windows WiFi Direct/P2P work in this phase.
- Do not remove the stored-image Windows photo fallback.
- Do not automatically unpair/re-pair the glasses. RCA-808 showed that unpairing a dual-mode device can break the BLE bond and notification delivery.
- Do not make BLE connect block on a long Windows audio recovery loop.
- Do not pretend the app can force Windows to activate an MMDevice endpoint when the OS refuses to connect the audio profile.

## Current State

### User-visible behavior

After the faster connect fix, the glasses connect quickly, but the microphone and speaker may still not be usable.

The app can show the HeyCyan mic/speaker as registered providers, but the real route only works when the backing Windows MMDevice endpoint is active.

### Windows state observed locally

Windows currently has M01 Bluetooth/PnP nodes:

```text
M01 Pro_E6C9
M01 Pro_E6C9 Hands-Free AG
M01 Pro_E6C9 Avrcp Transport
Standard Serial over Bluetooth link (COM9)
```

But the audio endpoints are not active:

```text
Capture: Headset (M01 Pro_E6C9)     -> NotPresent
Render:  Headphones (M01 Pro_E6C9)  -> NotPresent / Unplugged
```

So the app has enough evidence to say "Windows knows about the glasses audio profile", but not enough to select a working mic/speaker route.

## Root Problem

There are two separate layers:

1. **Classic Bluetooth profile/device layer**
   - PnP nodes can exist for A2DP, HFP, AVRCP, RFCOMM, and the base Bluetooth device.
   - `BluetoothDevice.FromBluetoothAddressAsync` and SDP/RFCOMM queries can see this layer.

2. **Windows audio endpoint layer**
   - The app uses MMDevice/NAudio endpoints for actual capture/render.
   - The mic/speaker are usable only when Windows exposes an active capture/render endpoint.

The first layer can be present while the second layer is inactive. That is the current failure mode.

## Design Principle

Treat HeyCyan audio activation as an explicit guided workflow:

```text
BLE connected
  -> register HeyCyan mic/speaker placeholders
  -> inspect Windows profile + endpoint state
  -> show exactly what is missing
  -> open Windows Bluetooth settings when user action is required
  -> poll/rescan until MMDevice endpoints become Active
  -> auto-register and auto-select when ready
```

This keeps the user informed without turning normal glasses connect into an
unbounded wait.

## Connection Flow Placement

The primary activation flow belongs on the **Glasses page**.

Current navigation is already right:

```text
Device Settings
  -> Connect Glasses
  -> Glasses page
  -> select HeyCyan glasses
  -> Connect
```

The last step should be the one that tries to make the selected glasses usable.
That means `GlassesViewModel.ConnectCommand` should orchestrate:

1. BLE/GATT connection through the existing glasses service.
2. Windows-only HeyCyan audio endpoint activation.
3. UI progress while Windows audio is being checked or activated.
4. A bounded wait, then background polling if Windows still needs time/user action.

Device Settings should remain the place where the current camera/microphone/speaker
selection is visible. It can expose **Refresh Audio Status** and a secondary retry
action, but it should not be the main path for connecting the glasses.

## Proposed UX

### Glasses Page

After selecting the glasses and pressing **Connect**, the page should show one
continuous connection flow:

```text
Connecting to M01 Pro_E6C9...
Checking Windows Bluetooth audio...
Waiting for microphone/speaker endpoints...
```

If Windows has profile nodes but inactive MMDevice endpoints, the page should make
that visible and open Windows Bluetooth settings as part of the activation attempt:

```text
Windows sees M01 Pro_E6C9 Hands-Free AG, but the audio endpoint is NotPresent.
Open Bluetooth settings and click Connect for M01 Pro_E6C9.
```

Ready state:

```text
Connected to M01 Pro_E6C9
HeyCyan microphone ready
HeyCyan speaker ready
```

Failure/help state:

```text
Windows still reports M01 Pro_E6C9 audio endpoints as NotPresent.
Remove/re-pair the glasses as a headset in Windows Bluetooth settings.
```

The Glasses page can keep the BLE connection successful even when audio activation
is still pending. The important point is that the user is told which part is ready:

```text
Glasses connected. Windows audio still needs Bluetooth profile activation.
```

### Device Settings Page

Device Settings should show current status and offer repair/refresh actions after
the primary connection flow has run.

Controls:

- **Refresh Audio Status**
  - Re-reads profile/MMDevice state without opening settings.

- **Connect Audio** or **Retry Audio**
  - Optional secondary action.
  - Re-runs the same Windows activation service used by the Glasses page.
  - Useful when the user fixed something in Windows settings after leaving the
    Glasses page.

Status text examples:

```text
HeyCyan microphone: waiting for Windows Bluetooth endpoint
HeyCyan speaker: waiting for Windows Bluetooth endpoint
```

## Proposed Service Shape

Add a shared abstraction with a Windows implementation and a null implementation elsewhere:

```csharp
public interface IHeyCyanAudioEndpointActivationService
{
    bool IsSupported { get; }
    HeyCyanAudioEndpointSnapshot? Current { get; }
    event EventHandler<HeyCyanAudioEndpointSnapshot>? Updated;

    Task<HeyCyanAudioEndpointSnapshot> RefreshAsync(CancellationToken ct);
    Task<HeyCyanAudioEndpointSnapshot> BeginActivationAsync(
        HeyCyanDeviceInfo? selectedDevice,
        CancellationToken ct);
    Task OpenBluetoothSettingsAsync(CancellationToken ct);
}
```

Snapshot model:

```csharp
public sealed record HeyCyanAudioEndpointSnapshot(
    string? MacAddress,
    string Summary,
    HeyCyanEndpointStatus CaptureStatus,
    HeyCyanEndpointStatus RenderStatus,
    IReadOnlyList<HeyCyanWindowsEndpointInfo> CaptureEndpoints,
    IReadOnlyList<HeyCyanWindowsEndpointInfo> RenderEndpoints,
    IReadOnlyList<HeyCyanWindowsProfileInfo> ProfileNodes,
    bool RequiresUserAction);

public enum HeyCyanEndpointStatus
{
    Unknown,
    Missing,
    NotPresent,
    Unplugged,
    Disabled,
    Active
}
```

Endpoint info:

```csharp
public sealed record HeyCyanWindowsEndpointInfo(
    string FriendlyName,
    string DeviceId,
    string State,
    string? ProviderId,
    string? MatchedMac);
```

Profile info:

```csharp
public sealed record HeyCyanWindowsProfileInfo(
    string Name,
    string Status,
    string PnpClass,
    string DeviceId);
```

## Windows Implementation

Create:

```text
src/BodyCam/Platforms/Windows/HeyCyan/WindowsHeyCyanAudioEndpointActivationService.cs
```

Responsibilities:

1. Resolve the target MAC from `IHeyCyanGlassesSession.Device?.Address`.
2. Refresh paired Bluetooth name-to-MAC cache.
3. Enumerate all MMDevice capture/render endpoints, not only active endpoints.
4. Match M01 endpoints by:
   - extracted BTHENUM MAC,
   - paired-device friendly-name fallback,
   - friendly name containing the connected device name where MAC is not available.
5. Enumerate relevant PnP nodes for the MAC/name:
   - `BTHENUM\{0000110B...}` A2DP AudioSink,
   - `BTHENUM\{0000111E...}` HFP Hands-Free,
   - AVRCP,
   - base Bluetooth device,
   - serial/RFCOMM if present.
6. Produce a snapshot with a clear summary.
7. Call `ScanAndRegister()` on input/output enumerators when active endpoints appear.
8. Trigger the existing `HeyCyanAudioRouter` path by relying on `EndpointRegistered` and the existing provider managers.

Non-destructive activation attempt:

```text
BeginActivationAsync(selectedDevice)
  -> RefreshAsync
  -> if both endpoints Active: scan/register/select and return
  -> run SDP/RFCOMM HFP query as a soft nudge
  -> open ms-settings:bluetooth
  -> poll RefreshAsync every 2s up to 60s
  -> when endpoint Active: scan/register/select
  -> if timeout: return snapshot requiring user action
```

Important safety rules:

- Never call `UnpairAsync` automatically.
- Never disconnect/reconnect the BLE session.
- Never block `WindowsHeyCyanGlassesSession.ConnectAsync`.
- If user wants a destructive repair, show instructions instead of doing it.

## Reuse Existing Pieces

Keep using:

- `WindowsBluetoothEnumerator`
- `WindowsBluetoothOutputEnumerator`
- `HeyCyanAudioRouter`
- `HeyCyanAudioInputProvider`
- `HeyCyanAudioOutputProvider`
- `DeviceViewModel` provider/status bindings

Do not replace `IHeyCyanAudioDiagnostics`. That service is primarily codec/route diagnostics and already has Android/iOS behavior. Phase 10 should either:

- add a separate endpoint activation service, or
- add endpoint snapshot fields only if the existing interface can be extended without confusing codec diagnostics.

The preferred design is a separate endpoint activation service because Windows endpoint state is not the same thing as negotiated audio codec state.

## Glasses Page Integration

`GlassesViewModel` should receive `IHeyCyanAudioEndpointActivationService`.

`ConnectCommand` should become the orchestrator:

```csharp
private async Task ConnectAsync()
{
    if (SelectedDevice is null)
    {
        return;
    }

    ConnectionDetailStatus = $"Connecting to {SelectedDevice.Name}...";
    await _glasses.ConnectAsync(SelectedDevice, CancellationToken.None);

    if (_audioEndpointActivation.IsSupported)
    {
        ConnectionDetailStatus = "Checking Windows Bluetooth audio...";
        var snapshot = await _audioEndpointActivation.BeginActivationAsync(
            SelectedDevice,
            CancellationToken.None);

        ConnectionDetailStatus = snapshot.Summary;
    }
}
```

New properties:

```csharp
public string? ConnectionDetailStatus { get; }
public bool IsAudioActivationRunning { get; }
public bool ShowWindowsAudioActivationStatus { get; }
```

The UI should keep the selected glasses row and connect button simple, but show
the detailed connection/audio status near the button so the user sees what is
happening while the command runs.

On Android and iOS, the activation service should be a no-op/null implementation
with `IsSupported == false`. The `ConnectCommand` can call the service safely
without changing the mobile behavior.

## Device Settings Integration

`DeviceViewModel` should receive `IHeyCyanAudioEndpointActivationService`.

New properties:

```csharp
public AsyncRelayCommand RetryHeyCyanAudioCommand { get; }
public AsyncRelayCommand RefreshHeyCyanAudioStatusCommand { get; }
public string? HeyCyanAudioEndpointSummary { get; }
public bool IsHeyCyanAudioActivationRunning { get; }
```

Existing status properties can stay:

```csharp
HeyCyanAudioInputStatus
HeyCyanAudioOutputStatus
```

When activation snapshot updates:

- refresh `HeyCyanAudioInputStatus`,
- refresh `HeyCyanAudioOutputStatus`,
- refresh the picker lists,
- refresh selected providers.

The picker can show registered HeyCyan providers even when unavailable, but auto-selection must still require `IsAvailable`.

## Implementation Order

1. Create shared endpoint activation interfaces/models.
2. Add null implementation for non-Windows.
3. Add Windows implementation that can only refresh state.
4. Add Windows implementation for `OpenBluetoothSettingsAsync`.
5. Add polling `BeginActivationAsync`.
6. Wire DI.
7. Wire `GlassesViewModel.ConnectCommand` to run activation after BLE connect.
8. Add Glasses page status UI for connection/audio activation progress.
9. Add Device Settings status/refresh/retry UI as a secondary surface.
10. Add tests for `GlassesViewModel` orchestration and `DeviceViewModel` status behavior.
11. Add Windows manual verification checklist.

## Manual Verification

### Case A: Endpoints already active

1. Pair/connect glasses as Windows headset.
2. Open Device Settings.
3. Click **Connect Glasses**.
4. Select `M01 Pro_E6C9` on the Glasses page.
5. Click **Connect**.
6. Confirm the Glasses page reports connected/audio ready.
7. Confirm Device Settings shows HeyCyan mic/speaker ready.
8. Confirm app auto-selects HeyCyan mic/speaker.

### Case B: PnP nodes exist, MMDevice endpoints not active

1. Ensure Windows reports `M01 Pro_E6C9 Hands-Free AG`.
2. Confirm MMDevice endpoints are `NotPresent`/`Unplugged`.
3. Open the Glasses page through Device Settings.
4. Select `M01 Pro_E6C9`.
5. Click **Connect**.
6. Confirm Bluetooth settings opens.
7. Click Connect for `M01 Pro_E6C9` in Windows settings.
8. Confirm app status changes to ready when MMDevice endpoints become active.
9. Confirm Device Settings shows HeyCyan mic/speaker ready after returning.

### Case C: Stale/failed Windows profile

1. Click **Connect** on the Glasses page.
2. Wait for timeout.
3. Confirm app summary says Windows still reports endpoints inactive.
4. Use Device Settings **Retry Audio** after trying manual Windows repair.
5. Follow manual repair guidance if needed: remove device, remove stale endpoints, re-pair as headset.

## Open Questions

1. Does `ms-settings:bluetooth` land close enough to the device row, or should we launch `ms-settings:sound` as well?
2. Can Windows expose a render-only A2DP endpoint for M01 while HFP capture stays missing? The UI should support partial readiness.
3. Should stale M01 MMDevice endpoints be shown in a diagnostic details expander?
4. Should the activation service write a compact diagnostic log file for hardware sessions?

## Success Criteria

- Pressing **Connect** on the Glasses page attempts BLE connection and Windows audio activation.
- The user can see why mic/speaker are not usable.
- The app gives one clear action: connect the M01 audio profile in Windows Bluetooth settings.
- The app polls and auto-selects once Windows endpoints become active.
- BLE connection remains fast.
- No automatic unpair/re-pair is performed.
- Android/iOS behavior is unchanged because the endpoint activation service is unsupported/no-op there.
