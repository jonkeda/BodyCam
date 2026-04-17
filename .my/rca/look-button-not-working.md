# RCA: Look Button Does Nothing When Clicked

**Date:** 2026-04-17  
**Status:** Root cause identified  
**Severity:** Critical — primary user-facing feature broken

## Symptom

Pressing the "👁 Look" button on the Home page does nothing. Expected behavior:
1. Snap an image from the camera
2. Send the image to the vision AI for description
3. Voice speaks the description aloud

## Root Cause

**The `LookCommand` is a stub.** It guards with `CanAct` but performs no work.

```csharp
// MainViewModel.cs, line 82
LookCommand = new AsyncRelayCommand(async () =>
{
    if (!CanAct) return;
    // ← nothing here
});
```

The full vision pipeline *does* exist and works — but only through the Realtime API
voice path (wake word "bodycam-look" or conversational tool call). The button was
never wired to invoke it.

## The Working Pipeline (voice path)

```
Voice "Look" → Realtime API → describe_scene function call
  → DescribeSceneTool.ExecuteAsync()
    → context.CaptureFrame()          → CameraView.CaptureImage() → JPEG bytes
    → VisionAgent.DescribeFrameAsync() → OpenAI gpt-4-vision → description text
    → ToolResult returned to Realtime API
  → Realtime API generates spoken response
    → OnAudioDelta → VoiceOutputAgent → IAudioOutputService.PlayChunkAsync()
```

This pipeline is fully functional on Windows (WindowsAudioOutputService) and
Android (AndroidAudioOutputService).

## What Needs to Happen (Fix)

The `LookCommand` needs to trigger the same flow the voice path uses. There are
two viable approaches:

### Option A: Invoke DescribeSceneTool directly (button-only, no voice response)

Wire the button to capture a frame, call VisionAgent, and display the result
in the transcript. No voice output — text only.

```
LookCommand → CaptureFrameFromCameraViewAsync()
            → VisionAgent.DescribeFrameAsync(frame)
            → Add TranscriptEntry to Entries
```

**Pros:** Simple, no Realtime API session needed  
**Cons:** No spoken response; inconsistent with voice path behavior

### Option B: Send a user message through the orchestrator (full pipeline)

Inject a synthetic user message like "Look at what's in front of me" into the
active Realtime API session, triggering the normal tool-call flow.

```
LookCommand → AgentOrchestrator.SendUserMessage("Describe what you see")
            → Realtime API → describe_scene tool call → vision → spoken response
```

**Pros:** Full voice response, consistent behavior  
**Cons:** Requires an active Realtime session (ActiveSession or WakeWord layer)

### Recommendation

**Option B** is preferred — it matches user expectations from the screenshot
(voice assistant app) and reuses the existing tested pipeline.

## Secondary Issues

| Issue | File | Impact |
|-------|------|--------|
| `ReadCommand` is also a stub | `MainViewModel.cs:86` | Read button also broken |
| `FindCommand` is also a stub | `MainViewModel.cs:90` | Find button also broken |
| `AudioOutputService` is a no-op stub | `Services/AudioOutputService.cs` | No speech on iOS/macCatalyst |
| 5-second vision cooldown | `DescribeSceneTool.cs:40` | Rapid taps return stale result |

## Evidence

### LookCommand definition (MainViewModel.cs:82-85)
```csharp
LookCommand = new AsyncRelayCommand(async () =>
{
    if (!CanAct) return;
});
```

### DescribeSceneTool.ExecuteAsync (Tools/DescribeSceneTool.cs:37-56)
```csharp
protected override async Task<ToolResult> ExecuteAsync(
    DescribeSceneArgs args, ToolContext context, CancellationToken ct)
{
    if (_vision.LastDescription is not null
        && DateTimeOffset.UtcNow - _vision.LastCaptureTime < TimeSpan.FromSeconds(5))
        return ToolResult.Success(new { description = _vision.LastDescription });

    var frame = await context.CaptureFrame(ct);
    if (frame is null)
    {
        var stale = _vision.LastDescription ?? "Camera not available or no frame captured.";
        return ToolResult.Success(new { description = stale });
    }

    var description = await _vision.DescribeFrameAsync(frame, args.Query);
    context.Session.LastVisionDescription = description;
    return ToolResult.Success(new { description });
}
```

### AudioOutputService stub (Services/AudioOutputService.cs)
```csharp
public Task PlayChunkAsync(byte[] pcmData, CancellationToken ct = default)
{
    // TODO: Play PCM data through speaker
    return Task.CompletedTask;
}
```

### Platform audio registrations (MauiProgram.cs:85-89)
```csharp
// Windows — real implementation
builder.Services.AddSingleton<IAudioOutputService, WindowsAudioOutputService>();
// Android — real implementation
builder.Services.AddSingleton<IAudioOutputService, AndroidAudioOutputService>();
// Fallback — stub (no-op)
builder.Services.AddSingleton<IAudioOutputService, AudioOutputService>();
```

### CanAct gating (MainViewModel.cs:238)
```csharp
public bool CanAct => CurrentLayer != ListeningLayer.Sleep;
```
Button is disabled in Sleep mode. Even when enabled (WakeWord/Active), the
command body is empty.
