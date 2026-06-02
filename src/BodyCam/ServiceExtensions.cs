using BodyCam.Agents;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.AiProviders;
using BodyCam.Services.Audio;
using BodyCam.Services.Audio.WebRtcApm;
using BodyCam.Services.Camera;
using BodyCam.Services.Camera.A9;
using BodyCam.Services.Camera.A9.Vue990;
using BodyCam.Services.Camera.Commands;
using BodyCam.Services.Camera.Usb;
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
#elif IOS
		// Shared AVAudioEngine for mic+speaker (VoiceProcessingIO requires both in the same engine)
		services.AddSingleton(sp => new AVFoundation.AVAudioEngine());
		services.AddSingleton<BodyCam.Platforms.iOS.PlatformMicProvider>();
		services.AddSingleton<IAudioInputProvider>(sp => sp.GetRequiredService<BodyCam.Platforms.iOS.PlatformMicProvider>());
#endif
		services.AddSingleton<AudioInputManager>();
		services.AddSingleton<IAudioInputService>(sp => sp.GetRequiredService<AudioInputManager>());

		// Generic Bluetooth audio input provider (MAC-aware selection)
		services.AddSingleton<BluetoothAudioInputProvider>();
		services.AddSingleton<IBluetoothAudioInputProvider>(sp => sp.GetRequiredService<BluetoothAudioInputProvider>());

		// HeyCyan glasses audio input provider (wraps generic BT provider)
		// Registered as concrete only — NOT as IAudioInputProvider — to avoid circular DI:
		// AudioInputManager → IEnumerable<IAudioInputProvider> → HeyCyanAudioInputProvider
		//   → IBluetoothAudioInputProvider → BluetoothAudioInputProvider → AudioInputManager
		// HeyCyanAudioRouter dynamically registers/unregisters with the managers on connect/disconnect.
		services.AddSingleton<Services.Glasses.HeyCyan.HeyCyanAudioInputProvider>();

		// Audio output abstraction
#if WINDOWS
		services.AddSingleton<IAudioOutputProvider, BodyCam.Platforms.Windows.WindowsSpeakerProvider>();
		services.AddSingleton<BodyCam.Platforms.Windows.Audio.WindowsBluetoothOutputEnumerator>();
#elif ANDROID
		services.AddSingleton<IAudioOutputProvider, BodyCam.Platforms.Android.PhoneSpeakerProvider>();
		services.AddSingleton<BodyCam.Platforms.Android.Audio.AndroidBluetoothOutputEnumerator>();
#elif IOS
		services.AddSingleton<IAudioOutputProvider, BodyCam.Platforms.iOS.PhoneSpeakerProvider>();
#endif
		services.AddSingleton<AudioOutputManager>();
		services.AddSingleton<IAudioOutputService>(sp => sp.GetRequiredService<AudioOutputManager>());

		// Generic Bluetooth audio output provider (MAC-aware selection)
		services.AddSingleton<BluetoothAudioOutputProvider>();
		services.AddSingleton<IBluetoothAudioOutputProvider>(sp => sp.GetRequiredService<BluetoothAudioOutputProvider>());

		// HeyCyan glasses audio output provider — concrete only (same circular DI reason as input)
		services.AddSingleton<Services.Glasses.HeyCyan.HeyCyanAudioOutputProvider>();

		// HeyCyan auto-routing service (watches session state, flips active providers)
		services.AddSingleton<Services.Glasses.HeyCyan.HeyCyanAudioRouter>();

		// HeyCyan audio diagnostics (codec verification)
#if ANDROID
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanCodecProbe, BodyCam.Platforms.Android.HeyCyan.HeyCyanCodecProbe>();
#elif IOS
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanCodecProbe, BodyCam.Platforms.iOS.HeyCyanCodecProbe>();
#else
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanCodecProbe, Services.Glasses.HeyCyan.NullCodecProbe>();
#endif
		services.AddSingleton<Services.Glasses.HeyCyan.HeyCyanAudioDiagnostics>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanAudioDiagnostics>(sp =>
			sp.GetRequiredService<Services.Glasses.HeyCyan.HeyCyanAudioDiagnostics>());

		// Clock drift monitoring (Phase 6.2)
		services.AddSingleton<Services.Audio.ClockDriftMonitor>();

		// Echo cancellation
