# M47 Report - Device Media Architecture

## Executive Summary

BodyCam does not have two completely separate hardware stacks. It has one main
runtime provider layer:

```text
ICameraProvider       -> CameraManager
IAudioInputProvider   -> AudioInputManager
IAudioOutputProvider  -> AudioOutputManager
IButtonInputProvider  -> ButtonInputManager
```

However, it does have two selection/orchestration paths that both try to drive
those managers:

1. **Runtime/front-page path**
   - `MainPage` initializes audio and button managers.
   - `MainViewModel` runs camera commands and active sessions.
   - `AgentOrchestrator`, `VoiceInputAgent`, and `VoiceOutputAgent` consume the
     managers through service interfaces.
   - HeyCyan runtime routing also happens independently through
     `CameraManager` session reselect and `HeyCyanAudioRouter`.

2. **Device settings path**
   - `DeviceSettingsPage` -> `DeviceViewModel`.
   - `DeviceViewModel` exposes source profiles and per-slot pickers.
   - `SourceProfileManager` applies profiles by calling the same three
     managers.
   - `DeviceSettings` JSON stores profile and known-device data.

So the answer to the user's question is:

**Yes, effectively there are two orchestration paths. They converge on the same
managers at runtime, but they do not have one shared owner or one shared saved
state model.**

## Runtime Provider Layer

### Camera Pictures

The still-picture architecture is the strongest part.

Key files:

- `src/BodyCam/Services/Camera/ICameraProvider.cs`
- `src/BodyCam/Services/Camera/CameraManager.cs`
- `src/BodyCam/Services/Camera/PhoneCameraProvider.cs`
- `src/BodyCam/Services/Glasses/HeyCyan/HeyCyanCameraProvider.cs`
- `src/BodyCam/Services/Camera/Commands/CameraCommandService.cs`

The intended flow for still pictures is:

```text
UI action / camera command
  -> CameraCommandService
  -> CameraManager.CaptureFrameAsync()
  -> active ICameraProvider.CaptureFrameAsync()
  -> VisionAgent / QR scanner / transcript
```

`CameraManager` owns the active camera provider. It persists the active provider
to the flat `ISettingsService.ActiveCameraProvider` key. If no active provider is
set, it falls back to the provider with ID `phone`.

`PhoneCameraProvider` wraps the MAUI CommunityToolkit `CameraView`. `MainPage`
passes the native `CameraView` into the provider:

```text
MainPage
  -> phoneCamera.SetCameraView(CameraPanel.CameraPreviewControl)
```

HeyCyan pictures are also an `ICameraProvider`: `HeyCyanCameraProvider` sends a
photo command to the glasses, waits for the file to settle, enters transfer
mode, lists media, and downloads the newest JPEG.

### Camera Commands From The Front Page

The action drawer Look/Read/Scan commands use the newer camera-command path:

```text
MainViewModel.ExecuteCameraCommandAsync()
  -> CameraCommandService.ExecuteAsync()
  -> LookCommand / ReadCommand / ScanCommand
  -> CameraManager.CaptureFrameAsync()
```

This path respects the active `CameraManager` provider. It also adds the
captured image to the transcript through `CameraCommandTranscriptInput`.

### Active Realtime Tool Calls

There is a second camera path in active realtime sessions.

`AgentOrchestrator.CreateToolContext()` exposes:

```text
CaptureFrame = FrameCaptureFunc ?? _cameraManager.CaptureFrameAsync
```

But `MainViewModel.SetLayerAsync()` sets:

```text
_orchestrator.FrameCaptureFunc = CaptureFrameFromCameraViewAsync
```

That means older realtime tools that call `context.CaptureFrame` can bypass
`CameraManager` and capture directly from the phone `CameraView`.

Affected tools include:

- `DescribeSceneTool`
- `TakePhotoTool`
- `FindObjectTool`
- `LookupBarcodeTool`
- `StartSceneWatchTool`

Newer command-based tools, such as `LookTool`, `ReadTextTool`, and
`ScanQrCodeTool`, go through `ICameraCommandService` and therefore through
`CameraManager`.

This is the clearest current split between "front page runtime" and "selected
device" behavior. A user may select HeyCyan or another external camera, but some
active-session tools can still use the phone `CameraView`.

