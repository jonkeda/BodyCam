# M5 Phase 4 — iOS Platform Support

Port wake word detection to iOS using Porcupine's iOS binding. Handle
`AVAudioSession` configuration, background audio mode, and iOS-specific
audio interruptions.

**Depends on:** M5 Phase 1 (Porcupine core), Phase 3 (Android — establishes
the cross-platform pattern).

---

## Wave 1: Porcupine iOS Package

### 1.1 — Add iOS-Specific NuGet

```xml
<!-- BodyCam.csproj -->
<PackageReference Include="Porcupine.iOS" Version="4.*" Condition="$(TargetFramework.Contains('ios'))" />
```

Porcupine's iOS package wraps the native framework. The C# API surface should
be identical to Windows/Android. Verify `PorcupineWakeWordService` compiles
without `#if` branches.

### 1.2 — Build Verification

```powershell
dotnet build -f net10.0-ios
```

If API differences exist between packages, isolate them in
`KeywordPathResolver` or a thin platform adapter.

---

## Wave 2: iOS `.ppn` Keyword Files

### 2.1 — Generate iOS Keywords

Same 9 keywords, targeting iOS in Picovoice Console:

| Keyword | File (iOS) |
|---------|------------|
| "Hey BodyCam" | `hey-bodycam_en_ios.ppn` |
| "Go to sleep" | `go-to-sleep_en_ios.ppn` |
| "bodycam-look" | `bodycam-look_en_ios.ppn` |
| "bodycam-read" | `bodycam-read_en_ios.ppn` |
| "bodycam-find" | `bodycam-find_en_ios.ppn` |
| "bodycam-remember" | `bodycam-remember_en_ios.ppn` |
| "bodycam-translate" | `bodycam-translate_en_ios.ppn` |
| "bodycam-call" | `bodycam-call_en_ios.ppn` |
| "bodycam-navigate" | `bodycam-navigate_en_ios.ppn` |

### 2.2 — Embed as Platform-Specific Assets

```xml
<ItemGroup Condition="$(TargetFramework.Contains('ios'))">
  <MauiAsset Include="Resources\WakeWords\*_ios.ppn" />
</ItemGroup>
```

### 2.3 — KeywordPathResolver Verification

Confirm `DeviceInfo.Platform == DevicePlatform.iOS` resolves to `"ios"` suffix.

---

## Wave 3: AVAudioSession Configuration

iOS requires explicit audio session configuration. Wake word detection needs
the session active in background with microphone input.

### 3.1 — Audio Session Setup

```csharp
// Platforms/iOS/Audio/WakeWordAudioSessionConfigurator.cs
using AVFoundation;

public static class WakeWordAudioSessionConfigurator
{
    public static void Configure()
    {
        var session = AVAudioSession.SharedInstance();

        // PlayAndRecord: allows simultaneous mic input + speaker output
        // SpokenAudio: system ducks other audio when BodyCam speaks
        session.SetCategory(
            AVAudioSessionCategory.PlayAndRecord,
            AVAudioSessionCategoryOptions.DefaultToSpeaker
                | AVAudioSessionCategoryOptions.AllowBluetooth
                | AVAudioSessionCategoryOptions.AllowBluetoothA2DP,
            out var categoryError);

        if (categoryError is not null)
            throw new InvalidOperationException($"AVAudioSession category error: {categoryError}");

        session.SetMode(AVAudioSessionMode.Measurement, out var modeError);
        if (modeError is not null)
            throw new InvalidOperationException($"AVAudioSession mode error: {modeError}");

        session.SetActive(true, out var activeError);
        if (activeError is not null)
            throw new InvalidOperationException($"AVAudioSession activation error: {activeError}");
    }
}
```

### 3.2 — Background Audio Mode

Add to `Info.plist`:

```xml
<key>UIBackgroundModes</key>
<array>
    <string>audio</string>
</array>
```

This allows the app to continue processing audio when backgrounded. iOS will
keep the app alive as long as it's actively using the audio session.

