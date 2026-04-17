# RCA-007 Fix — Migrate to CommunityToolkit.Maui.Camera

Replace the broken `CapturePhotoToStreamAsync` loop with a native `CameraView` for preview and `CaptureImage()` for frame grabs.

| Step | Title | Files Changed | Depends On |
|------|-------|---------------|------------|
| 1 | [Add NuGet + Register](#step-1-add-nuget--register) | `BodyCam.csproj`, `MauiProgram.cs` | — |
| 2 | [Replace XAML Preview](#step-2-replace-xaml-preview) | `MainPage.xaml`, `MainPage.xaml.cs` | 1 |
| 3 | [Simplify ICameraService](#step-3-simplify-icameraservice) | `ICameraService.cs`, `CameraService.cs` | 1 |
| 4 | [Rewrite WindowsCameraService](#step-4-rewrite-windowscameraservice) | `WindowsCameraService.cs` | 1, 3 |
| 5 | [Rewrite AndroidCameraService](#step-5-rewrite-androidcameraservice) | `AndroidCameraService.cs` | 1, 3 |
| 6 | [Update ViewModel — Remove Preview Loop](#step-6-update-viewmodel) | `MainViewModel.cs` | 2, 3 |
| 7 | [Update VisionAgent + Orchestrator](#step-7-update-visionagent--orchestrator) | `VisionAgent.cs`, `AgentOrchestrator.cs` | 3, 6 |
| 8 | [Update Tests](#step-8-update-tests) | `CameraServiceTests.cs`, `VisionAgentTests.cs`, `VisionAgentCachingTests.cs`, `AgentOrchestratorTests.cs` | 3–7 |

## Build & Verify

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -v q
```

---

## Step 1: Add NuGet + Register

### Goal
Add the `CommunityToolkit.Maui.Camera` NuGet package and register it in the MAUI builder.

### Changes

**`src/BodyCam/BodyCam.csproj`**
Add to the main (unconditional) `<ItemGroup>` with other package references:
```xml
<PackageReference Include="CommunityToolkit.Maui.Camera" Version="2.*" />
```

**`src/BodyCam/MauiProgram.cs`**
Add `using CommunityToolkit.Maui;` at the top.
Chain `.UseMauiCommunityToolkitCamera()` on the builder after `.UseMauiApp<App>()`:
```csharp
builder
    .UseMauiApp<App>()
    .UseMauiCommunityToolkitCamera()
    .ConfigureFonts(fonts => { ... });
```

### Verify
```powershell
dotnet restore src/BodyCam/BodyCam.csproj
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
```

---

## Step 2: Replace XAML Preview

### Goal
Replace the `Image` bound to `CameraPreview` with a native `CameraView` that renders directly from the camera hardware. The `CameraView` is a XAML control, not a ViewModel-bound image.

### Changes

**`src/BodyCam/MainPage.xaml`**

1. Add toolkit namespace:
```xml
xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit"
```

2. Replace the camera preview `Border` + `Image`:
```xml
<!-- OLD: -->
<Border StrokeShape="RoundRectangle 4" ...>
    <Image Source="{Binding CameraPreview}" Aspect="AspectFill" />
</Border>

<!-- NEW: -->
<Border StrokeShape="RoundRectangle 4"
        Stroke="{AppThemeBinding Light=#E0E0E0, Dark=#555}"
        BackgroundColor="Black"
        WidthRequest="160" HeightRequest="120"
        HorizontalOptions="End" VerticalOptions="Start"
        Margin="8"
        IsVisible="{Binding IsRunning}">
    <toolkit:CameraView x:Name="CameraPreview" />
</Border>
```

**`src/BodyCam/MainPage.xaml.cs`**

Expose the `CameraView` to the ViewModel so it can call `CaptureImage()`:
```csharp
public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.SetCameraView(CameraPreview);
        // ... existing CollectionChanged handler ...
    }
}
```

### Verify
Build succeeds (will have temporary errors until Step 6 adds `SetCameraView`).

---

## Step 3: Simplify ICameraService

### Goal
The `ICameraService` no longer needs `GetFramesAsync` or `CaptureFrameAsync` for UI preview — that's handled natively by `CameraView`. The interface now only exposes lifecycle management. Frame capture for the vision API will go through `CameraView.CaptureImage()` directly.

### Changes

**`src/BodyCam/Services/ICameraService.cs`**
```csharp
namespace BodyCam.Services;

public interface ICameraService
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    bool IsCapturing { get; }
}
```

Remove `CaptureFrameAsync` and `GetFramesAsync` — these methods move to the `CameraView` path.

**`src/BodyCam/Services/CameraService.cs`** (stub)
Update the stub to match the simplified interface — remove `CaptureFrameAsync` and `GetFramesAsync` methods.

### Verify
Build will fail until downstream consumers are updated (Steps 4–7).

---

## Step 4: Rewrite WindowsCameraService

### Goal
Strip `WindowsCameraService` down to lifecycle-only (Start/Stop). No more `MediaCapture` — the `CameraView` handles hardware access.

### Changes

**`src/BodyCam/Platforms/Windows/WindowsCameraService.cs`**
```csharp
using BodyCam.Services;

namespace BodyCam.Platforms.Windows;

public class WindowsCameraService : ICameraService
{
    public bool IsCapturing { get; private set; }

    public Task StartAsync(CancellationToken ct = default)
    {
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsCapturing = false;
        return Task.CompletedTask;
    }
}
```

The `CameraView` handler on Windows internally uses `MediaCapture` for the native surface. We don't need our own.

### Verify
Build (will still fail until Steps 6–7).

---

## Step 5: Rewrite AndroidCameraService

### Goal
Same simplification as Step 4 — lifecycle only.

### Changes

**`src/BodyCam/Platforms/Android/AndroidCameraService.cs`**
```csharp
using BodyCam.Services;

namespace BodyCam.Platforms.Android;

public class AndroidCameraService : ICameraService
{
    public bool IsCapturing { get; private set; }

    public Task StartAsync(CancellationToken ct = default)
    {
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsCapturing = false;
        return Task.CompletedTask;
    }
}
```

### Verify
Build (will still fail until Steps 6–7).

---

## Step 6: Update ViewModel

### Goal
Remove the `RefreshPreviewLoopAsync` loop, `CameraPreview` property, and `_previewCts`. Add `SetCameraView` to receive the XAML `CameraView` reference and a method to capture frames from it.

### Changes

**`src/BodyCam/ViewModels/MainViewModel.cs`**

1. **Remove** fields/properties:
   - `private ImageSource? _cameraPreview`
   - `private CancellationTokenSource? _previewCts`
   - `public ImageSource? CameraPreview { get; set; }`
   - `private async Task RefreshPreviewLoopAsync(CancellationToken ct)` (entire method)

2. **Add** `CameraView` reference and capture method:
```csharp
using CommunityToolkit.Maui.Views;

private CameraView? _cameraView;

public void SetCameraView(CameraView cameraView)
{
    _cameraView = cameraView;
}

/// <summary>
/// Captures a JPEG frame from the CameraView for the vision API.
/// </summary>
internal async Task<byte[]?> CaptureFrameFromCameraViewAsync(CancellationToken ct = default)
{
    if (_cameraView is null) return null;

    try
    {
        using var stream = await _cameraView.CaptureImage(ct);
        if (stream is null || stream.Length == 0) return null;

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
    catch
    {
        return null;
    }
}
```

3. **Update `ToggleAsync`** — remove `_previewCts` lifecycle:
   - In Start path: Remove `_previewCts = new CancellationTokenSource()` and `_ = RefreshPreviewLoopAsync(...)`. Instead, start the camera preview:
     ```csharp
     if (_cameraView is not null)
         await _cameraView.StartCameraPreview(CancellationToken.None);
     ```
   - In Stop path: Remove `_previewCts?.Cancel()`, `_previewCts?.Dispose()`, `CameraPreview = null`. Instead:
     ```csharp
     _cameraView?.StopCameraPreview();
     VisionStatus = null;
     ```

### Verify
Build (will still fail until Step 7).

---

## Step 7: Update VisionAgent + Orchestrator

### Goal
Wire the orchestrator to use the ViewModel's `CaptureFrameFromCameraViewAsync` instead of `ICameraService.CaptureFrameAsync`. The `VisionAgent.Camera` property goes away.

### Changes

**`src/BodyCam/Agents/VisionAgent.cs`**

1. Remove `ICameraService _camera` constructor parameter and field
2. Remove `public ICameraService Camera => _camera;` property
3. Remove `CaptureAndDescribeAsync` method (the orchestrator will call `DescribeFrameAsync` directly with bytes from the ViewModel)
4. Keep `DescribeFrameAsync(byte[] jpegFrame, ...)` unchanged — it's still the core vision call
5. Simplify constructor:
```csharp
public VisionAgent(IChatClient chatClient, AppSettings settings)
{
    _chatClient = chatClient;
    _settings = settings;
}
```

**`src/BodyCam/Orchestration/AgentOrchestrator.cs`**

1. Add a `Func<CancellationToken, Task<byte[]?>>? FrameCaptureFunc` property that the ViewModel sets:
```csharp
public Func<CancellationToken, Task<byte[]?>>? FrameCaptureFunc { get; set; }
```

2. Update `ExecuteDescribeSceneAsync` — use `FrameCaptureFunc` instead of `_vision.CaptureAndDescribeAsync`:
```csharp
private async Task<string> ExecuteDescribeSceneAsync(string? argumentsJson = null)
{
    string? userPrompt = null;
    if (argumentsJson is not null)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
        if (doc.RootElement.TryGetProperty("query", out var q))
            userPrompt = q.GetString();
    }

    // Rate-limit: return cached description if within cooldown
    if (_vision.LastDescription is not null
        && DateTimeOffset.UtcNow - _lastVisionTime < TimeSpan.FromSeconds(5))
    {
        return System.Text.Json.JsonSerializer.Serialize(new { description = _vision.LastDescription });
    }

    byte[]? frame = null;
    if (FrameCaptureFunc is not null)
        frame = await FrameCaptureFunc(_cts?.Token ?? CancellationToken.None);

    if (frame is null)
        return System.Text.Json.JsonSerializer.Serialize(new { description = "Camera not available." });

    var description = await _vision.DescribeFrameAsync(frame, userPrompt);
    Session.LastVisionDescription = description;
    _lastVisionTime = DateTimeOffset.UtcNow;

    return System.Text.Json.JsonSerializer.Serialize(new { description });
}
```

3. Add field: `private DateTimeOffset _lastVisionTime = DateTimeOffset.MinValue;`

4. **Remove** `CapturePreviewFrameAsync` method entirely

5. **Update `StartAsync`** — remove `await _vision.Camera.StartAsync(...)` camera startup block

6. **Update `StopAsync`** — remove `await _vision.Camera.StopAsync()` try/catch block

**`src/BodyCam/ViewModels/MainViewModel.cs`** (additional wiring)

In the Start path of `ToggleAsync`, after creating the orchestrator, set the frame capture delegate:
```csharp
_orchestrator.FrameCaptureFunc = CaptureFrameFromCameraViewAsync;
```

**`src/BodyCam/MauiProgram.cs`**

Update VisionAgent registration to no longer pass `ICameraService`:
```csharp
// OLD:
builder.Services.AddSingleton<VisionAgent>();

// If VisionAgent used DI constructor injection for ICameraService, update.
// The new constructor is: VisionAgent(IChatClient chatClient, AppSettings settings)
// Register with factory:
builder.Services.AddSingleton<VisionAgent>(sp =>
    new VisionAgent(
        sp.GetRequiredService<IChatClient>(),
        sp.GetRequiredService<AppSettings>()));
```

Also consider: if `ICameraService` is no longer used by `VisionAgent` or `AgentOrchestrator`, and the lifecycle is now just bool flags, evaluate whether the DI registration for `ICameraService` is still needed. Keep it for now if the orchestrator Start/Stop still calls it.

### Verify
```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None -v q
```

---

## Step 8: Update Tests

### Goal
Fix all existing tests to match the simplified signatures. No new test classes needed.

### Changes

**`src/BodyCam.Tests/Services/CameraServiceTests.cs`**
Remove tests for `CaptureFrameAsync_ReturnsNull_Stub` and `GetFramesAsync_YieldsNothing_Stub`. Keep `StartAsync_SetsIsCapturing` and `StopAsync_ClearsIsCapturing`.

**`src/BodyCam.Tests/Agents/VisionAgentTests.cs`**
- Remove `ICameraService` mock from test setup
- Update `new VisionAgent(camera, chatClient, settings)` → `new VisionAgent(chatClient, settings)`
- Update any tests that called `CaptureAndDescribeAsync` — they should call `DescribeFrameAsync` with a byte array directly

**`src/BodyCam.Tests/Agents/VisionAgentCachingTests.cs`**
- Remove `ICameraService _camera` field
- Update `CreateAgent()` → `new VisionAgent(_chatClient, _settings)`
- Update caching tests: instead of testing `CaptureAndDescribeAsync` cooldown through the camera mock, test caching at the orchestrator level or remove cooldown tests (cooldown logic moved to orchestrator)

**`src/BodyCam.Tests/Orchestration/AgentOrchestratorTests.cs`**
- Update `CreateOrchestrator` helper: `VisionAgent` no longer takes `ICameraService`
- Remove camera-related orchestrator tests that tested `CapturePreviewFrameAsync`
- Update any `_vision.Camera.StartAsync()` expectations

### Verify
```powershell
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -v q
# Expected: all tests pass (some test count reduction from removed tests)
```

---

## Summary

| Metric | Before | After |
|--------|--------|-------|
| Camera preview | 2fps JPEG decode loop, goes black after 1 frame | 30fps native GPU surface |
| Frame capture | `CapturePhotoToStreamAsync` (breaks after 1 call) | `CameraView.CaptureImage()` (designed for repeat use) |
| Platform code | ~100 lines WindowsCameraService + ~80 lines AndroidCameraService | ~15 lines each (lifecycle only) |
| Preview loop | `RefreshPreviewLoopAsync` + `ImageSource.FromStream` (leaks) | None — native rendering |
| Dependencies | WinRT MediaCapture + Camera2 | `CommunityToolkit.Maui.Camera` (cross-platform) |
