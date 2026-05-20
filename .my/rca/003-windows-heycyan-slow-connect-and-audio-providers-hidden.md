# RCA 003: Windows HeyCyan connect is slow and mic/speaker providers do not appear

**Date:** 2026-05-19  
**Status:** Open  
**Platform:** Windows  
**Device:** HeyCyan / M01 Pro_E6C9 (`D8:79:B8:7F:E6:C9`)

## Symptoms

1. Clicking **Connect** took a long time before the app reported the glasses as connected.
2. After connection, **Camera Source** showed `HeyCyan`, but **Microphone** and **Speaker** did not show HeyCyan providers.

Observed log sequence:

```text
CameraManager: HeyCyan state changed to Connecting; reselecting camera
WindowsHeyCyanGlassesSession: EnsureBtAudio: device paired but no endpoints -- forcing profile connection
WindowsHeyCyanGlassesSession: HFP service found via SDP -- waiting for Windows to create endpoint
WindowsHeyCyanGlassesSession: EnsureBtAudio: profile forcing failed -- continuing without audio
WindowsHeyCyanGlassesSession: HFP service found via SDP -- waiting for Windows to create endpoint
WindowsHeyCyanGlassesSession: Classic BT audio unavailable for M01 Pro_E6C9 -- GATT-only connection
HeyCyanAudioRouter: HeyCyan glasses mic not yet available -- will auto-select when endpoint appears.
HeyCyanAudioRouter: HeyCyan glasses speaker not yet available -- will auto-select when endpoint appears.
HeyCyanAudioRouter: Routed live audio to HeyCyan glasses (...)
CameraManager: HeyCyan state changed to Connected; reselecting camera
WindowsHeyCyanGlassesSession: Connected to M01 Pro_E6C9
```

## Impact

The Windows camera path currently downloads the stored/fake fallback image, so the important Windows hardware value is BLE control plus Classic Bluetooth mic/speaker. The connect action feels stuck because it blocks on Windows Classic Bluetooth audio recovery, and after the wait Windows still does not expose active audio endpoints. The Devices page then cannot list or select HeyCyan as a microphone or speaker.

## Regression Note From m36

The m36 notes point more toward the Classic Bluetooth audio recovery work than the WiFi photo download code.

- `.my/rca/fixed/serial-port-service-not-found-after-classic-bt-pair.md` says the previous BLE-only connection worked reliably because no Classic BT operation competed with GATT discovery.
- `.my/plan/m36-heycyan-windows/phase-7-classic-bt-pairing/3. connection-flow-design.md` then moved Classic BT work after BLE/GATT setup, but still designed it as part of `WindowsHeyCyanGlassesSession.ConnectAsync`.
- `.my/plan/m36-heycyan-windows/phase-8-wifi-photos/rca-808.md` later shows `EnsureClassicBtAudioAsync` causing BLE-side fallout when it tried to repair Classic BT too aggressively.
- `.my/plan/m36-heycyan-windows/phase-8-wifi-photos/rca-813-ble-to-wifi-flow.md` describes the WiFi transfer flow as a separate capture/download phase after BLE is already working.

So the likely regression is not the stored-image fallback or the media download path. The likely regression is that normal Windows app connect now waits synchronously for Classic BT audio endpoint forcing. We do still need the Classic BT audio recovery, but it should not hold the whole BLE connection hostage.

The Windows DI registration should keep `ensureClassicAudio` enabled, but the implementation should run that work after the BLE session has reported `Connected`.

## Local Validation

Windows currently has M01 Bluetooth device nodes, including A2DP/HFP-related PnP entries, but the actual MMDevice audio endpoints are not active:

```text
Capture: Headset (M01 Pro_E6C9)     -> NotPresent
Render:  Headphones (M01 Pro_E6C9)  -> NotPresent / Unplugged
```

The app enumerates active MMDevice endpoints for real input/output. With the endpoints in `NotPresent` or `Unplugged`, the app has nothing usable to select yet. This confirms the UI symptom is not merely a dropdown binding issue.

## Root Cause

### 1. Connect is blocked by Classic Bluetooth audio endpoint forcing

`WindowsHeyCyanGlassesSession.ConnectAsync` completes the BLE/GATT setup, then calls `EnsureClassicBtAudioAsync` before setting state to `Connected`.

Relevant flow:

```text
ConnectAsync
  -> BLE/GATT connect and notification setup
  -> EnsureClassicBtAudioAsync
       -> scan current Windows audio endpoints
       -> if paired but no endpoints, TryForceProfileConnectionAsync
          -> query HFP SDP
          -> poll every 2s for up to 15 attempts
       -> then Phase C pairing path can run and force/poll again
  -> State = Connected
```

`TryForceProfileConnectionAsync` polls for endpoint appearance for up to 30 seconds. In the observed run, the log appears twice:

```text
HFP service found via SDP ... waiting for Windows to create endpoint
...
HFP service found via SDP ... waiting for Windows to create endpoint
```

That means the connect flow can spend roughly 30-60 seconds trying to make Windows create HFP/A2DP endpoints even though the BLE session is already usable. This explains the long perceived connect time.

### 2. Windows connected the glasses as GATT-only

The decisive log line is:

