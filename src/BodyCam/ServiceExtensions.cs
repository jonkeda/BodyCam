using BodyCam.Agents;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Audio.WebRtcApm;
using BodyCam.Services.Camera;
using BodyCam.Services.Input;
using BodyCam.Services.Barcode;
using BodyCam.Services.QrCode;
using BodyCam.Services.QrCode.Handlers;
using BodyCam.Services.Vision;
using BodyCam.Services.WakeWord;
using BodyCam.Tools;
using BodyCam.ViewModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

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
		services.AddSingleton<BodyCam.Platforms.Android.PlatformMicProvider>();
		services.AddSingleton<IAudioInputProvider>(sp => sp.GetRequiredService<BodyCam.Platforms.Android.PlatformMicProvider>());
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

		// Echo cancellation
		services.AddSingleton<AecProcessor>();

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
		services.AddSingleton<ITool, ScanQrCodeTool>();
		services.AddSingleton<ITool, RecallLastScanTool>();
		services.AddSingleton<ITool, LookupBarcodeTool>();
		services.AddSingleton<ITool, LookTool>();
		services.AddSingleton<ToolDispatcher>();

		return services;
	}

	public static IServiceCollection AddBarcodeServices(this IServiceCollection services)
	{
		services.AddHttpClient<OpenFoodFactsClient>(client =>
		{
			client.DefaultRequestHeaders.UserAgent.ParseAdd("BodyCam/1.0");
			client.Timeout = TimeSpan.FromSeconds(5);
		});
		services.AddHttpClient<UpcItemDbClient>(client =>
		{
			client.Timeout = TimeSpan.FromSeconds(5);
		});
		services.AddHttpClient<OpenGtinDbClient>(client =>
		{
			client.Timeout = TimeSpan.FromSeconds(5);
		});

		services.AddSingleton<IBarcodeApiClient>(sp => sp.GetRequiredService<OpenFoodFactsClient>());
		services.AddSingleton<IBarcodeApiClient>(sp => sp.GetRequiredService<UpcItemDbClient>());
		services.AddSingleton<IBarcodeApiClient>(sp => sp.GetRequiredService<OpenGtinDbClient>());
		services.AddSingleton<IBarcodeLookupService, BarcodeLookupService>();

		return services;
	}

	public static IServiceCollection AddQrCodeServices(this IServiceCollection services)
	{
		services.AddSingleton<IQrCodeScanner, ZXingQrScanner>();
		services.AddSingleton<QrCodeService>();

		// Content handlers — order matters, PlainTextContentHandler must be last (catch-all)
		services.AddSingleton<IQrContentHandler, UrlContentHandler>();
		services.AddSingleton<IQrContentHandler, WifiContentHandler>();
		services.AddSingleton<IQrContentHandler, VCardContentHandler>();
		services.AddSingleton<IQrContentHandler, EmailContentHandler>();
		services.AddSingleton<IQrContentHandler, PhoneContentHandler>();
		services.AddSingleton<IQrContentHandler, PlainTextContentHandler>();
		services.AddSingleton<QrContentResolver>();

		return services;
	}

	public static IServiceCollection AddVisionPipeline(this IServiceCollection services)
	{
		services.AddSingleton<IVisionPipelineStage, QrScanStage>();
		services.AddSingleton<IVisionPipelineStage, TextDetectionStage>();
		services.AddSingleton<IVisionPipelineStage, SceneDescriptionStage>();
		services.AddSingleton<VisionPipeline>();

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

		// MAF Realtime client — wraps the SDK's RealtimeClient with IRealtimeClient
#pragma warning disable OPENAI002
		services.AddSingleton<Microsoft.Extensions.AI.IRealtimeClient>(sp =>
		{
			var settings = sp.GetRequiredService<AppSettings>();
			var apiKeyService = sp.GetRequiredService<IApiKeyService>();

			// Use Task.Run to avoid deadlocking Android's main thread on SecureStorage
			var apiKey = Task.Run(() => apiKeyService.GetApiKeyAsync()).GetAwaiter().GetResult()
				?? throw new InvalidOperationException("API key not configured.");

			if (settings.Provider == OpenAiProvider.Azure)
			{
				var rtOptions = new OpenAI.Realtime.RealtimeClientOptions
				{
					Endpoint = new Uri($"{settings.AzureEndpoint!.TrimEnd('/')}/openai/v1/realtime")
				};
				var sdkClient = new Services.AzureRealtimeClient(apiKey, rtOptions);
				return new Microsoft.Extensions.AI.OpenAIRealtimeClient(
					sdkClient, settings.AzureRealtimeDeploymentName!);
			}
			else
			{
				return new Microsoft.Extensions.AI.OpenAIRealtimeClient(
					apiKey, settings.RealtimeModel);
			}
		});
#pragma warning restore OPENAI002

		services.AddSingleton<AgentOrchestrator>();

		return services;
	}

	public static IServiceCollection AddViewModels(this IServiceCollection services)
	{
		services.AddTransient<SetupViewModel>();
		services.AddTransient<MainViewModel>();
		services.AddTransient<SettingsViewModel>();
		services.AddTransient<ViewModels.Settings.ConnectionViewModel>();
		services.AddTransient<ViewModels.Settings.VoiceViewModel>();
		services.AddTransient<ViewModels.Settings.DeviceViewModel>();
		services.AddTransient<ViewModels.Settings.AdvancedViewModel>();
		services.AddTransient<Pages.Setup.SetupPage>();
		services.AddTransient<Pages.Main.MainPage>();
		services.AddTransient<Pages.Settings.SettingsPage>();
		services.AddTransient<Pages.Settings.ConnectionSettingsPage>();
		services.AddTransient<Pages.Settings.VoiceSettingsPage>();
		services.AddTransient<Pages.Settings.DeviceSettingsPage>();
		services.AddTransient<Pages.Settings.AdvancedSettingsPage>();

		return services;
	}
}
