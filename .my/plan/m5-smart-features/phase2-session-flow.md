# M5 Phase 2 — Quick Action & Session Flow

Implement the quick-action lifecycle and session timeout. Wire layer transitions
with UI feedback and audio tones. This makes wake words feel responsive — a
keyword fires, the action executes, and the system returns to low-power listening.

**Depends on:** M5 Phase 1 (Porcupine running, wake words detected).

---

## Wave 1: Quick Action Lifecycle

Quick actions are the key UX innovation — say a wake word, get a result, no
session management required. The flow is:

```
Wake word detected ("bodycam-look")
  → MicrophoneCoordinator.TransitionToActiveSessionAsync()
  → Connect Realtime API WebSocket
  → Execute tool (describe_scene) with current camera frame
  → Receive response audio
  → Play through speakers
  → Disconnect WebSocket
  → MicrophoneCoordinator.TransitionToWakeWordAsync()
  → Resume Porcupine listening
```

### 1.1 — QuickActionExecutor

```csharp
// Orchestration/QuickActionExecutor.cs
public class QuickActionExecutor
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly IMicrophoneCoordinator _micCoordinator;
    private readonly IRealtimeClient _realtimeClient;

    /// <summary>
    /// Execute a tool as a quick action: connect → run → speak → disconnect.
    /// Returns to wake word layer automatically.
    /// </summary>
    public async Task ExecuteAsync(
        string toolName,
        string? arguments,
        CancellationToken ct = default)
    {
        try
        {
            await _micCoordinator.TransitionToActiveSessionAsync(ct);
            await _realtimeClient.ConnectAsync(ct);

            var result = await _orchestrator.ExecuteToolDirectAsync(toolName, arguments, ct);

            // Wait for TTS to finish playing
            await _orchestrator.WaitForSpeechCompleteAsync(ct);
        }
        finally
        {
            await _realtimeClient.DisconnectAsync();
            await _micCoordinator.TransitionToWakeWordAsync(ct);
        }
    }
}
```

### 1.2 — Wire into Orchestrator

Update `AgentOrchestrator.OnWakeWordDetected()` to use `QuickActionExecutor`
for `WakeWordMode.QuickAction` tools:

```csharp
private async void OnWakeWordDetected(object? sender, WakeWordDetectedEventArgs e)
{
    switch (e.Action)
    {
        case WakeWordAction.StartSession:
            await StartSessionAsync();
            break;

        case WakeWordAction.GoToSleep:
            await GoToSleepAsync();
            break;

        case WakeWordAction.InvokeTool:
            var tool = _toolDispatcher.GetTool(e.ToolName!);
            if (tool?.WakeWord?.Mode == WakeWordMode.QuickAction)
                await _quickAction.ExecuteAsync(e.ToolName!, null);
            else
                await StartSessionWithToolAsync(e.ToolName!);
            break;
    }
}
```

### 1.3 — Unit Tests

- Quick action connects, executes, speaks, disconnects
- Quick action returns to wake word layer on success
- Quick action returns to wake word layer on failure (finally block)
- FullSession tools start a session instead of quick action

---

## Wave 2: Session Timeout

Active sessions should auto-disconnect after a period of silence. The user
shouldn't need to say "Go to sleep" — inactivity returns to wake word layer.

### 2.1 — SessionTimeoutService

```csharp
// Services/SessionTimeoutService.cs
public class SessionTimeoutService : IDisposable
{
    private readonly TimeSpan _timeout;
    private CancellationTokenSource? _cts;

    public event EventHandler? TimedOut;

    public SessionTimeoutService(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>Reset the timer. Call on any user activity.</summary>
    public void Reset()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = WaitAndFireAsync(_cts.Token);
    }

    /// <summary>Stop the timer entirely.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task WaitAndFireAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_timeout, ct);
            TimedOut?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose() => Stop();
}
```

### 2.2 — Wire into Orchestrator

- Reset timer on `InputTranscriptCompleted` (user spoke)
- Reset timer on `OutputTranscriptDelta` (AI is speaking)
- On `TimedOut` → transition back to wake word layer

### 2.3 — Configurable Timeout

