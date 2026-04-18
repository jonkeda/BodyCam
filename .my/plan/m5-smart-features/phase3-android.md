# M5 Phase 3 — Android Platform Support

Port wake word detection to Android using Porcupine's Android package. Generate
Android-specific `.ppn` files. Handle background service and audio permissions.

**Depends on:** M5 Phase 1 (Porcupine on Windows working), Phase 2 (session flow).

---

## Wave 1: Porcupine Android Package

### 1.1 — Add Android-Specific NuGet

```xml
<!-- BodyCam.csproj -->
<PackageReference Include="Porcupine" Version="4.*" Condition="'$(TargetFramework)' == 'net10.0-windows10.0.19041.0'" />
<PackageReference Include="Porcupine.Android" Version="4.*" Condition="$(TargetFramework.Contains('android'))" />
```

Porcupine's Android package wraps the native `.so` library and handles JNI
initialization. The C# API surface is identical to the Windows package —
`PorcupineWakeWordService` should work as-is with no `#if` branches.

### 1.2 — Verify Shared Code Compiles on Android

Build for both targets:

```powershell
dotnet build -f net10.0-windows10.0.19041.0
dotnet build -f net10.0-android
```

If Porcupine's API differs between packages, add a thin platform adapter.

---

## Wave 2: Android `.ppn` Keyword Files

### 2.1 — Generate Android Keywords

Same keywords as Windows, but targeting the Android platform in Picovoice Console:

| Keyword | File (Android) |
|---------|----------------|
| "Hey BodyCam" | `hey-bodycam_en_android.ppn` |
| "Go to sleep" | `go-to-sleep_en_android.ppn` |
| "bodycam-look" | `bodycam-look_en_android.ppn` |
| "bodycam-read" | `bodycam-read_en_android.ppn` |
| "bodycam-find" | `bodycam-find_en_android.ppn` |
| "bodycam-remember" | `bodycam-remember_en_android.ppn` |
| "bodycam-translate" | `bodycam-translate_en_android.ppn` |
| "bodycam-call" | `bodycam-call_en_android.ppn` |
| "bodycam-navigate" | `bodycam-navigate_en_android.ppn` |

### 2.2 — Embed as Platform-Specific Assets

```xml
<ItemGroup Condition="$(TargetFramework.Contains('android'))">
  <MauiAsset Include="Resources\WakeWords\*_android.ppn" />
</ItemGroup>
<ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows10.0.19041.0'">
  <MauiAsset Include="Resources\WakeWords\*_windows.ppn" />
</ItemGroup>
```

### 2.3 — KeywordPathResolver

The `KeywordPathResolver` from Phase 1 already handles platform selection.
Verify it resolves correctly on Android:

```csharp
// On Android: "bodycam-look_en_android.ppn"
// On Windows: "bodycam-look_en_windows.ppn"
var path = KeywordPathResolver.Resolve("bodycam-look");
```

---

## Wave 3: Android Audio Permissions

### 3.1 — Permissions

Wake word detection requires microphone access. BodyCam already requests this
for the Realtime API audio pipeline — verify the existing permission flow
covers always-on wake word listening.

```xml
<!-- AndroidManifest.xml (already present) -->
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```

### 3.2 — Runtime Permission Check

In `PorcupineWakeWordService.StartAsync()`, check and request microphone
permission before initializing:

```csharp
#if ANDROID
var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
if (status != PermissionStatus.Granted)
{
    status = await Permissions.RequestAsync<Permissions.Microphone>();
    if (status != PermissionStatus.Granted)
        throw new PermissionException("Microphone permission required for wake word detection.");
}
#endif
```

---

## Wave 4: Android Foreground Service

For wake word detection to continue when the app is in the background, Android
requires a foreground service with a persistent notification.

### 4.1 — WakeWordForegroundService

```csharp
// Platforms/Android/Services/WakeWordForegroundService.cs
[Service(ForegroundServiceType = Android.Content.PM.ForegroundService.TypeMicrophone)]
public class WakeWordForegroundService : Android.App.Service
{
    public override StartCommandResult OnStartCommand(
        Android.Content.Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = BuildNotification("BodyCam is listening for wake words");
        StartForeground(1001, notification, Android.Content.PM.ForegroundService.TypeMicrophone);
        return StartCommandResult.Sticky;
    }

    private Android.App.Notification BuildNotification(string text)
    {
        var channelId = "bodycam_wakeword";
        var channel = new Android.App.NotificationChannel(
            channelId, "Wake Word", Android.App.NotificationImportance.Low);

        var manager = GetSystemService(NotificationService) as Android.App.NotificationManager;
        manager?.CreateNotificationChannel(channel);

        return new Android.App.Notification.Builder(this, channelId)
            .SetContentTitle("BodyCam")
            .SetContentText(text)
            .SetSmallIcon(Resource.Drawable.ic_mic)
            .SetOngoing(true)
            .Build()!;
    }
}
```

### 4.2 — AndroidManifest Additions

```xml
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_MICROPHONE" />
```

### 4.3 — Service Lifecycle

- Start foreground service when entering wake word layer
- Stop service when going to sleep or when app is closed
- Service keeps Porcupine listening in the background

---

## Wave 5: Platform Audio Routing

### 5.1 — Android Audio Source Selection

On Android, `AudioRecord` can select which audio source to use. For wake word
detection, use `AudioSource.Default` (picks the best mic available — BT headset
if connected, phone mic otherwise).

Verify that `PlatformMicProvider` (from M12) feeds audio to both Porcupine (in
wake word layer) and the Realtime API (in active session) through the same
`IAudioInputService` abstraction.

### 5.2 — BT Mic Priority

When BT glasses are connected, the mic input should come from the glasses mic.
This is handled by `AudioInputManager` (M12) selecting the BT provider.
Porcupine receives audio from whatever provider is active — no special handling
needed in the wake word service.

### 5.3 — Integration Test

Deploy to Android emulator or device:
- Say "Hey BodyCam" → verify wake word detected
- Verify foreground notification appears in wake word layer
- Background the app → verify wake word still works
- Connect BT device → verify wake word detects from BT mic

---

## Exit Criteria

- [ ] Porcupine.Android NuGet compiles and initializes
- [ ] All 9 Android `.ppn` files generated and embedded
- [ ] `KeywordPathResolver` selects correct platform files
- [ ] Microphone permission requested at runtime
- [ ] Foreground service keeps listening in background
- [ ] Wake word detection works from phone mic
- [ ] Wake word detection works from BT mic (when connected)
- [ ] Layer transitions (wake word → active → wake word) work on Android
- [ ] Audio feedback tones play on Android