#if WINDOWS
		// Conditional: Windows Voice Capture DMO (deprecated, opt-in fallback)
		services.AddSingleton<IAecProcessor>(sp =>
		{
			var settings = sp.GetRequiredService<AppSettings>();
			var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

			if (settings.WindowsUseVoiceCaptureDmo)
			{
				var dmoLogger = loggerFactory.CreateLogger<BodyCam.Platforms.Windows.Audio.VoiceCaptureDmoAecProcessor>();
				var dmo = new BodyCam.Platforms.Windows.Audio.VoiceCaptureDmoAecProcessor(dmoLogger);
				dmo.Initialize();
				return dmo;
			}
			else
			{
				return CreateWebRtcAecProcessor(sp, mobileMode: false);
			}
		});
#else
		services.AddSingleton<IAecProcessor>(sp =>
		{
#if ANDROID || IOS
			return CreateWebRtcAecProcessor(sp, mobileMode: true);
#else
			return CreateWebRtcAecProcessor(sp, mobileMode: false);
#endif
		});
#endif

		// Route monitoring for AEC bypass
#if WINDOWS
		services.AddSingleton<BodyCam.Services.Audio.IRouteMonitor, BodyCam.Platforms.Windows.WindowsRouteMonitor>();
#elif ANDROID
		services.AddSingleton<BodyCam.Services.Audio.IRouteMonitor, BodyCam.Platforms.Android.AndroidRouteMonitor>();
#elif IOS
		services.AddSingleton<BodyCam.Services.Audio.IRouteMonitor, BodyCam.Platforms.iOS.IosRouteMonitor>();
#endif
		services.AddSingleton<BodyCam.Services.Audio.IAudioRoutePolicyService, BodyCam.Services.Audio.AudioRoutePolicyService>();
		services.AddSingleton<BodyCam.Services.Audio.AecBypassManager>();

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

		services.AddSingleton<A9CameraProvider>();
		services.AddSingleton<ICameraProvider>(sp => sp.GetRequiredService<A9CameraProvider>());

		services.AddSingleton<IA9Vue990DirectCaptureClient, A9Vue990DirectCaptureClient>();
		services.AddSingleton<Vue990CameraProvider>();
		services.AddSingleton<ICameraProvider>(sp => sp.GetRequiredService<Vue990CameraProvider>());

#if WINDOWS
		services.AddSingleton<IUsbCameraClient, WindowsUsbCameraClient>();
		services.AddSingleton<UsbCameraProvider>();
		services.AddSingleton<ICameraProvider>(sp => sp.GetRequiredService<UsbCameraProvider>());
#endif

#if ANDROID
		// Use HeyCyan-aware selector on Android; default selector elsewhere
		services.AddSingleton<ICameraProviderSelector, HeyCyanCameraSelector>();
#else
		services.AddSingleton<ICameraProviderSelector, DefaultCameraSelector>();
#endif

		services.AddSingleton<CameraManager>();
		services.AddSingleton<IManualCameraCaptureCoordinator, ManualCameraCaptureCoordinator>();
		services.AddSingleton<ICameraCommand, LookCommand>();
		services.AddSingleton<ICameraCommand, ReadCommand>();
		services.AddSingleton<ICameraCommand, ScanCommand>();
		services.AddSingleton<ICameraCommandRegistry, CameraCommandRegistry>();
		services.AddSingleton<ICameraCommandService, CameraCommandService>();

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
		// Source profiles
#if ANDROID || IOS
		services.AddSingleton<ISourceProfile, PhoneSourceProfile>();
#elif WINDOWS
		services.AddSingleton<ISourceProfile, LaptopSourceProfile>();
#endif
		services.AddSingleton<ISourceProfile, HeyCyanSourceProfile>();
		services.AddSingleton<ISourceProfile, BluetoothSourceProfile>();
		services.AddSingleton<ISourceProfile, CustomSourceProfile>();
		services.AddSingleton<SourceProfileManager>();
		services.AddSingleton<KnownDeviceService>();

		// Button input
#if WINDOWS
		services.AddSingleton<IButtonInputProvider, BodyCam.Platforms.Windows.Input.KeyboardShortcutProvider>();
