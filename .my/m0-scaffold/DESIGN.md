# M0 — Project Scaffold ✦ Foundation

**Status:** COMPLETE
**Goal:** Runnable MAUI app with DI, settings, and basic UI shell.

---

## Scope

| # | Task | Status |
|---|------|--------|
| 0.1 | Create MAUI solution | ✅ Done |
| 0.2 | Settings & configuration | ✅ Done |
| 0.3 | DI registration | ✅ Done |
| 0.4 | Main page shell | ✅ Done |
| 0.5 | Build & run on Windows | ✅ Done |

## Exit Criteria

- [x] App launches on Windows
- [x] Shows Start/Stop, transcript, debug console UI
- [x] DI resolves all services

---

## Architecture Decisions

### MVVM — Hand-rolled (no CommunityToolkit)
- `ObservableObject` — `INotifyPropertyChanged` + `SetProperty<T>`
- `RelayCommand` / `AsyncRelayCommand` — `ICommand` implementations
- `ViewModelBase` — adds `IsBusy`, `Title`

**Rationale:** Minimal dependencies, full control, easy to understand.

### Folder Structure
```
src/BodyCam/
├── Agents/           Agent classes (VoiceIn, Conversation, VoiceOut, Vision)
├── Models/           SessionContext, ChatMessage
├── Mvvm/             ObservableObject, RelayCommand, AsyncRelayCommand, ViewModelBase
├── Orchestration/    AgentOrchestrator
├── Services/         Interfaces + stub implementations
├── ViewModels/       MainViewModel
├── AppSettings.cs    Configuration
├── MainPage.xaml     UI
└── MauiProgram.cs    DI composition root
```

### DI Registration Pattern
- Services registered as singletons (one mic, one speaker, one connection)
- ViewModels registered as transient (fresh per navigation)
- Pages registered as transient (injected with ViewModel)

### Settings
- `AppSettings` class with placeholder API key
- Future: migrate to secure storage (`SecureStorage`) for keys

---

## Files Created

| File | Purpose |
|------|---------|
| `Mvvm/ObservableObject.cs` | INotifyPropertyChanged base |
| `Mvvm/RelayCommand.cs` | Sync ICommand |
| `Mvvm/AsyncRelayCommand.cs` | Async ICommand with guard |
| `Mvvm/ViewModelBase.cs` | ViewModel base class |
| `Services/IAudioInputService.cs` | Mic capture interface |
| `Services/IAudioOutputService.cs` | Speaker playback interface |
| `Services/ICameraService.cs` | Camera frame interface |
| `Services/IOpenAiStreamingClient.cs` | OpenAI streaming interface |
| `Services/AudioInputService.cs` | Stub mic impl |
| `Services/AudioOutputService.cs` | Stub speaker impl |
| `Services/CameraService.cs` | Stub camera impl |
| `Services/OpenAiStreamingClient.cs` | Stub OpenAI impl |
| `Agents/VoiceInputAgent.cs` | Mic → transcription agent |
| `Agents/ConversationAgent.cs` | Reasoning agent |
| `Agents/VoiceOutputAgent.cs` | TTS → speaker agent |
| `Agents/VisionAgent.cs` | Camera → description agent |
| `Orchestration/AgentOrchestrator.cs` | Agent pipeline coordinator |
| `Models/SessionContext.cs` | Session + message models |
| `ViewModels/MainViewModel.cs` | Main page ViewModel |
| `AppSettings.cs` | Config class |
| `MainPage.xaml` | UI layout |
| `MainPage.xaml.cs` | Code-behind with DI |
| `MauiProgram.cs` | Composition root |