```text
Classic BT audio unavailable for M01 Pro_E6C9 -- GATT-only connection
```

The glasses BLE session succeeded, but Windows did not expose audio capture/render endpoints matching the glasses MAC. The app therefore has a connected HeyCyan session but no usable Windows microphone/speaker endpoints for that device.

### 3. Camera availability and audio availability use different rules

The camera provider is available when the HeyCyan session is `Connected` or `TransferMode`.

The HeyCyan mic/speaker providers are stricter:

```csharp
_session.State == HeyCyanState.Connected &&
_bt.HasEndpointWithMac(_session.Device?.Address)
```

So the camera can appear with a BLE/GATT-only connection, while microphone and speaker stay unavailable until Windows creates matching Classic Bluetooth endpoints.

### 4. The Devices page filters out unavailable audio providers

`DeviceViewModel.AudioInputProviders` and `DeviceViewModel.AudioOutputProviders` return only providers where `IsAvailable` is true.

That means the HeyCyan audio providers may be registered internally by `HeyCyanAudioRouter`, but they will not appear in the UI if Windows has not created matching endpoints.

### 5. The audio router log is misleading

`HeyCyanAudioRouter` logs:

```text
Routed live audio to HeyCyan glasses
```

even when the preceding lines say:

```text
HeyCyan glasses mic not yet available
HeyCyan glasses speaker not yet available
```

In this scenario, the router registered the providers and attempted routing, but did not actually route mic/speaker audio because the providers were not available. The log should distinguish "registered/waiting" from "routed".

## Contributing Factors

- Windows audio endpoints for dual-mode Bluetooth devices are created by the OS, not by the BLE/GATT session.
- SDP discovery can find the HFP service without Windows actually creating a usable audio endpoint.
- The app treats Classic BT audio endpoint forcing as a synchronous part of connect, even though the audio endpoint may appear later or require a Windows profile reconnect outside the BLE session.
- The UI currently hides unavailable providers, which removes useful diagnostic information.

## Proposed Fixes

### Priority 1: Keep Classic BT audio recovery, but run it after BLE connect

Make Classic BT audio setup best-effort and asynchronous on Windows:

1. Complete BLE/GATT connection quickly.
2. Set `State = Connected`.
3. Start Classic BT audio endpoint discovery/recovery in the background.
4. When endpoints appear, register/refresh/select HeyCyan mic/speaker.

This preserves the goal of getting mic/speaker working, while avoiding a Connect button that appears frozen for 30-60 seconds. Do not disable Classic BT audio for the normal Windows build.

### Priority 2: Avoid double endpoint forcing

If the device is already paired but `TryForceProfileConnectionAsync` fails, do not immediately enter the full pairing phase and poll again. Return `Failure` or use a short single retry. The current flow can duplicate the same 30-second wait.

### Priority 3: Fix router logging

Change the final routing log to reflect the actual outcome:

- If both selected: `Routed live audio to HeyCyan glasses`
- If one selected: `Partially routed HeyCyan audio`
- If none selected: `HeyCyan audio providers registered; waiting for Windows audio endpoints`

### Priority 4: Improve Devices page diagnostics

The audio pickers should show registered HeyCyan devices even when Windows has
not activated the backing MMDevice endpoint yet, with a status row:

```text
HeyCyan Glasses Mic - waiting for Windows Bluetooth endpoint
HeyCyan Glasses Speaker - waiting for Windows Bluetooth endpoint
```

This would make it clear that the app knows about the glasses, but Windows has not provided audio endpoints.

Applied change:

- `DeviceViewModel.AudioInputProviders` / `AudioOutputProviders` now return registered providers instead of filtering by `IsAvailable`.
- The Devices page shows `HeyCyan microphone/speaker waiting for Windows Bluetooth endpoint` while the provider is registered but unavailable.
- Audio auto-select still requires `IsAvailable`, so the app does not silently route to a dead Windows endpoint.

## Verification Plan

1. Connect glasses on Windows with M01 MMDevice endpoints currently `NotPresent`.
2. Confirm connect reaches `Connected` quickly after BLE/GATT setup.
3. Confirm background Classic BT audio recovery starts and only attempts profile forcing once.
4. If Windows changes M01 endpoints to `Active`, confirm HeyCyan mic/speaker auto-select.
5. If endpoints remain `NotPresent`, confirm logs clearly say Windows audio endpoints are not active.
6. Confirm logs no longer say "Routed live audio" unless a provider was actually selected.

## Current Workaround

Recover the Windows Bluetooth audio profile outside the app if endpoints remain `NotPresent`: open Windows Bluetooth settings, connect/reconnect the `M01 Pro_E6C9` audio profile, or remove/re-pair the glasses as a headset if Windows refuses to activate HFP/A2DP. Once Windows exposes `Headset (M01 Pro_E6C9)` or `Headphones (M01 Pro_E6C9)` as active MMDevice endpoints, the app should be able to register and select them.

## Affected Files

- `src/BodyCam/Platforms/Windows/HeyCyan/WindowsHeyCyanGlassesSession.cs`
- `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioInputProvider.cs`
- `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioOutputProvider.cs`
- `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanAudioRouter.cs`
- `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs`
