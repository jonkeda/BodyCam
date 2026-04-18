# Improvement Proposals

Concrete proposals ranked by priority. Each references the relevant review document for context.

---

## P0 — Critical (do before next release)

### 1. Add SemaphoreSlim to Audio Managers
**Ref:** [thread-safety.md](thread-safety.md) §1
**Scope:** `AudioInputManager`, `AudioOutputManager`
**Effort:** Small (~30 min)
**What:** Wrap `SetActiveAsync`, `RegisterProvider`, `UnregisterProviderAsync` in `SemaphoreSlim`. Bluetooth hot-plug events arrive on background threads and can race with user-initiated provider switches.

### 2. Add try/catch to All Async Void Event Handlers
**Ref:** [error-handling.md](error-handling.md) §1, §2
**Scope:** `VoiceInputAgent.OnAudioChunk`, `AgentOrchestrator` event handlers
**Effort:** Small (~20 min)
**What:** Every `async void` handler needs a top-level try/catch. Log errors and send error results to the model when applicable. Prevents silent crashes.

### 3. Add Tool Execution Timeout
**Ref:** [resilience.md](resilience.md) §2
**Scope:** `ToolDispatcher.ExecuteAsync`
**Effort:** Trivial (~10 min)
**What:** `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter(15s)`. Return `{"error":"timeout"}` to the model. Prevents hung responses.

---

## P1 — High (should do soon)

### 4. WebSocket Reconnection
**Ref:** [resilience.md](resilience.md) §1
**Scope:** `RealtimeClient` (add `ConnectionLost` event), `AgentOrchestrator` (reconnect logic)
**Effort:** Medium (~2 hours)
**What:** When `ReceiveLoop` exits unexpectedly, fire `ConnectionLost`. Orchestrator catches it and retries with exponential backoff (1s, 2s, 4s, 8s, 16s). After 5 failures, transition to Sleep and notify user.

### 5. MemoryStore Thread Safety + Cache
**Ref:** [thread-safety.md](thread-safety.md) §2, [performance.md](performance.md) §1
**Scope:** `MemoryStore`
**Effort:** Small (~30 min)
**What:** Add `SemaphoreSlim` for file I/O. Keep entries in memory after first load. Write-through on save. Eliminates both the race condition and the repeated deserialization.

### 6. ToolDispatcher Error Envelope
**Ref:** [error-handling.md](error-handling.md) §5
**Scope:** `ToolDispatcher.ExecuteAsync`
**Effort:** Trivial (~10 min)
**What:** Catch `JsonException` and general exceptions in the dispatcher. Return structured error JSON instead of letting exceptions propagate. The model can then respond gracefully ("I couldn't do that").

### 7. Fix SetLayerAsync State Revert
**Ref:** [error-handling.md](error-handling.md) §4
**Scope:** `MainViewModel.SetLayerAsync`
**Effort:** Trivial (~5 min)
**What:** Set `CurrentLayer` only after successful `StartAsync`. Currently, a failed start leaves `CurrentLayer` at `ActiveSession` even though `IsRunning` is false.

---

## P2 — Medium (quality of life)

### 8. Snapshot Settings on Session Start
**Ref:** [configuration.md](configuration.md) §1
**Scope:** `AgentOrchestrator.StartAsync`, new `SessionConfig` record
**Effort:** Small (~30 min)
**What:** Create a frozen `SessionConfig` from `AppSettings` at session start. Pass it to `RealtimeClient.ConnectAsync` instead of reading mutable `AppSettings`. Makes it explicit that mid-session changes are ignored.

### 9. Extract DI Registrations
**Ref:** [configuration.md](configuration.md) §2
**Scope:** `MauiProgram.cs`
**Effort:** Small (~20 min)
**What:** Split into `AddBodyCamAudio()`, `AddBodyCamCamera()`, `AddBodyCamTools()`, `AddBodyCamAgents()` extension methods. Pure maintainability improvement.

### 10. Camera TCS Cleanup
**Ref:** [resilience.md](resilience.md) §3
**Scope:** `PhoneCameraProvider.CaptureViaEventAsync`
**Effort:** Trivial (~5 min)
**What:** Always unsubscribe the `MediaCaptured` handler in a `finally` block, regardless of timeout or success.

### 11. Transition Guard in MainViewModel
**Ref:** [thread-safety.md](thread-safety.md) §4
**Scope:** `MainViewModel.SetLayerAsync`
**Effort:** Trivial (~5 min)
**What:** Add `_isTransitioning` guard to prevent concurrent state transitions from rapid button presses.

---

## P3 — Low (nice to have)

### 12. Lazy Wake Word Initialization
**Ref:** [performance.md](performance.md) §3
**Scope:** `MauiProgram.cs`, `PorcupineWakeWordService`
**Effort:** Trivial
**What:** Wrap in `Lazy<T>` so Porcupine native library only loads when wake word mode is first entered.

### 13. Porcupine Dispose Guard
**Ref:** [resilience.md](resilience.md) §4
**Scope:** `PorcupineWakeWordService.StartAsync`
**Effort:** Trivial
**What:** Ensure the Porcupine instance is disposed if startup fails after creation.

### 14. Configurable Mic Release Delay
**Ref:** [resilience.md](resilience.md) §5
**Scope:** `MicrophoneCoordinator`
**Effort:** Trivial
**What:** Move the 50ms hardcoded delay to `AppSettings`.

### 15. Model/Voice Constants
**Ref:** [configuration.md](configuration.md) §4
**Scope:** `ModelOptions`
**Effort:** Trivial
**What:** Replace string arrays with const fields for compile-time safety.

---

## Summary Table

| # | Proposal | Priority | Effort | Risk Addressed |
|---|----------|----------|--------|----------------|
| 1 | Audio manager locking | P0 | Small | BT hot-plug race |
| 2 | Async void try/catch | P0 | Small | Silent crashes |
| 3 | Tool execution timeout | P0 | Trivial | Hung responses |
| 4 | WebSocket reconnect | P1 | Medium | Silent session death |
| 5 | MemoryStore safety + cache | P1 | Small | Data loss + I/O |
| 6 | ToolDispatcher error envelope | P1 | Trivial | Unhandled exceptions |
| 7 | SetLayerAsync state revert | P1 | Trivial | UI inconsistency |
| 8 | Snapshot settings | P2 | Small | Mutation risk |
| 9 | Extract DI registrations | P2 | Small | Maintainability |
| 10 | Camera TCS cleanup | P2 | Trivial | Event handler leak |
| 11 | Transition guard | P2 | Trivial | Double-start |
| 12 | Lazy wake word | P3 | Trivial | Startup time |
| 13 | Porcupine dispose guard | P3 | Trivial | Native memory leak |
| 14 | Configurable mic delay | P3 | Trivial | Platform robustness |
| 15 | Model/voice constants | P3 | Trivial | Type safety |

**Estimated total effort for P0+P1:** ~4 hours
**Estimated total effort for all:** ~6 hours
