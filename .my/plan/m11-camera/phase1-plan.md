# M11 Phase 1 — Camera Abstraction & Phone Camera

**Status:** NOT STARTED  
**Prerequisite:** None (first phase)  
**Goal:** Extract the tightly-coupled CameraView code from MainViewModel into a
proper `ICameraProvider` → `CameraManager` → `AgentOrchestrator` pipeline.
All existing features continue to work. Headless capture enabled.

---

## Current State (What Exists)

| Component | Location | Problem |
|-----------|----------|---------|
| `_cameraView` field | `MainViewModel` (line 29) | ViewModel directly owns a UI control |
| `SetCameraView()` | `MainViewModel` (line 446) | Page passes UI control to ViewModel |
| `CaptureFrameFromCameraViewAsync()` | `MainViewModel` (line 454) | 50 lines of capture logic in ViewModel |
| `FrameCaptureFunc` delegate | `AgentOrchestrator` (line 30) | ViewModel manually wires this on session start (line 406) |
| `ICameraService` stubs | `Services/ICameraService.cs` | Empty stubs — `IsCapturing` bool only, not used for capture |
| CameraView XAML | `MainPage.xaml` (line ~135) | `IsVisible="{Binding ShowCameraTab}"` — hidden when on transcript tab |

**Pain points:**
- Frame capture fails when camera tab is hidden (CameraView not rendering)
- ViewModel has a direct reference to a XAML control (breaks MVVM)
- No way to swap camera sources — hardcoded to CameraView
- Orchestrator depends on ViewModel setting `FrameCaptureFunc` at session start

---

## Deliverables

### New Files

| File | Purpose |
|------|---------|
| `Services/Camera/ICameraProvider.cs` | Interface — all camera sources implement this |
| `Services/Camera/CameraManager.cs` | Manages active provider, provides capture delegate |
| `Services/Camera/PhoneCameraProvider.cs` | Wraps CameraView, supports headless capture |

### Modified Files

| File | Change |
|------|--------|
| `MainPage.xaml.cs` | Wire CameraView to `PhoneCameraProvider` instead of ViewModel |
| `MainPage.xaml` | Possibly swap `IsVisible` for `Opacity="0" HeightRequest="1"` on hidden camera |
| `ViewModels/MainViewModel.cs` | Remove `_cameraView`, `SetCameraView`, `CaptureFrameFromCameraViewAsync`; inject `CameraManager` |
| `Orchestration/AgentOrchestrator.cs` | Inject `CameraManager`, replace `FrameCaptureFunc` delegate |
| `MauiProgram.cs` | Register `PhoneCameraProvider`, `CameraManager` in DI |
| `Services/ISettingsService.cs` | Add `ActiveCameraProvider` property |

### Removed Code

| What | Where |
|------|-------|
| `CameraView? _cameraView` field | `MainViewModel` |
| `SetCameraView(CameraView)` method | `MainViewModel` |
| `CaptureFrameFromCameraViewAsync()` method | `MainViewModel` |
| `FrameCaptureFunc` property | `AgentOrchestrator` (replaced by injected `CameraManager`) |
| `_orchestrator.FrameCaptureFunc = ...` | `MainViewModel.SetLayerAsync` |

---

## Implementation Waves

### Wave 1: Interface + Provider (no integration yet)

Create the new files without modifying any existing code. Compile, run tests.

**1.1 — Create `ICameraProvider` interface**

```csharp
// Services/Camera/ICameraProvider.cs
namespace BodyCam.Services.Camera;

public interface ICameraProvider : IAsyncDisposable
{
    string DisplayName { get; }
    string ProviderId { get; }
    bool IsAvailable { get; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();

    Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default);
    IAsyncEnumerable<byte[]> StreamFramesAsync(CancellationToken ct);

    event EventHandler? Disconnected;
}
```

**1.2 — Create `PhoneCameraProvider`**

Extract `CaptureFrameFromCameraViewAsync` logic from `MainViewModel` (lines 454–497):

- `SetCameraView(CameraView)` — called by `MainPage.xaml.cs`
- `CaptureFrameAsync` — if preview not started, start it (500ms warm-up), capture, stop if we started it
- `CaptureViaEventAsync` — subscribe to `MediaCaptured` event, call `CaptureImage`, wait for result
- All CameraView calls dispatched via `MainThread.InvokeOnMainThreadAsync()`

Key logic for headless capture:
```csharp
public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
{
    if (_cameraView is null) return null;

    bool ownedStart = !_started;
    if (ownedStart)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
            await _cameraView.StartCameraPreview(ct));
        _started = true;
        await Task.Delay(500, ct); // warm-up
    }

    try { return await CaptureViaEventAsync(ct); }
    finally
    {
        if (ownedStart)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                _cameraView.StopCameraPreview());
            _started = false;
        }
    }
}
```

