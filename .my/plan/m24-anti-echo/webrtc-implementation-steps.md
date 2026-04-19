# M24 — WebRTC AEC: Implementation Steps

Implements the approach described in `browser-webrtc-approach.md`.
Target: Android first, then Windows, then iOS.

---

## Step 1 — Create the audio bridge HTML/JS

**File**: `src/BodyCam/Resources/Raw/audio-bridge.html`

Single HTML page loaded in a hidden WebView. Contains all JavaScript for mic capture and speaker playback.

### JavaScript responsibilities

1. **Mic capture** with AEC:
   ```js
   const stream = await navigator.mediaDevices.getUserMedia({
     audio: {
       echoCancellation: true,
       noiseSuppression: true,
       autoGainControl: true,
       sampleRate: 24000,   // hint — browser may ignore
       channelCount: 1
     }
   });
   ```

2. **AudioWorklet processor** to extract raw PCM chunks:
   - Register an `AudioWorkletProcessor` subclass
   - In `process()`, convert Float32 samples to Int16 PCM bytes
   - Post chunks to the main thread via `port.postMessage()`
   - Main thread sends chunks to C# via the JS bridge

3. **Playback pipeline** for AI response audio:
   - Receive base64-encoded PCM chunks from C#
   - Decode to Float32, push into an `AudioWorklet` playback node connected to `destination`
   - This audio goes through the speakers — the browser AEC uses it as the reference signal automatically