### Camera Video

Video is not a unified app architecture yet.

The shared camera interface has:

```text
bool SupportsVideoRecording
IAsyncEnumerable<byte[]> StreamFramesAsync(...)
```

But there is no shared `StartVideoAsync` / `StopVideoAsync` camera-manager
contract and no front-page video workflow.

Current state:

- `PhoneCameraProvider` and some A9 providers report
  `SupportsVideoRecording = true`.
- `DeviceSettingsPage` shows a "Record Video" button when the active provider
  supports video, but that button has no command wired to it.
- HeyCyan has video start/stop at the glasses session level
  (`IHeyCyanGlassesSession.StartVideoAsync` / `StopVideoAsync`), but
  `HeyCyanCameraProvider` reports `SupportsVideoRecording = false` because the
  camera provider is a still-picture provider.
- A9/Vue990 work contains video/probe logic, but it is provider-specific and
  not exposed as a general app-level video feature.

So: **pictures are first-class; video is currently provider-specific and partly
diagnostic/probe-oriented.**

## Input Audio Architecture

Key files:

- `src/BodyCam/Services/Audio/IAudioInputProvider.cs`
- `src/BodyCam/Services/Audio/AudioInputManager.cs`
- `src/BodyCam/Agents/VoiceInputAgent.cs`
- platform mic providers under `src/BodyCam/Platforms/*`

Runtime flow:

```text
Platform/Bluetooth/HeyCyan mic provider
  -> AudioInputManager
  -> WebRTC/platform AEC handling
  -> IAudioInputService.AudioChunkAvailable
  -> VoiceInputAgent
  -> resample 48 kHz to 24 kHz
  -> Realtime API input_audio_buffer
```

`AudioInputManager` implements `IAudioInputService` for backward compatibility,
so the app-facing voice pipeline depends on the manager, not on a concrete
provider.

The manager can register dynamic providers, such as Bluetooth endpoints, after
startup. It persists active input provider selection to the flat
`ISettingsService.ActiveAudioInputProvider` key.

HeyCyan audio is special:

- `HeyCyanAudioInputProvider` is registered as a concrete provider, not directly
  as `IAudioInputProvider`, to avoid circular DI.
- `HeyCyanAudioRouter` registers it dynamically with `AudioInputManager` when
  the glasses session connects.
- The actual live mic is Bluetooth classic/HFP style audio, not BLE media.

## Output Audio Architecture

Key files:

- `src/BodyCam/Services/Audio/IAudioOutputProvider.cs`
- `src/BodyCam/Services/Audio/AudioOutputManager.cs`
- `src/BodyCam/Agents/VoiceOutputAgent.cs`
- platform speaker providers under `src/BodyCam/Platforms/*`

Runtime flow:

```text
Realtime API output audio delta at 24 kHz
  -> VoiceOutputAgent
  -> resample 24 kHz to 48 kHz
  -> AudioOutputManager
  -> optional jitter buffer
  -> active IAudioOutputProvider
```

`AudioOutputManager` implements `IAudioOutputService`. It owns the active output
provider, persists it to `ISettingsService.ActiveAudioOutputProvider`, and
handles provider fallback.

It also integrates with echo cancellation:

- output route changes update estimated AEC stream delay;
- render-reference audio is fed to the AEC processor when the active output
  route requires it.

HeyCyan output mirrors input:

- `HeyCyanAudioOutputProvider` is registered dynamically by
  `HeyCyanAudioRouter`;
- actual speaker output is Bluetooth classic/A2DP style audio;
- BLE is used for control/session state, not for live output audio.

## Device Settings Architecture

Key files:

- `src/BodyCam/Pages/Settings/DeviceSettingsPage.xaml`
- `src/BodyCam/ViewModels/Settings/DeviceViewModel.cs`
- `src/BodyCam/Services/SourceProfileManager.cs`
- `src/BodyCam/Services/ISourceProfile.cs`
- `src/BodyCam/Models/DeviceSettings.cs`

The Devices page has three concepts:

1. **Connected device cards**
   - Read-only-ish summary cards built by looking at connected glasses,
     available audio providers, button providers, and external cameras.

