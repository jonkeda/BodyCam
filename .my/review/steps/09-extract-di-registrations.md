# Step 9: Extract DI Registrations into Extension Methods

**Priority:** P2 | **Effort:** Small | **Risk:** MauiProgram.cs is 160+ lines of DI registration

---

## Problem

`MauiProgram.CreateMauiApp` is a single method with ~160 lines mixing DI registration for audio, camera, agents, tools, settings, and orchestration. Hard to navigate, easy to create ordering bugs.

## Steps

### 9.1 Create ServiceExtensions class

**File:** `src/BodyCam/ServiceExtensions.cs` (new file)

```csharp
using BodyCam.Agents;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Input;
using BodyCam.Tools;
using BodyCam.ViewModels;
using Microsoft.Extensions.AI;
using OpenAI;

namespace BodyCam;

public static class ServiceExtensions
{
    public static IServiceCollection AddAudioServices(this IServiceCollection services)
    {
#if WINDOWS
        services.AddSingleton<IAudioInputProvider, Platforms.Windows.PlatformMicProvider>();
        services.AddSingleton<Platforms.Windows.Audio.WindowsBluetoothEnumerator>();
        services.AddSingleton<IAudioOutputProvider, Platforms.Windows.WindowsSpeakerProvider>();
        services.AddSingleton<Platforms.Windows.Audio.WindowsBluetoothOutputEnumerator>();
#elif ANDROID
        services.AddSingleton<IAudioInputProvider, Platforms.Android.PlatformMicProvider>();
        services.AddSingleton<Platforms.Android.Audio.AndroidBluetoothEnumerator>();
        services.AddSingleton<IAudioOutputProvider, Platforms.Android.PhoneSpeakerProvider>();
        services.AddSingleton<Platforms.Android.Audio.AndroidBluetoothOutputEnumerator>();
#endif
        services.AddSingleton<AudioInputManager>();
        services.AddSingleton<IAudioInputService>(sp => sp.GetRequiredService<AudioInputManager>());
        services.AddSingleton<AudioOutputManager>();
        services.AddSingleton<IAudioOutputService>(sp => sp.GetRequiredService<AudioOutputManager>());
        return services;
    }

    public static IServiceCollection AddCameraServices(this IServiceCollection services)
    {
#if WINDOWS
        services.AddSingleton<ICameraService, Platforms.Windows.WindowsCameraService>();
#elif ANDROID
        services.AddSingleton<ICameraService, Platforms.Android.AndroidCameraService>();
#else
        services.AddSingleton<ICameraService, CameraService>();
#endif
        services.AddSingleton<PhoneCameraProvider>();
        services.AddSingleton<ICameraProvider>(sp => sp.GetRequiredService<PhoneCameraProvider>());
        services.AddSingleton<CameraManager>();
        return services;
    }

    public static IServiceCollection AddAgents(this IServiceCollection services)
    {
        services.AddSingleton<VoiceInputAgent>();
        services.AddSingleton<ConversationAgent>();
        services.AddSingleton<VoiceOutputAgent>();
        services.AddSingleton<VisionAgent>();
        return services;
    }

    public static IServiceCollection AddTools(this IServiceCollection services)
    {
        services.AddSingleton<ITool, DescribeSceneTool>();
        services.AddSingleton<ITool, DeepAnalysisTool>();
        services.AddSingleton<ITool, ReadTextTool>();
        services.AddSingleton<ITool, TakePhotoTool>();
        services.AddSingleton<ITool, SaveMemoryTool>();
        services.AddSingleton<ITool, RecallMemoryTool>();
        services.AddSingleton<ITool, SetTranslationModeTool>();
        services.AddSingleton<ITool, MakePhoneCallTool>();
        services.AddSingleton<ITool, SendMessageTool>();
        services.AddSingleton<ITool, LookupAddressTool>();
        services.AddSingleton<ITool, FindObjectTool>();
        services.AddSingleton<ITool, NavigateToTool>();
        services.AddSingleton<ITool, StartSceneWatchTool>();
        services.AddSingleton<ToolDispatcher>();
        return services;
    }

    public static IServiceCollection AddOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<IWakeWordService, Services.WakeWord.PorcupineWakeWordService>();
        services.AddSingleton<IMicrophoneCoordinator, MicrophoneCoordinator>();
        services.AddSingleton<IRealtimeClient, RealtimeClient>();
        services.AddSingleton<AgentOrchestrator>();
        return services;
    }

    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<MainPage>();
        services.AddTransient<SettingsPage>();
        return services;
    }
}
```

### 9.2 Simplify MauiProgram.CreateMauiApp

**File:** `src/BodyCam/MauiProgram.cs`

Replace the DI registration block with:

```csharp
builder.Services
    .AddAudioServices()
    .AddCameraServices()
    .AddAgents()
    .AddTools()
    .AddOrchestration()
    .AddViewModels();
```

Keep the settings setup (AppSettings / ISettingsService / .env overrides) and the `IChatClient` factory registration in `MauiProgram` since they have complex initialization logic.

### 9.3 Build and run tests

```powershell
dotnet build src/BodyCam/BodyCam.csproj -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
dotnet test src/BodyCam.Tests/BodyCam.Tests.csproj -f net10.0-windows10.0.19041.0
```

Also verify integration tests still pass — they use `BodyCamTestHost` which has its own DI setup, so they should be unaffected.