Add to settings:

```csharp
public int SessionTimeoutSeconds { get; set; } = 60; // 30–120 range
```

### 2.4 — Unit Tests

- Timeout fires after configured duration of silence
- Timer resets on activity
- Timer stops when session ends manually
- Timeout triggers layer transition

---

## Wave 3: Layer Transition UI

Visual feedback so the user knows which layer is active. The existing tri-state
pill in MainPage already has Sleep/Listening/Active states — wire them to layer
transitions.

### 3.1 — Layer State in MainViewModel

```csharp
public enum ListeningLayer { Sleep, WakeWord, ActiveSession }

private ListeningLayer _currentLayer = ListeningLayer.Sleep;
public ListeningLayer CurrentLayer
{
    get => _currentLayer;
    set => SetProperty(ref _currentLayer, value);
}
```

### 3.2 — UI Updates

| Layer | Pill Color | Pill Text | Status Bar |
|-------|-----------|-----------|------------|
| Sleep | Gray | "Sleep" | "Press button to start" |
| WakeWord | Amber | "Listening" | "Say 'Hey BodyCam' or a command" |
| ActiveSession | Green | "Active" | "Conversation active" |

### 3.3 — Wire Layer Changes

- `PorcupineWakeWordService.StartAsync()` → `CurrentLayer = WakeWord`
- `OnWakeWordDetected(StartSession)` → `CurrentLayer = ActiveSession`
- `OnWakeWordDetected(GoToSleep)` → `CurrentLayer = Sleep`
- Quick action returns → `CurrentLayer = WakeWord`
- Session timeout → `CurrentLayer = WakeWord`

---

## Wave 4: Audio Feedback Tones

Short audio tones provide confirmation without needing to look at the phone.

### 4.1 — Tone Files

| Event | Sound | Duration | File |
|-------|-------|----------|------|
| Wake word detected | Rising chime | ~300ms | `tone-activate.wav` |
| Session started | Double beep | ~200ms | `tone-session.wav` |
| Quick action complete | Descending chime | ~300ms | `tone-done.wav` |
| Go to sleep | Low tone | ~200ms | `tone-sleep.wav` |
| Session timeout | Fade-out tone | ~500ms | `tone-timeout.wav` |

### 4.2 — TonePlayer

```csharp
// Services/TonePlayer.cs
public class TonePlayer
{
    private readonly IAudioOutputService _audioOutput;

    public Task PlayAsync(string toneName, CancellationToken ct = default)
    {
        var path = $"Resources/Tones/{toneName}.wav";
        var pcm = LoadWavAsPcm(path);
        return _audioOutput.PlayChunkAsync(pcm, ct);
    }
}
```

### 4.3 — Wire into Layer Transitions

- Wake word detected → play `tone-activate`
- Quick action complete → play `tone-done`
- Go to sleep → play `tone-sleep`
- Timeout → play `tone-timeout`

---

## Wave 5: MicrophoneCoordinator Integration Tests

Verify the full mic handoff sequence works end-to-end with the new quick action
and timeout flows.

### 5.1 — Test Scenarios

- **Quick action handoff:** Porcupine listening → wake word → mic transfers to
  Realtime API → tool executes → mic returns to Porcupine
- **Session timeout handoff:** Active session → silence → timeout → mic returns
  to Porcupine
- **Rapid wake words:** Two quick actions in rapid succession — verify no mic
  contention or deadlock
- **Cancel during quick action:** Wake word during quick action execution —
  verify queued or ignored (not crash)

### 5.2 — Integration Test Setup

Use `TestMicProvider` (from M15 if available, or a simple stub) to feed audio
and verify handoff timing.

---

## Exit Criteria

- [ ] Quick actions (look, read) connect → execute → speak → disconnect automatically
- [ ] FullSession tools start a persistent session
- [ ] Session timeout returns to wake word layer after configurable silence (30s–2min)
- [ ] Timer resets on any user or AI activity
- [ ] UI layer indicator updates on all transitions
- [ ] Audio feedback tones play on layer transitions
- [ ] MicrophoneCoordinator handoff works for quick action round-trip
- [ ] No mic contention on rapid wake words