4. **JS bridge messages** (C# ↔ JS):
   - **JS → C#**: `window.chrome.webview.postMessage(base64PcmChunk)` (Android/Windows)
   - **C# → JS**: `webView.EvaluateJavaScriptAsync("playChunk('base64data')")` (all platforms)
   - **Control messages**: `start`, `stop`, `playChunk(data)`, `setVolume(v)`

### AudioWorklet vs ScriptProcessorNode

Use `AudioWorklet` (modern, runs on audio thread, low latency). `ScriptProcessorNode` is deprecated and runs on the main thread. All target WebView engines support `AudioWorklet`:
- Chrome/WebView Android: since v64
- WebView2 (Windows): yes
- Safari/WKWebView: since v14.1

### Sample rate handling

`getUserMedia` may capture at 48000 Hz even if we request 24000. Options:
- **Option A**: Request `sampleRate: 24000` — if the browser honors it, no resampling needed
- **Option B**: Capture at whatever rate the browser gives, resample in JS before sending to C#
- **Option C**: Capture at native rate, let C# resample (adds complexity)

**Recommendation**: Option A first, fall back to B. Check `audioContext.sampleRate` after creation. If it's not 24000, add a simple linear resampler in the AudioWorklet.

### Chunk size

Match existing app chunk size: `24000 * 2 * 50 / 1000 = 2400 bytes` per 50ms chunk (PCM16 mono).
AudioWorklet `process()` delivers 128-sample frames. Accumulate ~18.75 frames (1200 samples) before posting a chunk. At 48kHz, accumulate ~37.5 frames (2400 samples) then resample to 1200 samples.

### Skeleton

```html
<!DOCTYPE html>
<html><head><meta charset="utf-8"></head>
<body>
<script>
  let audioCtx, micStream, captureNode, playbackNode;
  const TARGET_RATE = 24000;
  const CHUNK_SAMPLES = 1200; // 50ms at 24kHz

  async function start() {
    audioCtx = new AudioContext({ sampleRate: TARGET_RATE });
    micStream = await navigator.mediaDevices.getUserMedia({
      audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true }
    });

    await audioCtx.audioWorklet.addModule('data:text/javascript,' + encodeURIComponent(workletCode));
    const micSource = audioCtx.createMediaStreamSource(micStream);
    captureNode = new AudioWorkletNode(audioCtx, 'capture-processor');
    captureNode.port.onmessage = (e) => {
      // e.data is Int16Array → convert to base64 → send to C#
      const b64 = int16ToBase64(e.data);
      if (window.chrome?.webview) {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'audio', data: b64 }));
      }
    };
    micSource.connect(captureNode);
    // Don't connect captureNode to destination — it's a sink

    // Playback node for AI response
    playbackNode = new AudioWorkletNode(audioCtx, 'playback-processor');
    playbackNode.connect(audioCtx.destination);
  }

  function playChunk(base64Pcm) {
    const samples = base64ToInt16(base64Pcm);
    playbackNode.port.postMessage(samples);
  }

  function stop() {
    micStream?.getTracks().forEach(t => t.stop());
    audioCtx?.close();
  }

  // Worklet code as string (inlined to avoid separate file + CORS)
  const workletCode = `
    class CaptureProcessor extends AudioWorkletProcessor {
      constructor() { super(); this._buffer = new Float32Array(${CHUNK_SAMPLES}); this._pos = 0; }
      process(inputs) {
        const input = inputs[0]?.[0];
        if (!input) return true;
        for (let i = 0; i < input.length; i++) {
          this._buffer[this._pos++] = input[i];
          if (this._pos >= ${CHUNK_SAMPLES}) {
            const pcm = new Int16Array(${CHUNK_SAMPLES});
            for (let j = 0; j < ${CHUNK_SAMPLES}; j++)
              pcm[j] = Math.max(-32768, Math.min(32767, Math.floor(this._buffer[j] * 32767)));
            this.port.postMessage(pcm);
            this._pos = 0;
          }
        }
        return true;
      }
    }
    class PlaybackProcessor extends AudioWorkletProcessor {
      constructor() { super(); this._queue = []; this._pos = 0;
        this.port.onmessage = (e) => this._queue.push(e.data);
      }
      process(_, outputs) {
        const out = outputs[0]?.[0];
        if (!out) return true;
        for (let i = 0; i < out.length; i++) {
          if (this._queue.length > 0) {
            out[i] = this._queue[0][this._pos++] / 32768;
            if (this._pos >= this._queue[0].length) { this._queue.shift(); this._pos = 0; }
          } else { out[i] = 0; }
        }
        return true;
      }
    }
    registerProcessor('capture-processor', CaptureProcessor);
    registerProcessor('playback-processor', PlaybackProcessor);
  `;

  // Base64 helpers
  function int16ToBase64(int16arr) { /* ... */ }
  function base64ToInt16(b64) { /* ... */ }
</script>
</body></html>
```

### Acceptance criteria

- [ ] WebView loads HTML without errors
- [ ] `getUserMedia` succeeds and echoCancellation is confirmed active
- [ ] AudioWorklet captures PCM chunks at correct rate and size
- [ ] Playback node plays audio through speakers
- [ ] JS bridge sends/receives messages to/from C#

---

## Step 2 — Create `WebViewAudioBridge` C# service

**File**: `src/BodyCam/Services/Audio/WebViewAudioBridge.cs`

Manages the WebView element and JS interop. Shared by both input and output providers.

### Interface

```csharp
public sealed class WebViewAudioBridge : IAsyncDisposable
{
    // Set by MainPage after WebView is created
    public void SetWebView(WebView webView);

    // Control
    public Task InitializeAsync(CancellationToken ct = default);
    public Task StartCaptureAsync(CancellationToken ct = default);
    public Task StopCaptureAsync();
    public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default);
    public void ClearPlaybackBuffer();

    // Events
    public event EventHandler<byte[]>? AudioCaptured;      // clean PCM from JS
    public event EventHandler? CaptureStarted;
    public event EventHandler? CaptureStopped;
}
```

### Key implementation details

1. **WebView message handler**: Subscribe to the WebView's `WebMessageReceived` event (platform-specific).  
   - Android: Use `WebView.AddJavascriptInterface` or the MAUI `WebView` eval pattern  
   - Windows: `CoreWebView2.WebMessageReceived` (WebView2)  
   - MAUI abstraction: Use `webView.EvaluateJavaScriptAsync()` for C#→JS and handle JS→C# via a custom URL scheme or `window.external.notify`

2. **Message protocol** (JS → C#):
   ```json
   { "type": "audio", "data": "<base64 PCM>" }
   { "type": "ready" }
   { "type": "error", "message": "..." }
   ```

3. **C# → JS calls**:
   ```csharp
   await _webView.EvaluateJavaScriptAsync("start()");
   await _webView.EvaluateJavaScriptAsync($"playChunk('{base64}')");
   await _webView.EvaluateJavaScriptAsync("stop()");
   ```

4. **Thread safety**: Audio chunks arrive from JS on the UI thread. Fire `AudioCaptured` on a background thread or let consumers handle it.

5. **Lifecycle**: WebView must be kept alive while audio is flowing. Handle app backgrounding (Android `OnPause`/`OnResume`).

### MAUI WebView JS interop specifics

MAUI's `WebView` doesn't have a built-in C#←JS message channel. Options:

- **Option A — URL navigation interception**: JS sets `window.location = 'bridge://audio/base64data'`, C# intercepts in `Navigating` event. Limited by URL length (~2MB on most platforms, but adds overhead).
- **Option B — Platform handler**: Access the native WebView via `webView.Handler.PlatformView` and use platform-specific JS interop:
  - Android: `Android.Webkit.WebView.AddJavascriptInterface()`
  - Windows: `Microsoft.Web.WebView2.Core.CoreWebView2.WebMessageReceived`
- **Option C — Polling via EvaluateJavaScriptAsync**: C# polls JS for queued chunks. Adds latency.

**Recommendation**: Option B — platform handler. Direct native interop gives lowest latency and most reliable delivery for audio chunks.

### Acceptance criteria

- [ ] C# receives audio chunk events from WebView JS
- [ ] C# can send playback chunks to WebView
- [ ] Start/stop lifecycle works cleanly
- [ ] No memory leaks from base64 string allocations

---

## Step 3 — Create `WebViewMicProvider` (IAudioInputProvider)

**File**: `src/BodyCam/Services/Audio/WebViewMicProvider.cs`

Implements `IAudioInputProvider` using the `WebViewAudioBridge`.

```csharp
public sealed class WebViewMicProvider : IAudioInputProvider
{
    private readonly WebViewAudioBridge _bridge;

    public string DisplayName => "WebView Mic (AEC)";
    public string ProviderId => "webview";
    public bool IsAvailable => _bridge.IsReady;
    public bool IsCapturing { get; private set; }

    public event EventHandler<byte[]>? AudioChunkAvailable;
    public event EventHandler? Disconnected;

    public WebViewMicProvider(WebViewAudioBridge bridge)
    {
        _bridge = bridge;
        _bridge.AudioCaptured += (_, chunk) => AudioChunkAvailable?.Invoke(this, chunk);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsCapturing) return;
        await _bridge.StartCaptureAsync(ct);
        IsCapturing = true;
    }

    public async Task StopAsync()
    {
        if (!IsCapturing) return;
        await _bridge.StopCaptureAsync();
        IsCapturing = false;
    }

    public ValueTask DisposeAsync() => default;
}
```

### How it replaces PlatformMicProvider

`WebViewMicProvider` registers as an additional `IAudioInputProvider` with ProviderId `"webview"`. Both providers coexist — user can switch in Settings or we default to `"webview"` when available.

No changes needed to `VoiceInputAgent` or `AudioInputManager` — they work with `IAudioInputProvider` via the manager and don't care where audio comes from.

### Acceptance criteria

- [ ] `AudioInputManager` sees `"webview"` provider
- [ ] Switching to `"webview"` provider captures audio with AEC applied
- [ ] Switching back to `"platform"` provider works (fallback)

---

## Step 4 — Create `WebViewSpeakerProvider` (IAudioOutputProvider)

**File**: `src/BodyCam/Services/Audio/WebViewSpeakerProvider.cs`

Implements `IAudioOutputProvider` using the `WebViewAudioBridge`.

```csharp
public sealed class WebViewSpeakerProvider : IAudioOutputProvider
{
    private readonly WebViewAudioBridge _bridge;

    public string DisplayName => "WebView Speaker (AEC ref)";
    public string ProviderId => "webview-speaker";
    public bool IsAvailable => _bridge.IsReady;
    public bool IsPlaying { get; private set; }

    public event EventHandler? Disconnected;

    public WebViewSpeakerProvider(WebViewAudioBridge bridge)
    {
        _bridge = bridge;
    }

    public Task StartAsync(int sampleRate, CancellationToken ct = default)
    {
        IsPlaying = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsPlaying = false;
        return Task.CompletedTask;
    }

    public async Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        await _bridge.PlayChunkAsync(pcmData, ct);
    }

    public void ClearBuffer()
    {
        _bridge.ClearPlaybackBuffer();
    }

    public ValueTask DisposeAsync() => default;
}
```

### Why output must also go through the WebView

**This is critical.** The browser AEC works because it knows what audio is playing through its audio context. If we play audio through the native speaker (`PhoneSpeakerProvider` / `WindowsSpeakerProvider`) but capture mic through the WebView, the browser AEC has no reference signal and **cannot cancel the echo**.

Both input AND output must flow through the WebView for AEC to work.

### Acceptance criteria

- [ ] AI response audio plays through the WebView speaker
- [ ] Audio is audible on the device speaker
- [ ] `VoiceOutputAgent` works unchanged (calls `PlayChunkAsync` on the manager)

---

## Step 5 — Add hidden WebView to MainPage

**Files**: `src/BodyCam/MainPage.xaml`, `src/BodyCam/MainPage.xaml.cs`

### XAML change

Add a hidden WebView inside the existing `Grid` (Row 1), below the existing content:

```xml
<!-- Hidden WebView for audio bridge (AEC) -->
<WebView x:Name="AudioBridgeWebView"
         IsVisible="False"
         HeightRequest="0" WidthRequest="0"
         Grid.Row="1" />
```

### Code-behind change

In the `Loaded` handler, wire the WebView to the bridge:

```csharp
// In constructor, inject WebViewAudioBridge
private readonly WebViewAudioBridge _audioBridge;

// In Loaded handler:
_audioBridge.SetWebView(AudioBridgeWebView);
await _audioBridge.InitializeAsync();
```

Load the HTML from raw resources:

```csharp
AudioBridgeWebView.Source = new HtmlWebViewSource
{
    Html = await LoadRawResourceAsync("audio-bridge.html")
};
```

Or use a URL to the bundled asset (platform-specific path).

### Platform-specific WebView configuration

**Android** — need to enable JavaScript and media autoplay:
```csharp
#if ANDROID
var nativeWebView = (Android.Webkit.WebView)AudioBridgeWebView.Handler.PlatformView;
nativeWebView.Settings.JavaScriptEnabled = true;
nativeWebView.Settings.MediaPlaybackRequiresUserGesture = false;
// Grant mic permission automatically (already granted at app level)
#endif
```

**Windows** — WebView2 should work with default settings. May need to set `CoreWebView2Settings.IsWebMessageEnabled = true`.

### Acceptance criteria

- [ ] WebView loads without being visible to the user
- [ ] WebView has JS enabled and mic permission
- [ ] Bridge initialization completes
- [ ] No visual impact on existing layout

---

## Step 6 — Register services in DI

**File**: `src/BodyCam/ServiceExtensions.cs`

### Changes to `AddAudioServices()`

```csharp
public static IServiceCollection AddAudioServices(this IServiceCollection services)
{
    // WebView audio bridge (shared by input + output providers)
    services.AddSingleton<WebViewAudioBridge>();

    // Audio input
    services.AddSingleton<IAudioInputProvider, WebViewMicProvider>();      // <-- NEW: WebView AEC mic
    #if WINDOWS
        services.AddSingleton<IAudioInputProvider, Platforms.Windows.PlatformMicProvider>();
        services.AddSingleton<Platforms.Windows.Audio.WindowsBluetoothEnumerator>();
    #elif ANDROID
        services.AddSingleton<IAudioInputProvider, Platforms.Android.PlatformMicProvider>();
        services.AddSingleton<Platforms.Android.Audio.AndroidBluetoothEnumerator>();
    #endif
    services.AddSingleton<AudioInputManager>();
    services.AddSingleton<IAudioInputService>(sp => sp.GetRequiredService<AudioInputManager>());

    // Audio output
    services.AddSingleton<IAudioOutputProvider, WebViewSpeakerProvider>(); // <-- NEW: WebView speaker
    #if WINDOWS
        services.AddSingleton<IAudioOutputProvider, Platforms.Windows.WindowsSpeakerProvider>();
        services.AddSingleton<Platforms.Windows.Audio.WindowsBluetoothOutputEnumerator>();
    #elif ANDROID
        services.AddSingleton<IAudioOutputProvider, Platforms.Android.PhoneSpeakerProvider>();
        services.AddSingleton<Platforms.Android.Audio.AndroidBluetoothOutputEnumerator>();
    #endif
    services.AddSingleton<AudioOutputManager>();
    services.AddSingleton<IAudioOutputService>(sp => sp.GetRequiredService<AudioOutputManager>());
}
```

### Default provider selection

Modify `AudioInputManager.InitializeAsync` to default to `"webview"` instead of `"platform"` when no saved preference exists:
```csharp
var providerId = _settings.ActiveAudioInputProvider ?? "webview";
```

Or keep `"platform"` as default and let the user switch via Settings. For the experiment phase, **default to `"webview"`** on Android.

### Acceptance criteria

- [ ] Both `webview` and `platform` providers appear in `AudioInputManager.Providers`
- [ ] Both `webview-speaker` and platform speaker appear in `AudioOutputManager.Providers`
- [ ] App starts without errors, defaults to WebView provider

---

## Step 7 — Platform-specific JS bridge wiring

This is the most platform-specific step. The MAUI `WebView` abstraction is thin — we need platform handlers.

### Android (priority)

**File**: `src/BodyCam/Platforms/Android/WebViewBridgeHandler.cs`

```csharp
// Access native Android WebView
var nativeWebView = (Android.Webkit.WebView)mauiWebView.Handler.PlatformView;

// Add JS interface for JS→C# communication
nativeWebView.AddJavascriptInterface(
    new AudioBridgeJsInterface(bridge),
    "NativeBridge"
);

// JS calls: NativeBridge.onAudioChunk(base64)
```

```csharp
[Android.Webkit.JavascriptInterface]
[Android.Runtime.Export("onAudioChunk")]
public void OnAudioChunk(string base64Pcm)
{
    var bytes = Convert.FromBase64String(base64Pcm);
    _bridge.RaiseAudioCaptured(bytes);
}
```

Update JS to call `NativeBridge.onAudioChunk(b64)` instead of `window.chrome.webview.postMessage()`.

### Windows

**File**: `src/BodyCam/Platforms/Windows/WebViewBridgeHandler.cs`

```csharp
var nativeWebView2 = (Microsoft.UI.Xaml.Controls.WebView2)mauiWebView.Handler.PlatformView;
await nativeWebView2.EnsureCoreWebView2Async();
nativeWebView2.CoreWebView2.WebMessageReceived += (_, args) =>
{
    var json = args.WebMessageAsJson;
    // Parse and dispatch
};
```

JS uses: `window.chrome.webview.postMessage(json)`

### iOS (future)

**File**: `src/BodyCam/Platforms/iOS/WebViewBridgeHandler.cs`

```csharp
var nativeWkWebView = (WebKit.WKWebView)mauiWebView.Handler.PlatformView;
var handler = new AudioMessageHandler(bridge);
nativeWkWebView.Configuration.UserContentController
    .AddScriptMessageHandler(handler, "audioBridge");
```

JS uses: `webkit.messageHandlers.audioBridge.postMessage(json)`

### JS bridge abstraction

Update `audio-bridge.html` to detect the platform and use the right bridge:

```js
function sendToNative(msg) {
  if (window.NativeBridge?.onAudioChunk) {
    // Android
    NativeBridge.onAudioChunk(msg.data);
  } else if (window.chrome?.webview) {
    // Windows WebView2
    window.chrome.webview.postMessage(JSON.stringify(msg));
  } else if (window.webkit?.messageHandlers?.audioBridge) {
    // iOS
    window.webkit.messageHandlers.audioBridge.postMessage(msg);
  }
}
```

### Acceptance criteria

- [ ] Android: JS→C# audio chunks flow via `JavascriptInterface`
- [ ] Android: C#→JS playback chunks flow via `EvaluateJavascript`
- [ ] Windows: Same flows via WebView2 `WebMessageReceived`
- [ ] Round-trip latency < 20ms per chunk

---

## Step 8 — Handle permissions and lifecycle

### Microphone permission

The app already requests mic permission for the platform provider. The WebView also needs it:

**Android**: Override `WebChromeClient.OnPermissionRequest` to auto-grant when the app already has `RECORD_AUDIO`:

```csharp
public override void OnPermissionRequest(PermissionRequest request)
{
    if (request.GetResources().Contains(PermissionRequest.ResourceAudioCapture))
        request.Grant(request.GetResources());
}
```

**Windows**: WebView2 should inherit the app's mic permission. May need `CoreWebView2.PermissionRequested` handler.

### App lifecycle

**Android backgrounding**: `AudioContext` may be suspended when the app goes to background. Handle:
- `OnPause`: Call `stop()` in JS to release mic
- `OnResume`: Call `start()` in JS to re-acquire mic

Wire via `MainPage.OnAppearing` / `OnDisappearing` or the MAUI lifecycle events.

**Screen off**: Similar to backgrounding. Test behavior on Android — some devices kill WebView processes aggressively.

### Acceptance criteria

- [ ] WebView mic works without separate permission prompt (uses app-level permission)
- [ ] App resume re-acquires mic without errors
- [ ] No crashes on background/foreground transitions

---

## Step 9 — Integration test on Android

### Test plan

1. **Build and deploy**: `dotnet build src/BodyCam/BodyCam.csproj -f net10.0-android -t:Install`
2. **Verify WebView loads**: Check debug log for "audio bridge ready" message
3. **Verify AEC**: 
   - Set state to Active → speak → AI responds through speaker
   - Listen for echo in the AI's transcript (if AI hears its own response, echo is not cancelled)
   - Compare vs `"platform"` provider
4. **Verify latency**: Check if conversation feels responsive (< 200ms total pipeline latency)
5. **Verify audio quality**: Ensure no clicks, pops, or artifacts from the JS bridge

### Fallback

If `"webview"` provider fails, `AudioInputManager` should fall back to `"platform"`. Add error handling in `WebViewMicProvider.StartAsync`:

```csharp
public async Task StartAsync(CancellationToken ct = default)
{
    try
    {
        await _bridge.StartCaptureAsync(ct);
        IsCapturing = true;
    }
    catch (Exception ex)
    {
        // Log error, mark as unavailable
        System.Diagnostics.Debug.WriteLine($"WebView mic failed: {ex.Message}");
        IsAvailable = false;
        throw; // AudioInputManager will fall back to next provider
    }
}
```

### Acceptance criteria

- [ ] End-to-end voice conversation works on Android with WebView provider
- [ ] Echo is noticeably reduced compared to platform AEC
- [ ] Fallback to platform provider works when WebView fails
- [ ] No regression in existing functionality

---

## Step 10 — Settings UI for provider selection

### Optional — add after core works

Add provider picker to Settings page so user can switch between `"webview"` and `"platform"` mic/speaker.

The `AudioInputManager.Providers` and `AudioOutputManager.Providers` lists already exist. The `SettingsViewModel` can expose them and call `SetActiveAsync(providerId)` on change.

This is not blocking for the experiment — can be done after validating AEC quality.

---

## File summary

| # | File | Action | Step |
|---|------|--------|------|
| 1 | `Resources/Raw/audio-bridge.html` | **Create** | 1 |
| 2 | `Services/Audio/WebViewAudioBridge.cs` | **Create** | 2 |
| 3 | `Services/Audio/WebViewMicProvider.cs` | **Create** | 3 |
| 4 | `Services/Audio/WebViewSpeakerProvider.cs` | **Create** | 4 |
| 5 | `MainPage.xaml` | **Edit** — add hidden WebView | 5 |
| 6 | `MainPage.xaml.cs` | **Edit** — wire bridge in Loaded | 5 |
| 7 | `ServiceExtensions.cs` | **Edit** — register new providers | 6 |
| 8 | `Platforms/Android/WebViewBridgeHandler.cs` | **Create** | 7 |
| 9 | `Platforms/Windows/WebViewBridgeHandler.cs` | **Create** (after Android) | 7 |
| 10 | `Platforms/Android/PlatformMicProvider.cs` | No change (kept as fallback) | — |

All files under `src/BodyCam/`.

---

## Execution order

```
Step 1  ──→  Step 2  ──→  Step 3 + Step 4 (parallel)
                              │
                              ▼
                          Step 5  ──→  Step 6  ──→  Step 7 (Android)
                                                        │
                                                        ▼
                                                    Step 8  ──→  Step 9
                                                                    │
                                                                    ▼
                                                                Step 10 (optional)
                                                                Step 7 (Windows)
```

Steps 1-6 can be built and compiled without a device. Step 7+ requires device testing.
