# M11 — Camera Abstraction Layer

## ICameraProvider

The central interface all camera sources implement. One method for on-demand
frame capture, one for continuous streaming, and lifecycle management.

```csharp
namespace BodyCam.Services.Camera;

/// <summary>
/// A camera source that can capture JPEG frames.
/// Only one provider is active at a time, managed by CameraManager.
/// </summary>
public interface ICameraProvider : IAsyncDisposable
{
    /// <summary>
    /// Human-readable name for the camera source (e.g. "Phone Camera", "Meta Ray-Ban").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Unique identifier for this provider type (e.g. "phone", "usb", "meta", "wifi").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Whether the camera hardware is currently connected and ready to capture.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Initialize the camera hardware. Call before CaptureFrameAsync.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Release the camera hardware. Idempotent.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Capture a single JPEG frame. Returns null if capture fails.
    /// Starts the camera if not already started (with warm-up delay).
    /// </summary>
    Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default);

    /// <summary>
    /// Stream continuous JPEG frames. Used for live preview or continuous monitoring.
    /// Optional — not all providers support this.
    /// </summary>
    IAsyncEnumerable<byte[]> StreamFramesAsync(CancellationToken ct);

    /// <summary>
    /// Raised when the camera disconnects unexpectedly (e.g. glasses turned off, USB unplugged).
    /// </summary>
    event EventHandler? Disconnected;
}
```

### Design Decisions

**JPEG bytes as the interchange format.** All providers return `byte[]` containing
JPEG data. This is the format VisionAgent already expects, and JPEG is universally
supported across camera hardware and network streams. Converting from platform-native
formats (NV21, BGRA, etc.) to JPEG happens inside each provider.

**Start/Stop lifecycle.** Providers manage their own hardware lifecycle. `CaptureFrameAsync`
auto-starts if needed (with warm-up delay), so callers don't need to worry about state.
But explicit `StartAsync` allows pre-warming for better latency.

**One active at a time.** The `CameraManager` enforces this. When switching providers,
it stops the current one before starting the new one. This avoids hardware conflicts
(e.g. two processes trying to open the same USB camera).

---

## CameraManager

Manages the active camera provider and provides the `FrameCaptureFunc` delegate
to the orchestrator.

```csharp
namespace BodyCam.Services.Camera;

/// <summary>
/// Manages available camera providers and the currently active one.
/// Provides frame capture to the orchestrator.
/// </summary>
public class CameraManager
{
    private readonly IReadOnlyList<ICameraProvider> _providers;
    private readonly ISettingsService _settings;
    private ICameraProvider? _active;

    public CameraManager(
        IEnumerable<ICameraProvider> providers,
        ISettingsService settings)
    {
        _providers = providers.ToList();
        _settings = settings;
    }

    /// <summary>All registered camera providers.</summary>
    public IReadOnlyList<ICameraProvider> Providers => _providers;

    /// <summary>The currently active camera provider.</summary>
    public ICameraProvider? Active => _active;

    /// <summary>
    /// Select and activate a camera provider by its ProviderId.
    /// Stops the previous provider if one was active.
    /// </summary>
    public async Task SetActiveAsync(string providerId, CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId)
            ?? throw new ArgumentException($"Unknown camera provider: {providerId}");

        if (_active is not null && _active != provider)
            await _active.StopAsync();

        _active = provider;
        _settings.ActiveCameraProvider = providerId;

        // Subscribe to disconnection for fallback
        provider.Disconnected += OnProviderDisconnected;
        await provider.StartAsync(ct);
    }

    /// <summary>
    /// Capture a frame from the active provider.
    /// This is the delegate wired to AgentOrchestrator.FrameCaptureFunc.
    /// </summary>
    public async Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default)
    {
        if (_active is null)
            await FallbackToPhoneAsync(ct);

        return _active is not null
            ? await _active.CaptureFrameAsync(ct)
            : null;
    }

    /// <summary>
    /// Initialize: restore last-used provider from settings, or default to phone.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var savedId = _settings.ActiveCameraProvider ?? "phone";
        var provider = _providers.FirstOrDefault(p => p.ProviderId == savedId)
            ?? _providers.FirstOrDefault(p => p.ProviderId == "phone");

        if (provider is not null)
            await SetActiveAsync(provider.ProviderId, ct);
    }

    private async void OnProviderDisconnected(object? sender, EventArgs e)
    {
        if (sender is ICameraProvider p)
            p.Disconnected -= OnProviderDisconnected;

        // Fallback to phone camera
        await FallbackToPhoneAsync();
    }

    private async Task FallbackToPhoneAsync(CancellationToken ct = default)
    {
        var phone = _providers.FirstOrDefault(p => p.ProviderId == "phone");
        if (phone is not null && phone != _active)
            await SetActiveAsync("phone", ct);
    }
}
```

---

## DI Registration

```csharp
// In MauiProgram.cs

// Camera providers (registered as individual singletons)
builder.Services.AddSingleton<ICameraProvider, PhoneCameraProvider>();
#if WINDOWS
builder.Services.AddSingleton<ICameraProvider, UsbCameraProvider>();
#endif
builder.Services.AddSingleton<ICameraProvider, IpCameraProvider>();
// Add more providers as they're implemented

// Camera manager
builder.Services.AddSingleton<CameraManager>();
```

---

## Integration with Existing Code

### AgentOrchestrator

Replace the current `FrameCaptureFunc` delegate pattern with `CameraManager`:

```csharp
// Before (current)
public Func<CancellationToken, Task<byte[]?>>? FrameCaptureFunc { get; set; }

// After
private readonly CameraManager _cameraManager;

// In CreateToolContext():
CaptureFrame = _cameraManager.CaptureFrameAsync,
```

This removes the need for `MainViewModel` to set `FrameCaptureFunc` and the
dependency on CameraView from the ViewModel.

### MainViewModel

The ViewModel no longer owns camera capture. Instead of calling
`CaptureFrameFromCameraViewAsync()`, it uses the orchestrator's camera
(which goes through `CameraManager`).

```csharp
// Before (current)
var frame = await CaptureFrameFromCameraViewAsync();

// After
var frame = await _cameraManager.CaptureFrameAsync();
```

### SendVisionCommandAsync (direct mode)

When no Realtime session is running, the ViewModel captures directly from
`CameraManager` instead of `CaptureFrameFromCameraViewAsync`:

```csharp
private async Task SendVisionCommandAsync(string prompt)
{
    if (IsRunning)
    {
        await _orchestrator.SendTextInputAsync(prompt);
        return;
    }

    var frame = await _cameraManager.CaptureFrameAsync();
    // ... add to transcript with image ...
}
```

---

## Settings

New settings for camera selection:

```csharp
// ISettingsService additions
string? ActiveCameraProvider { get; set; }  // "phone", "usb", "wifi:192.168.1.50", "meta", etc.
```

Settings UI: dropdown/picker listing available providers with their `DisplayName`.
Updates on provider availability changes (USB plugged/unplugged, glasses
connect/disconnect).