#endif
		services.AddSingleton<ActionMap>();
		services.AddSingleton<IButtonMappingStore, ButtonMappingStore>();
		services.AddSingleton<ButtonInputManager>();

		services.AddSingleton<PorcupineWakeWordService>();
		services.AddSingleton<IWakeWordService>(sp => sp.GetRequiredService<PorcupineWakeWordService>());
		services.AddSingleton<IMicrophoneCoordinator, MicrophoneCoordinator>();
		services.AddSingleton<IApiKeyService, ApiKeyService>();
		services.AddSingleton<IAiProviderAdapter, OpenAiProviderAdapter>();
		services.AddSingleton<IAiProviderAdapter, AzureOpenAiProviderAdapter>();
		services.AddSingleton<IAiProviderAdapter, XaiGrokProviderAdapter>();
		services.AddSingleton<IAiProviderRegistry, AiProviderRegistry>();
		services.AddSingleton<IAiProviderInstanceStore, AiProviderInstanceStore>();
		services.AddSingleton<IAiProviderDiagnosticsService, AiProviderDiagnosticsService>();
		services.AddSingleton<IGrokEphemeralTokenBroker, GrokEphemeralTokenBroker>();
		services.AddSingleton<IGrokRealtimeVoiceProvider, GrokRealtimeVoiceProvider>();

		// MAF Realtime client — wraps the SDK's RealtimeClient with IRealtimeClient
#pragma warning disable OPENAI002
		services.AddSingleton<Microsoft.Extensions.AI.IRealtimeClient>(sp =>
		{
			var settings = sp.GetRequiredService<AppSettings>();
			var apiKeyService = sp.GetRequiredService<IApiKeyService>();
			var provider = sp.GetRequiredService<IAiProviderRegistry>().GetRequired(settings.ProviderId);

			// Use Task.Run to avoid deadlocking Android's main thread on SecureStorage
			var apiKey = Task.Run(() => apiKeyService.GetApiKeyAsync(settings.ProviderId)).GetAwaiter().GetResult()
				?? throw new InvalidOperationException("API key not configured.");

			if (provider.Id == AiProviderIds.AzureOpenAi)
			{
				var rtOptions = new OpenAI.Realtime.RealtimeClientOptions
				{
					Endpoint = new Uri($"{settings.AzureEndpoint!.TrimEnd('/')}/openai/v1/realtime")
				};
				var sdkClient = new Services.AzureRealtimeClient(apiKey, rtOptions);
				return new Microsoft.Extensions.AI.OpenAIRealtimeClient(
					sdkClient, settings.AzureRealtimeDeploymentName!);
			}
			else if (provider.Id == AiProviderIds.OpenAi)
			{
				return new Microsoft.Extensions.AI.OpenAIRealtimeClient(
					apiKey, settings.RealtimeModel);
			}
			else if (provider.Id == AiProviderIds.XaiGrok)
			{
				var grokRealtime = sp.GetRequiredService<IGrokRealtimeVoiceProvider>()
					.CreateSessionOptions(settings);
				return new UnsupportedRealtimeClient(
					$"Grok realtime voice is configured for {grokRealtime.WebSocketUri}; the Android realtime session still needs the device audio-route implementation.");
			}

			throw new InvalidOperationException(
				$"Realtime client creation is not implemented for provider '{provider.DisplayName}'.");
		});