**1.3 — Create `CameraManager`**

- Constructor takes `IEnumerable<ICameraProvider>` + `ISettingsService`
- `Active` property — current provider
- `SetActiveAsync(string providerId)` — stop old, start new, persist choice
- `CaptureFrameAsync(CancellationToken)` — delegate to active provider
- `InitializeAsync()` — restore from settings, default to "phone"
- `OnProviderDisconnected` — fallback to phone camera

**Verify:** All three files compile. No existing code changed yet.

---

### Wave 2: Wire into DI + MainPage

**2.1 — Register in `MauiProgram.cs`**

```csharp
// Camera abstraction
builder.Services.AddSingleton<ICameraProvider, PhoneCameraProvider>();
builder.Services.AddSingleton<CameraManager>();
```

**2.2 — Update `MainPage.xaml.cs`**

Replace:
```csharp
viewModel.SetCameraView(CameraPreview);
```
With:
```csharp
var phoneCamera = handler.MauiContext!.Services.GetRequiredService<IEnumerable<ICameraProvider>>()
    .OfType<PhoneCameraProvider>()
    .FirstOrDefault();
phoneCamera?.SetCameraView(CameraPreview);
```

Or resolve `PhoneCameraProvider` directly if registered as concrete type.

**2.3 — XAML: Enable headless capture**

If testing shows headless capture fails with `IsVisible="False"`, change the
camera Grid from:
```xml
<Grid IsVisible="{Binding ShowCameraTab}">
```
To:
```xml
<Grid Opacity="{Binding CameraTabOpacity}" HeightRequest="{Binding CameraTabHeight}">
```
Where `CameraTabOpacity` is 1.0 when active, 0.01 when hidden, and
`CameraTabHeight` is -1 (auto) when active, 1 when hidden.

**Test first with `IsVisible` — may work fine on Windows.** Only apply the
opacity workaround if black frames are observed.

**Verify:** App starts, PhoneCameraProvider receives CameraView, DI resolves.

---

### Wave 3: Rewire AgentOrchestrator

**3.1 — Inject `CameraManager` into `AgentOrchestrator`**

Add constructor parameter:
```csharp
private readonly CameraManager _cameraManager;

public AgentOrchestrator(
    ...,
    CameraManager cameraManager)
{
    _cameraManager = cameraManager;
}
```

**3.2 — Replace `FrameCaptureFunc`**

In `CreateToolContext()`, replace:
```csharp
CaptureFrame = FrameCaptureFunc ?? ((ct) => Task.FromResult<byte[]?>(null)),
```
With:
```csharp
CaptureFrame = _cameraManager.CaptureFrameAsync,
```

**3.3 — Remove `FrameCaptureFunc` property**

Delete:
```csharp
public Func<CancellationToken, Task<byte[]?>>? FrameCaptureFunc { get; set; }
```

**Verify:** Build succeeds (will break MainViewModel — expected, fixed in Wave 4).

---

### Wave 4: Clean up MainViewModel

**4.1 — Inject `CameraManager`**

Add to constructor:
```csharp
private readonly CameraManager _cameraManager;

public MainViewModel(
    ...,
    CameraManager cameraManager)
{
    _cameraManager = cameraManager;
}
```

**4.2 — Remove camera coupling**

Delete from MainViewModel:
- `private CameraView? _cameraView;` (line 29)
- `public void SetCameraView(CameraView cameraView)` method (line 446–449)
- `internal async Task<byte[]?> CaptureFrameFromCameraViewAsync(...)` method (lines 454–497)

**4.3 — Update `SendVisionCommandAsync`**

Replace:
```csharp
if (_cameraView is not null)
    await _cameraView.StartCameraPreview(CancellationToken.None);
var frame = await CaptureFrameFromCameraViewAsync();
```
With:
```csharp
var frame = await _cameraManager.CaptureFrameAsync();
```

**4.4 — Update `SetLayerAsync`**

Remove:
```csharp
_orchestrator.FrameCaptureFunc = CaptureFrameFromCameraViewAsync;
if (_cameraView is not null)
    await _cameraView.StartCameraPreview(CancellationToken.None);
```

Camera start is now handled by `CameraManager` — no need for the ViewModel
to start the preview when a session begins.

**4.5 — Update camera tab switching**