### 3.3 — Microphone Permission

Add to `Info.plist`:

```xml
<key>NSMicrophoneUsageDescription</key>
<string>BodyCam needs microphone access to listen for wake words and voice commands.</string>
```

---

## Wave 4: Audio Interruption Handling

iOS can interrupt the audio session for phone calls, Siri, alarms, and other
apps requesting audio. Porcupine must handle these gracefully.

### 4.1 — Interruption Observer

```csharp
// Platforms/iOS/Audio/AudioInterruptionHandler.cs
using AVFoundation;
using Foundation;

public class AudioInterruptionHandler : IDisposable
{
    private readonly IWakeWordService _wakeWordService;
    private NSObject? _interruptionObserver;
    private NSObject? _routeChangeObserver;

    public void StartObserving()
    {
        _interruptionObserver = AVAudioSession.Notifications
            .ObserveInterruption(OnInterruption);

        _routeChangeObserver = AVAudioSession.Notifications
            .ObserveRouteChange(OnRouteChange);
    }

    private async void OnInterruption(object? sender,
        AVAudioSessionInterruptionEventArgs e)
    {
        switch (e.InterruptionType)
        {
            case AVAudioSessionInterruptionType.Began:
                // Phone call, Siri, etc. — pause Porcupine
                await _wakeWordService.StopAsync();
                break;

            case AVAudioSessionInterruptionType.Ended:
                // Interruption over — reactivate audio session and resume
                var session = AVAudioSession.SharedInstance();
                session.SetActive(true, out _);
                await _wakeWordService.StartAsync();
                break;
        }
    }

    private void OnRouteChange(object? sender,
        AVAudioSessionRouteChangeEventArgs e)
    {
        // Handle BT connect/disconnect, headphone plug/unplug
        // Audio route changes may require restarting Porcupine
        // to pick up the new input device
        switch (e.Reason)
        {
            case AVAudioSessionRouteChangeReason.NewDeviceAvailable:
            case AVAudioSessionRouteChangeReason.OldDeviceUnavailable:
                // Restart wake word service to use new audio route
                Task.Run(async () =>
                {
                    await _wakeWordService.StopAsync();
                    await _wakeWordService.StartAsync();
                });
                break;
        }
    }

    public void Dispose()
    {
        _interruptionObserver?.Dispose();
        _routeChangeObserver?.Dispose();
    }
}
```

### 4.2 — DI Registration

```csharp
// MauiProgram.cs
#if IOS
builder.Services.AddSingleton<AudioInterruptionHandler>();
#endif
```

Start observing in app startup after wake word service is initialized.

---

## Wave 5: Integration & Testing

### 5.1 — iOS Simulator Testing

- Build and deploy to iOS Simulator
- Verify Porcupine initializes with AccessKey
- Verify `.ppn` files load from app bundle
- Note: Simulator mic support varies — may need physical device

### 5.2 — Physical Device Testing

- Say "Hey BodyCam" → verify wake word detected
- Background the app → verify wake word still fires
- Receive a phone call during listening → verify Porcupine pauses and resumes
- Trigger Siri → verify audio session recovers
- Connect/disconnect BT device → verify audio route change handled

### 5.3 — Battery Profiling

Use Xcode Instruments (Energy Log) to verify wake word layer power draw.
Target: ≤15mW with Porcupine running on iOS.

---

## Exit Criteria

- [ ] `Porcupine.iOS` NuGet compiles and initializes on iOS
- [ ] All 9 iOS `.ppn` files generated and embedded
- [ ] `AVAudioSession` configured for background audio with mic input
- [ ] `UIBackgroundModes: audio` in Info.plist
- [ ] `NSMicrophoneUsageDescription` in Info.plist
- [ ] Wake word detection works when app is backgrounded
- [ ] Audio interruptions (phone call, Siri) pause and resume cleanly
- [ ] Audio route changes (BT connect/disconnect) restart Porcupine
- [ ] Audio feedback tones play on iOS
- [ ] Battery draw ≤ 15mW in wake word layer