2. **Source profiles**
   - User-facing bundles like Phone, HeyCyan Glasses, Bluetooth Audio, Laptop,
     and Custom.
   - Implemented by `ISourceProfile`.
   - Applied by `SourceProfileManager`.

3. **Individual slot pickers**
   - Camera source.
   - Microphone.
   - Speaker.
   - Visible in Custom mode.
   - These call the three runtime managers directly.

Profile application flow:

```text
DeviceSettingsPage picker
  -> DeviceViewModel.SelectedProfile
  -> SourceProfileManager.ApplyProfileAsync(profileId)
  -> ISourceProfile.ApplyAsync(camera, mic, speaker)
  -> CameraManager / AudioInputManager / AudioOutputManager
```

Individual picker flow:

```text
DeviceSettingsPage picker
  -> DeviceViewModel.SelectedCameraProvider / SelectedAudioInputProvider / SelectedAudioOutputProvider
  -> manager.SetActiveAsync(providerId)
  -> DeviceViewModel.SwitchToCustomIfNeeded()
  -> SourceProfileManager.ApplyProfileAsync("custom")
```

## Where The Two Paths Diverge

### 1. SourceProfileManager Is Not The Startup Owner

`SourceProfileManager` is registered in DI, and it has an `InitializeAsync`
method. But current app startup does not call it.

`MainPage.Loaded` initializes:

- `AudioInputManager`;
- `AudioOutputManager`;
- `ButtonInputManager`;
- Bluetooth enumerators;
- HeyCyan auto-reconnect.

It does not initialize:

- `SourceProfileManager`;
- `CameraManager`.

So the Device page can display and apply profiles, but the app runtime can start
without the profile manager having applied the saved profile.

### 2. HeyCyan Runtime Auto-Selection Exists Outside Profiles

HeyCyan source selection can happen even when the Device settings page is never
opened:

- `CameraManager` listens to `IHeyCyanGlassesSession.StateChanged` and
  reselects the camera with `HeyCyanCameraSelector`.
- `HeyCyanAudioRouter` listens to the same session and dynamically registers or
  unregisters HeyCyan mic/speaker providers with the audio managers.

If the Devices page is open, `DeviceViewModel.OnGlassesStateChanged` also calls
`SourceProfileManager.HandleDeviceConnectedAsync()` or
`HandleDeviceDisconnectedAsync()`.

That means HeyCyan routing has two possible drivers:

- runtime manager/router behavior;
- settings profile behavior.

They usually point in the same direction, but they are not a single source of
truth.

### 3. CameraManager And Direct CameraView Capture Coexist

There are two picture capture mechanisms:

```text
CameraManager path:
  CameraCommandService / MainViewModel legacy vision methods
  -> CameraManager.CaptureFrameAsync()
  -> selected provider

Direct CameraView path:
  AgentOrchestrator ToolContext
  -> MainViewModel.CaptureFrameFromCameraViewAsync()
  -> phone CameraView
```

This is probably legacy from before `PhoneCameraProvider` existed. `MainPage`
still sets the same camera view on both:

```text
phoneCamera.SetCameraView(...)
viewModel.SetCameraView(...)
```

The first is the desired provider path. The second preserves a bypass.

### 4. Settings Persistence Is Split

There are two saved-state models:

1. Flat preferences:
   - `ActiveCameraProvider`
   - `ActiveAudioInputProvider`
   - `ActiveAudioOutputProvider`

2. JSON `DeviceSettings`:
   - `activeProfileId`
   - `custom`
   - `active`
   - `knownDevices`
   - `profileSettings`

The managers update the flat keys when active providers change. The
`SourceProfileManager` updates `DeviceSettings.ActiveProfileId`.

`DeviceSettings.Active` and `DeviceSettings.Custom` appear intended to replace
flat provider keys, but they are not currently updated by the manager
`SetActiveAsync` calls. `CustomSourceProfile.SaveCustomSelections()` exists, but
the Devices page does not call it in the current path.

This makes saved profile state and saved active provider state capable of
drifting.

## Direct Answer: Are There Two Architectures?

There is one provider/manager runtime architecture, but there are two
orchestration architectures layered over it:

