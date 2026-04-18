# Step 14: Configurable Mic Release Delay

**Priority:** P3 | **Effort:** Trivial | **Risk:** Hardcoded 50ms may be too short on some Android devices

---

## Problem

`MicrophoneCoordinator` has a hardcoded `50ms` delay for microphone release between wake word engine and active session. Some Android OEMs hold the mic resource longer. The delay should be configurable.

Current code:

```csharp
private static readonly TimeSpan MicReleaseDelay = TimeSpan.FromMilliseconds(50);
```

## Steps

### 14.1 Add MicReleaseDelayMs to AppSettings

**File:** `src/BodyCam/AppSettings.cs`

Add property:

```csharp
public int MicReleaseDelayMs { get; set; } = 50;
```

### 14.2 Inject AppSettings into MicrophoneCoordinator

**File:** `src/BodyCam/Services/MicrophoneCoordinator.cs`

```csharp
public class MicrophoneCoordinator : IMicrophoneCoordinator
{
    private readonly IWakeWordService _wakeWord;
    private readonly AppSettings _settings;

    public MicrophoneCoordinator(IWakeWordService wakeWord, AppSettings settings)
    {
        _wakeWord = wakeWord;
        _settings = settings;
    }

    public async Task TransitionToActiveSessionAsync()
    {
        if (_wakeWord.IsListening)
            await _wakeWord.StopAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(_settings.MicReleaseDelayMs));
    }

    public async Task TransitionToWakeWordAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(_settings.MicReleaseDelayMs));

        if (!_wakeWord.IsListening)
            await _wakeWord.StartAsync();
    }
}
```

### 14.3 Update DI registration

No change needed — `AppSettings` is already registered as singleton. The DI container will inject it.

### 14.4 Update test mocks

If `MicrophoneCoordinator` tests create the coordinator directly, add `new AppSettings()` to the constructor call.

### 14.5 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```
