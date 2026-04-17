# M3 — Vision Pipeline (Laptop/Phone Camera) ✦ Core

**Status:** NOT STARTED
**Goal:** Capture camera frames, send to gpt-5.4 Vision, integrate with conversation.

---

## Scope

| # | Task | Details |
|---|------|---------|
| 3.1 | `ICameraService` + Windows impl | Webcam frame capture as byte[] / base64 |
| 3.2 | `VisionAgent` (MAF) | Send frames to gpt-5.4 Vision, get descriptions |
| 3.3 | Vision trigger modes | On-demand (button), periodic, voice-triggered |
| 3.4 | Context injection | Vision descriptions feed into ConversationAgent |
| 3.5 | Android camera impl | CameraX / platform camera for Android |
| 3.6 | Camera preview UI | Small preview pane on MainPage |

## Exit Criteria

- [ ] Point webcam at object → ask "what is this?" → get accurate spoken description
- [ ] Vision context injected into conversation naturally
- [ ] Works with both Windows webcam and Android camera
- [ ] Camera preview visible in UI

---

## Technical Design

### Camera Capture (Windows)

**Approach:** Use `MediaCapture` (WinRT) or OpenCvSharp for webcam.

**Option A — MediaCapture (WinRT, native):**
- Available on Windows 10+
- Async frame capture
- MAUI WinUI3 can access WinRT APIs directly

**Option B — OpenCvSharp:**
- Cross-platform (works on Windows + can work on Android via bindings)
- Simpler API for frame capture
- `VideoCapture.Read()` → `Mat` → JPEG bytes

**Decision:** Start with OpenCvSharp for uniformity. Fall back to platform-native if needed.

```
NuGet: OpenCvSharp4
NuGet: OpenCvSharp4.runtime.win (Windows)
```

### Camera Capture (Android)

**Approach:** `Android.Hardware.Camera2` API or CameraX.

- CameraX is simpler but requires AndroidX dependency
- Capture to `ImageReader` → extract JPEG bytes
- Needs `CAMERA` permission

**File:** `Platforms/Android/AndroidCameraService.cs`

### Frame Processing Pipeline

```
Camera → JPEG frame (byte[])
  │
  ├── UI: display preview (small thumbnail)
  │
  └── VisionAgent.DescribeFrameAsync(byte[])
        │
        ├── Convert to base64
        ├── Build gpt-5.4 Vision request:
        │   messages: [
        │     { role: "user", content: [
        │       { type: "text", text: "Describe what you see" },
        │       { type: "image_url", url: "data:image/jpeg;base64,..." }
        │     ]}
        │   ]
        ├── Call Chat Completions API with vision model
        └── Return description string
```

### Vision Trigger Modes

| Mode | Trigger | Use Case |
|------|---------|----------|
| On-demand | User taps camera button in UI | "Take a photo and describe it" |
| Voice-triggered | User says "what do you see?" | Hands-free scene description |
| Periodic | Timer (e.g., every 10s) | Background scene awareness |
| Smart | When conversation needs visual context | AI decides to look |

**Implementation:**
- Keywords detected in transcript: "see", "look", "what is", "read", "describe"
- Orchestrator checks keywords → triggers `VisionAgent.CaptureAndDescribeAsync()`
- Result injected into `SessionContext.LastVisionDescription`

### Context Injection

When vision is triggered, the description is added to the conversation:

```csharp
// In AgentOrchestrator
if (ShouldTriggerVision(transcript))
{
    var description = await _vision.CaptureAndDescribeAsync(ct);
    if (description != null)
    {
        session.LastVisionDescription = description;
        // Inject as system context for next conversation turn
        session.Messages.Add(new ChatMessage
        {
            Role = "system",
            Content = $"[Vision] The user's camera currently shows: {description}"
        });
    }
}
```

### UI Updates

Add to MainPage.xaml:
- Camera preview `Image` (small, corner overlay)
- Camera toggle button
- Last vision description label

```xml
<!-- Camera preview overlay -->
<Image Source="{Binding CameraPreview}"
       WidthRequest="160" HeightRequest="120"
       HorizontalOptions="End" VerticalOptions="Start" />
```

---

## Cost Considerations

gpt-5.4 Vision is expensive per image. Mitigations:
- Resize frames to 512×512 before sending (reduces token count)
- Use `detail: "low"` for basic descriptions
- Cache recent descriptions (don't re-describe identical scenes)
- Rate-limit periodic mode to max 1 frame per 10s

---

## NuGet Packages Needed

| Package | Purpose | Platform |
|---------|---------|----------|
| OpenCvSharp4 | Frame capture | Windows |
| OpenCvSharp4.runtime.win | Native bindings | Windows |

---

## Risks

| Risk | Mitigation |
|------|-----------|
| OpenCvSharp bloat in MAUI | Consider lighter alternatives |
| Vision API cost spiral | Rate limiting, frame size reduction, detail=low |
| Camera permissions on Android | Runtime permission flow |
| Slow vision response | Async, don't block conversation; inject when ready |
