# M24 — Browser WebRTC AEC Approach

## The idea

Route microphone audio through a hidden WebView that uses the browser's built-in WebRTC audio processing (`getUserMedia` with `echoCancellation: true`, `noiseSuppression: true`, `autoGainControl: true`). The browser's WebRTC stack uses Google's AEC3 engine — the same engine used in Chrome, Google Meet, and every WebRTC call. It handles echo cancellation, noise suppression, and auto gain control with zero configuration.

This approach has been used successfully by others who got good echo cancellation results without needing to build native WebRTC libraries.

## Has it improved?

**Yes, significantly.** Key improvements over the past year:

1. **Chrome 141 (2026)**: Added `echoCancellation: "all"` and `"remote-only"` constraint values — can now cancel ALL system audio (screen readers, notifications) or just remote peer audio. Supported in Chrome Android and WebView Android.

2. **AEC3 maturity**: Google's AEC3 (3rd generation) has been the default in Chrome since ~2018 and has had continuous improvements. It's now considered production-grade across all platforms.

3. **WebView support**: `WebView Android` supports echo cancellation constraints (since version 59), including the new `"all"` mode in v141.

4. **iOS Safari**: `echoCancellation` supported since Safari 11 / iOS 11. However, the new `"all"` and `"remote-only"` modes are NOT supported on Safari/iOS — only `true`/`false`.

## How it would work with the Realtime API

```
┌─────────────────────────────────────────────────────┐
│ Hidden WebView                                       │
│                                                      │
│  getUserMedia({ echoCancellation: true,              │
│                 noiseSuppression: true,              │
│                 autoGainControl: true })             │
│       │                                              │
│       ▼                                              │
│  MediaStreamTrack → MediaRecorder / ScriptProcessor │
│       │                                              │
│       ▼                                              │
│  Clean PCM chunks (via JS bridge)                   │
│                                                      │
│  Speaker playback ← Audio element / Web Audio API   │
│       ▲                                              │
│       │                                              │
│  Receive PCM from C# (via JS bridge)                │
└──────────┬──────────────────────────┬───────────────┘
           │                          │
           ▼                          ▲
┌──────────────────────────────────────────────────────┐
│ C# / MAUI                                            │
│                                                      │
│  WebView JS bridge receives clean audio chunks       │
│       │                                              │
│       ▼                                              │
│  Send to OpenAI Realtime API (WebSocket)             │
│       │                                              │
│       ▼                                              │
│  Receive response PCM from API                       │
│       │                                              │
│       ▼                                              │
│  Send to WebView for playback (JS bridge)            │
│  (Browser AEC uses this as reference signal)         │
└──────────────────────────────────────────────────────┘
```

### Key insight

The browser's AEC automatically uses whatever audio is playing through the page's audio context as the reference signal. By playing the AI response audio through the WebView's `<audio>` element or Web Audio API, the browser's AEC already knows what to subtract from the mic input. **No manual reference signal plumbing needed.**

### Data flow

1. WebView calls `getUserMedia()` with AEC enabled — browser applies echo cancellation
2. `ScriptProcessorNode` / `AudioWorklet` captures clean PCM chunks
3. JavaScript bridge (`window.chrome.webview.postMessage` on Windows, `webkit.messageHandlers` on iOS) sends chunks to C#
4. C# sends clean audio to OpenAI Realtime API via WebSocket
5. API response PCM comes back to C#
6. C# sends response audio to WebView via `evaluateJavascript`
7. WebView plays audio through Web Audio API — browser AEC uses this as reference
8. Loop back to step 2

## Platform support

| Platform | WebView Engine | AEC Support | Notes |
|----------|---------------|-------------|-------|
| Android  | Chromium (WebView) | Yes — full, including `"all"` mode | Best support; same engine as Chrome |
| Windows  | WebView2 (Chromium) | Yes — full | Requires WebView2 runtime (built into Windows 11, installable on 10) |
| iOS      | WKWebView (Safari) | Yes — `true`/`false` only | No `"all"` mode; Apple's AEC is good quality |
| macOS    | WKWebView (Safari) | Yes — `true`/`false` only | Same as iOS |

## Advantages

- **Zero native libraries** — no cross-compilation, no P/Invoke, no native build scripts
- **Battle-tested AEC** — Google's AEC3 / Apple's AEC, used by billions of users daily
- **Cross-platform** — works on Android, Windows, iOS, macOS with the same JS code
- **Automatic reference signal** — browser handles it internally
- **Noise suppression + AGC included** — free with `getUserMedia` constraints
- **Always up to date** — browser updates improve AEC without app changes

## Disadvantages

- **Latency** — JS bridge adds 5-15ms round-trip per chunk vs direct native capture
- **WebView overhead** — hidden WebView consumes memory (~30-50MB)
- **Sample rate** — browser may capture at 48kHz (need to verify/resample to 24kHz)
- **Permissions** — WebView needs mic permission; may need to handle this separately from MAUI's permission flow
- **Debugging** — audio pipeline split between C# and JS; harder to debug
- **iOS constraints** — WKWebView has restrictions on background audio; may need `AVAudioSession` configuration
- **Dependency** — ties audio pipeline to WebView availability

## Effort estimate

| Component | Effort |
|-----------|--------|
| WebView HTML/JS for getUserMedia + audio capture | Low |
| JS↔C# bridge for audio chunks (both directions) | Medium |
| Replace `PlatformMicProvider` with WebView-based capture | Medium |
| Route API response audio to WebView for playback | Medium |
| Handle permissions, lifecycle, background audio | Medium |
| Testing across Android/Windows/iOS | Medium |
| **Total** | **Medium** |

## Comparison with other approaches

| Approach | AEC Quality | Cross-platform | Effort | Dependency Risk |
|----------|------------|----------------|--------|-----------------|
| **Browser WebRTC (this)** | High | All platforms | Medium | Low (browsers) |
| Platform-native AEC | Varies by device | Per-platform | Medium each | Low |
| WebRTC APM native lib | High | Needs native builds | High | Medium (maintainer hiatus) |
| Server-side (OpenAI) | Moderate | All | Low | Relies on API |

## Recommendation

This approach is worth trying as a **Phase 1 experiment** on Android (where we're currently seeing echo). The implementation is:

1. Create a minimal HTML page with `getUserMedia` + `AudioWorklet`
2. Load it in a hidden `WebView` on `MainPage`
3. Bridge audio chunks via JavaScript interop
4. Play API response audio through the WebView
5. Compare echo quality vs current `PlatformMicProvider` + `AcousticEchoCanceler`

If it works well on Android, extend to Windows and iOS.

## Files to create/change

- `src/BodyCam/Resources/Raw/audio-bridge.html` — WebView HTML/JS for audio capture + playback
- `src/BodyCam/Services/Audio/WebViewAudioProvider.cs` — New `IAudioInputProvider` + `IAudioOutputProvider` using WebView bridge
- `src/BodyCam/MainPage.xaml` — Add hidden WebView element
- `src/BodyCam/MainPage.xaml.cs` — Wire WebView JS bridge
- `src/BodyCam/ServiceExtensions.cs` — Register WebView audio provider