#pragma warning restore OPENAI002

		services.AddSingleton<AgentOrchestrator>();

		return services;
	}

	public static IServiceCollection AddGlassesServices(this IServiceCollection services)
	{
		// Platform-specific SDK bridges and sessions
#if ANDROID
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanSdkBridge, BodyCam.Platforms.Android.HeyCyan.HeyCyanSdkBridge>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanGlassesSession, Services.Glasses.HeyCyan.AndroidHeyCyanGlassesSession>();
		services.AddTransient<BodyCam.Platforms.Android.HeyCyan.WiFiP2pHttpClient>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanHttpClientFactory, BodyCam.Platforms.Android.HeyCyan.AndroidHeyCyanHttpClientFactory>();
		services.AddSingleton<Services.Glasses.HeyCyan.Media.IMediaStore, BodyCam.Platforms.Android.HeyCyan.AndroidMediaStore>();
		services.AddSingleton<Services.Glasses.HeyCyan.Media.IMediaDurationProbe, BodyCam.Platforms.Android.HeyCyan.AndroidMediaDurationProbe>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanAudioEndpointActivationService, Services.Glasses.HeyCyan.NullHeyCyanAudioEndpointActivationService>();
#elif IOS
		services.AddSingleton<BodyCam.Platforms.iOS.HeyCyan.HotspotHttpClient>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanHttpClientFactory, BodyCam.Platforms.iOS.HeyCyan.IosHeyCyanHttpClientFactory>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanGlassesSession, BodyCam.Platforms.iOS.HeyCyan.IosHeyCyanGlassesSession>();
		services.AddSingleton<Services.Glasses.HeyCyan.Media.IMediaStore, BodyCam.Platforms.iOS.HeyCyan.IosMediaStore>();
		services.AddSingleton<Services.Glasses.HeyCyan.Media.IMediaDurationProbe, BodyCam.Platforms.iOS.HeyCyan.IosMediaDurationProbe>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanAudioEndpointActivationService, Services.Glasses.HeyCyan.NullHeyCyanAudioEndpointActivationService>();
#elif WINDOWS
		services.AddSingleton<BodyCam.Platforms.Windows.HeyCyan.WindowsGlassesWiFiManager>();
		services.AddSingleton<BodyCam.Platforms.Windows.HeyCyan.WindowsWiFiDirectManager>(sp =>
			new BodyCam.Platforms.Windows.HeyCyan.WindowsWiFiDirectManager(
				sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BodyCam.Platforms.Windows.HeyCyan.WindowsWiFiDirectManager>>()));
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanGlassesSession>(sp =>
			new BodyCam.Platforms.Windows.HeyCyan.WindowsHeyCyanGlassesSession(
				sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BodyCam.Platforms.Windows.HeyCyan.WindowsHeyCyanGlassesSession>>(),
				sp.GetRequiredService<BodyCam.Platforms.Windows.Audio.WindowsBluetoothEnumerator>(),
				sp.GetRequiredService<BodyCam.Platforms.Windows.Audio.WindowsBluetoothOutputEnumerator>(),
				sp.GetRequiredService<BodyCam.Platforms.Windows.HeyCyan.WindowsGlassesWiFiManager>(),
				sp.GetRequiredService<BodyCam.Platforms.Windows.HeyCyan.WindowsWiFiDirectManager>()));
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanHttpClientFactory, BodyCam.Platforms.Windows.HeyCyan.WindowsHeyCyanHttpClientFactory>();
		services.AddSingleton<Services.Glasses.HeyCyan.Media.IMediaStore, Services.Glasses.HeyCyan.Media.NoopMediaStore>();
		services.AddSingleton<Services.Glasses.HeyCyan.Media.IMediaDurationProbe, Services.Glasses.HeyCyan.Media.NoopMediaDurationProbe>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanAudioEndpointActivationService, BodyCam.Platforms.Windows.HeyCyan.WindowsHeyCyanAudioEndpointActivationService>();
#else
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanGlassesSession, Services.Glasses.HeyCyan.NullHeyCyanGlassesSession>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanHttpClientFactory, Services.Glasses.HeyCyan.NullHeyCyanHttpClientFactory>();
		services.AddSingleton<Services.Glasses.HeyCyan.Media.IMediaStore, Services.Glasses.HeyCyan.Media.NoopMediaStore>();
		services.AddSingleton<Services.Glasses.HeyCyan.Media.IMediaDurationProbe, Services.Glasses.HeyCyan.Media.NoopMediaDurationProbe>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanAudioEndpointActivationService, Services.Glasses.HeyCyan.NullHeyCyanAudioEndpointActivationService>();
#endif

		// Cross-platform HeyCyan providers (consume IHeyCyanGlassesSession)
		services.AddSingleton<Services.Glasses.HeyCyan.HeyCyanCameraProvider>();
		services.AddSingleton<ICameraProvider>(sp => sp.GetRequiredService<Services.Glasses.HeyCyan.HeyCyanCameraProvider>());
		services.AddSingleton<Services.Glasses.HeyCyan.HeyCyanButtonProvider>();
		services.AddSingleton<IButtonInputProvider>(sp => sp.GetRequiredService<Services.Glasses.HeyCyan.HeyCyanButtonProvider>());
		services.AddSingleton<Services.Glasses.HeyCyan.Media.ISidecarWriter, Services.Glasses.HeyCyan.Media.JsonSidecarWriter>();
		services.AddSingleton<Services.Glasses.HeyCyan.HeyCyanMediaTransfer>();
		services.AddSingleton<Services.Glasses.HeyCyan.StoredImageHeyCyanMediaTransfer>();
		services.AddSingleton<Services.Glasses.HeyCyan.IHeyCyanMediaTransfer>(sp =>
		{
			return sp.GetRequiredService<Services.Glasses.HeyCyan.HeyCyanMediaTransfer>();
		});
		services.AddSingleton<Services.Glasses.HeyCyan.Media.IHeyCyanRecordedMediaService, Services.Glasses.HeyCyan.Media.HeyCyanRecordedMediaService>();
	// Glasses device manager (aggregates session + providers)
	services.AddSingleton<Services.Glasses.HeyCyan.HeyCyanGlassesDeviceManager>();
	services.AddSingleton<Services.Glasses.GlassesDeviceManager>(sp =>
		sp.GetRequiredService<Services.Glasses.HeyCyan.HeyCyanGlassesDeviceManager>());
		return services;
	}

	public static IServiceCollection AddViewModels(this IServiceCollection services)
	{
		services.AddSingleton<AppShell>();
		services.AddTransient<SetupViewModel>();
		services.AddTransient<MainViewModel>();
		services.AddTransient<SettingsViewModel>();
		services.AddTransient<ViewModels.Settings.ConnectionViewModel>();
		services.AddTransient<ViewModels.Settings.LlmProvidersViewModel>();
		services.AddTransient<ViewModels.Settings.AddLlmProviderViewModel>();
		services.AddTransient<ViewModels.Settings.LlmProviderDetailViewModel>();
		services.AddTransient<ViewModels.Settings.VoiceViewModel>();
		services.AddTransient<ViewModels.Settings.DeviceViewModel>();
		services.AddTransient<ViewModels.Settings.AddDevicesViewModel>();
		services.AddTransient<ViewModels.Settings.A9CameraSettingsViewModel>();
		services.AddTransient<ViewModels.Settings.Vue990CameraSettingsViewModel>();
#if WINDOWS
		services.AddTransient<ViewModels.Settings.UsbCameraSettingsViewModel>();
#endif
		services.AddTransient<ViewModels.Settings.CommandsViewModel>();
		services.AddTransient<ViewModels.Settings.CommandDetailViewModel>();
		services.AddTransient<ViewModels.Settings.AdvancedViewModel>();
		services.AddTransient<ViewModels.Settings.GlassesCameraSectionViewModel>();
		services.AddTransient<MediaGalleryViewModel>();
		services.AddTransient<GlassesViewModel>();
		services.AddTransient<Pages.Setup.SetupPage>();
		services.AddTransient<Pages.Main.MainPage>();
		services.AddTransient<Pages.Settings.SettingsPage>();
		services.AddTransient<Pages.Settings.ConnectionSettingsPage>();
		services.AddTransient<Pages.Settings.LlmProvidersSettingsPage>();
		services.AddTransient<Pages.Settings.AddLlmProviderPage>();
		services.AddTransient<Pages.Settings.LlmProviderSettingsPage>();
		services.AddTransient<Pages.Settings.VoiceSettingsPage>();
		services.AddTransient<Pages.Settings.DeviceSettingsPage>();
		services.AddTransient<Pages.Settings.AddDevicesPage>();
		services.AddTransient<Pages.Settings.A9CameraSettingsPage>();
		services.AddTransient<Pages.Settings.Vue990CameraSettingsPage>();
#if WINDOWS
		services.AddTransient<Pages.Settings.UsbCameraSettingsPage>();
#endif
		services.AddTransient<Pages.Settings.CommandsSettingsPage>();
		services.AddTransient<Pages.Settings.CommandDetailSettingsPage>();
		services.AddTransient<Pages.Settings.AdvancedSettingsPage>();
		services.AddTransient<Pages.MediaGalleryPage>();
		services.AddTransient<Pages.ImageViewerPage>();
		services.AddTransient<Pages.AudioPlayerPage>();
		services.AddTransient<Pages.GlassesPage>();

		Routing.RegisterRoute("media-gallery", typeof(Pages.MediaGalleryPage));
		Routing.RegisterRoute("image-viewer", typeof(Pages.ImageViewerPage));
		Routing.RegisterRoute("audio-player", typeof(Pages.AudioPlayerPage));
		Routing.RegisterRoute("glasses", typeof(Pages.GlassesPage));

		return services;
	}

	private static IAecProcessor CreateWebRtcAecProcessor(IServiceProvider sp, bool mobileMode)
	{
		var logger = sp.GetRequiredService<ILogger<AecProcessor>>();
		var settings = sp.GetRequiredService<AppSettings>();
		var drift = sp.GetRequiredService<Services.Audio.ClockDriftMonitor>();
		var apm = new AecProcessor(logger, settings, drift);

		try
		{
			apm.Initialize(mobileMode);
			return apm;
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
		{
			apm.Dispose();
			var fallbackLogger = sp.GetRequiredService<ILogger<NullAecProcessor>>();
			fallbackLogger.LogWarning(ex, "WebRTC APM native processor unavailable; falling back to pass-through audio");
			return new NullAecProcessor(fallbackLogger, "WebRTC APM native processor unavailable");
		}
	}
}
