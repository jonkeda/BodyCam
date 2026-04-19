using BodyCam.Agents;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Input;
using BodyCam.Services.WakeWord;
using BodyCam.Tools;
using BodyCam.ViewModels;

namespace BodyCam;

public static class ServiceExtensions
{
	public static IServiceCollection AddAudioServices(this IServiceCollection services)
	{
		// Audio input abstraction
#if WINDOWS
		services.AddSingleton<IAudioInputProvider, BodyCam.Platforms.Windows.PlatformMicProvider>();
		services.AddSingleton<BodyCam.Platforms.Windows.Audio.WindowsBluetoothEnumerator>();
#elif ANDROID
		services.AddSingleton<IAudioInputProvider, BodyCam.Platforms.Android.PlatformMicProvider>();
		services.AddSingleton<BodyCam.Platforms.Android.Audio.AndroidBluetoothEnumerator>();
#endif
		services.AddSingleton<AudioInputManager>();
		services.AddSingleton<IAudioInputService>(sp => sp.GetRequiredService<AudioInputManager>());

		// Audio output abstraction
#if WINDOWS
		services.AddSingleton<IAudioOutputProvider, BodyCam.Platforms.Windows.WindowsSpeakerProvider>();
		services.AddSingleton<BodyCam.Platforms.Windows.Audio.WindowsBluetoothOutputEnumerator>();
#elif ANDROID
		services.AddSingleton<IAudioOutputProvider, BodyCam.Platforms.Android.PhoneSpeakerProvider>();
		services.AddSingleton<BodyCam.Platforms.Android.Audio.AndroidBluetoothOutputEnumerator>();
#endif
		services.AddSingleton<AudioOutputManager>();
		services.AddSingleton<IAudioOutputService>(sp => sp.GetRequiredService<AudioOutputManager>());

		return services;
	}

	public static IServiceCollection AddCameraServices(this IServiceCollection services)
	{
#if WINDOWS
		services.AddSingleton<ICameraService, BodyCam.Platforms.Windows.WindowsCameraService>();
#elif ANDROID
		services.AddSingleton<ICameraService, BodyCam.Platforms.Android.AndroidCameraService>();
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
		// Button input
#if WINDOWS
		services.AddSingleton<IButtonInputProvider, BodyCam.Platforms.Windows.Input.KeyboardShortcutProvider>();
#endif
		services.AddSingleton<ButtonInputManager>();

		services.AddSingleton<PorcupineWakeWordService>();
		services.AddSingleton<IWakeWordService>(sp => sp.GetRequiredService<PorcupineWakeWordService>());
		services.AddSingleton<IMicrophoneCoordinator, MicrophoneCoordinator>();
		services.AddSingleton<IApiKeyService, ApiKeyService>();
		services.AddSingleton<IRealtimeClient, RealtimeClient>();
		services.AddSingleton<AgentOrchestrator>();

		return services;
	}

	public static IServiceCollection AddViewModels(this IServiceCollection services)
	{
		services.AddTransient<SetupViewModel>();
		services.AddTransient<MainViewModel>();
		services.AddTransient<SettingsViewModel>();
		services.AddTransient<SetupPage>();
		services.AddTransient<MainPage>();
		services.AddTransient<SettingsPage>();

		return services;
	}
}