Replace direct `_cameraView.StartCameraPreview()` / `StopCameraPreview()` calls
in the tab switching logic with `_cameraManager.Active?.StartAsync()` /
`StopAsync()` if needed, or let `PhoneCameraProvider` manage this internally.

**Verify:** Full build, app runs, Look/Read/Find/Photo buttons work, both
direct-mode and Realtime-session mode.

---

### Wave 5: Settings + Camera Picker UI

**5.1 — Add `ActiveCameraProvider` to settings**

```csharp
// ISettingsService
string? ActiveCameraProvider { get; set; }
```

In the implementation, persist via `Preferences.Get/Set`.

**5.2 — Initialize CameraManager on app start**

In `App.xaml.cs` or `MainPage` loaded handler:
```csharp
await cameraManager.InitializeAsync();
```

**5.3 — Camera picker on Settings page**

Add a `Picker` to SettingsPage:
```xml
<Picker Title="Camera Source"
        ItemsSource="{Binding CameraProviders}"
        ItemDisplayBinding="{Binding DisplayName}"
        SelectedItem="{Binding SelectedCameraProvider}" />
```

SettingsViewModel exposes:
```csharp
public IReadOnlyList<ICameraProvider> CameraProviders => _cameraManager.Providers;
public ICameraProvider? SelectedCameraProvider { get => ...; set => ... }
```

For Phase 1, only "Phone Camera" appears. Future phases add providers.

**Verify:** Settings page shows picker with "Phone Camera" selected. Changing
selection (when more providers exist) switches the active camera.

---

## ICameraService Disposition

The existing `ICameraService` interface and its stubs (`WindowsCameraService`,
`AndroidCameraService`, `CameraService`) are **not used for actual frame capture**.
They only toggle an `IsCapturing` boolean.

**Decision:** Leave them in place for now. They may become platform-specific
lifecycle helpers (e.g. requesting Android camera permissions). Do not remove
them in Phase 1 to avoid unnecessary churn. Revisit in Phase 2.

---

## Test Plan

### Unit Tests (BodyCam.Tests)

| Test | Validates |
|------|-----------|
| `CameraManager_DefaultsToPhone` | `InitializeAsync()` selects "phone" when no saved pref |
| `CameraManager_RestoresSavedProvider` | Reads `ActiveCameraProvider` from settings |
| `CameraManager_FallbackOnDisconnect` | When active provider fires `Disconnected`, switches to phone |
| `CameraManager_StopsOldProviderOnSwitch` | `SetActiveAsync` stops previous before starting new |
| `CameraManager_CaptureFrame_DelegatesToActive` | `CaptureFrameAsync` calls active provider |
| `CameraManager_CaptureFrame_NullWhenNoActive` | Returns null when no provider is active |

### Integration Tests (manual)

| Scenario | Expected |
|----------|----------|
| App starts → camera tab → preview visible | Phone camera shows preview |
| Transcript tab → Look button (direct mode) | Headless capture → vision response in transcript |
| Start Realtime session → say "what do you see?" | Tool captures frame via CameraManager → describes scene |
| Settings → Camera picker shows "Phone Camera" | Single option, selected |

### Regression

All existing vision features must continue to work:
- Look / Read / Find / Photo buttons (both direct and Realtime modes)
- `describe_scene`, `read_text`, `find_object` tools via Realtime API
- Camera preview on camera tab
- Tab switching doesn't break capture

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Headless capture returns black frames on Android | Swap `IsVisible="False"` for `Opacity="0.01" HeightRequest="1"` |
| `StartCameraPreview` throws when CameraView not in visual tree | Ensure CameraView is always in layout (just hidden) |
| Thread safety — CameraView ops must be on main thread | All CameraView calls wrapped in `MainThread.InvokeOnMainThreadAsync()` |
| DI ordering — PhoneCameraProvider created before CameraView exists | `SetCameraView()` is deferred — called from `MainPage.Loaded` |
| Warm-up delay too short/long | 500ms default, configurable. Test on physical Android device |
| Removing FrameCaptureFunc breaks something | Search for all usages before removing — only MainViewModel sets it |

---

## Exit Criteria

- [ ] `ICameraProvider` interface created
- [ ] `PhoneCameraProvider` implements the interface wrapping CameraView
- [ ] `CameraManager` manages active provider with phone camera default
- [ ] `AgentOrchestrator` uses `CameraManager` directly (no `FrameCaptureFunc`)
- [ ] `MainViewModel` no longer references `CameraView` directly
- [ ] Headless capture works (frame capture when camera tab is hidden)
- [ ] All existing vision features pass regression
- [ ] Settings page has camera picker (single "Phone Camera" entry for now)
- [ ] Build succeeds on both Windows and Android targets