| Area | Device settings path | Front-page/runtime path | Same runtime manager? | Risk |
|---|---|---|---|---|
| Camera profile selection | `SourceProfileManager` | `CameraManager` selector and active commands | Usually yes | Medium |
| Still picture capture | Settings test uses active provider | Action drawer uses `CameraCommandService`; some tools use direct `CameraView` | Not always | High |
| Video | Settings shows a button from `SupportsVideoRecording` | No shared front-page video workflow | No | High |
| Mic selection | `SourceProfileManager` or picker | `AudioInputManager` plus `HeyCyanAudioRouter` | Yes | Medium |
| Speaker selection | `SourceProfileManager` or picker | `AudioOutputManager` plus `HeyCyanAudioRouter` | Yes | Medium |
| Saved source state | `DeviceSettings` JSON | flat provider preference keys | No | High |

## Recommendations

### 1. Choose One Source-Selection Owner

Recommended: make `SourceProfileManager` the single owner of source selection.

It should:

- initialize during app startup;
- apply the saved profile before the first session starts;
- handle device connect/disconnect events globally, not only while the Devices
  page is alive;
- call the three managers to make provider changes;
- own `DeviceSettings` persistence.

Then `CameraManager`, `AudioInputManager`, and `AudioOutputManager` become slot
managers, not policy owners.

Alternative: remove `SourceProfileManager` from runtime policy and make Device
settings a pure facade over the three managers. That is simpler, but it loses
the nice "Phone / HeyCyan / Bluetooth / Custom" user mental model.

### 2. Remove The Direct CameraView Bypass

The active session should not set:

```text
_orchestrator.FrameCaptureFunc = CaptureFrameFromCameraViewAsync
```

Instead, tools should use `CameraManager.CaptureFrameAsync` everywhere. The
phone `CameraView` should be owned only by `PhoneCameraProvider`.

This makes external cameras and HeyCyan work consistently across:

- action buttons;
- realtime tool calls;
- wake-word quick actions;
- typed/voice requests during active sessions.

### 3. Consolidate Device Persistence

Pick one durable source of truth.

Recommended model:

- `DeviceSettings.ActiveProfileId` stores the chosen profile.
- `DeviceSettings.Custom` stores custom slot picks.
- `DeviceSettings.Active` stores current runtime slot state for diagnostics.
- flat `ActiveCameraProvider`, `ActiveAudioInputProvider`, and
  `ActiveAudioOutputProvider` become compatibility shims or are removed after
  migration.

### 4. Make Video A Separate Architecture

Do not stretch `ICameraProvider.CaptureFrameAsync` into video recording.

If video matters, add a dedicated abstraction, for example:

```csharp
public interface IVideoCaptureProvider
{
    string ProviderId { get; }
    bool IsAvailable { get; }
    Task StartRecordingAsync(CancellationToken ct);
    Task<VideoCaptureResult?> StopRecordingAsync(CancellationToken ct);
}
```

HeyCyan video can then be modeled as:

```text
BLE StartVideo
  -> wait/record
  -> BLE StopVideo
  -> media transfer
  -> MP4 result
```

A9/Vue990 can expose stream/video capture through the same shape without
forcing all still cameras to pretend they are video recorders.

### 5. Add Regression Tests Around The Split

Useful tests:

- active session tool calls use `CameraManager`, not direct `CameraView`;
- selecting HeyCyan profile makes Look, DescribeScene, TakePhoto, Scan, and
  StartSceneWatch use the HeyCyan camera;
- app startup applies the saved source profile before the first active session;
- custom source picker updates both runtime managers and `DeviceSettings.Custom`;
- connecting/disconnecting HeyCyan does not double-switch or fight between
  `SourceProfileManager` and `HeyCyanAudioRouter`.

## Suggested Next Milestone

If we implement after this report, the next phase should be:

```text
M48 - Unify Runtime Source Selection
```

Primary work:

- initialize `SourceProfileManager` at startup;
- remove active-session direct `CameraView` capture;
- make `DeviceSettings` the source selection persistence model;
- decide whether `HeyCyanAudioRouter` remains a low-level provider registrar or
  becomes part of the profile manager flow.
