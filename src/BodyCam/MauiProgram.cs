using BodyCam.Agents;
using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.ViewModels;
using CommunityToolkit.Maui;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace BodyCam;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkitCamera()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Settings
		var settingsService = new SettingsService();
		builder.Services.AddSingleton<ISettingsService>(settingsService);

		var settings = new AppSettings();

		// Apply .env overrides for provider (dev convenience)
		var provider = DotEnvReader.Read("OPENAI_PROVIDER");
		if (string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase))
		{
			settings.Provider = OpenAiProvider.Azure;
			settings.AzureEndpoint = DotEnvReader.Read("AZURE_OPENAI_ENDPOINT");
			settings.AzureRealtimeDeploymentName = DotEnvReader.Read("AZURE_OPENAI_DEPLOYMENT");
			settings.AzureChatDeploymentName = DotEnvReader.Read("AZURE_OPENAI_CHAT_DEPLOYMENT");
			settings.AzureVisionDeploymentName = DotEnvReader.Read("AZURE_OPENAI_VISION_DEPLOYMENT");
			settings.AzureApiVersion = DotEnvReader.Read("AZURE_OPENAI_API_VERSION")
			                         ?? settings.AzureApiVersion;

			// Seed into ISettingsService so the Settings UI shows .env values
			settingsService.Provider = OpenAiProvider.Azure;
			settingsService.AzureEndpoint = settings.AzureEndpoint;
			settingsService.AzureRealtimeDeploymentName = settings.AzureRealtimeDeploymentName;
			settingsService.AzureChatDeploymentName = settings.AzureChatDeploymentName;
			settingsService.AzureVisionDeploymentName = settings.AzureVisionDeploymentName;
			settingsService.AzureApiVersion = settings.AzureApiVersion;
		}
		else
		{
			// Load from persisted settings
			settings.Provider = settingsService.Provider;
			settings.AzureEndpoint = settingsService.AzureEndpoint;
			settings.AzureRealtimeDeploymentName = settingsService.AzureRealtimeDeploymentName;
			settings.AzureChatDeploymentName = settingsService.AzureChatDeploymentName;
			settings.AzureVisionDeploymentName = settingsService.AzureVisionDeploymentName;
			settings.AzureApiVersion = settingsService.AzureApiVersion;
		}

		// Load model selections from persisted settings
		settings.RealtimeModel = settingsService.RealtimeModel;
		settings.ChatModel = settingsService.ChatModel;
		settings.VisionModel = settingsService.VisionModel;
		settings.TranscriptionModel = settingsService.TranscriptionModel;
		settings.Voice = settingsService.Voice;
		settings.TurnDetection = settingsService.TurnDetection;
		settings.NoiseReduction = settingsService.NoiseReduction;
		settings.SystemInstructions = settingsService.SystemInstructions;

		builder.Services.AddSingleton(settings);

		// Services
#if WINDOWS
		builder.Services.AddSingleton<IAudioInputService, BodyCam.Platforms.Windows.WindowsAudioInputService>();
#elif ANDROID
		builder.Services.AddSingleton<IAudioInputService, BodyCam.Platforms.Android.AndroidAudioInputService>();
#else
		builder.Services.AddSingleton<IAudioInputService, AudioInputService>();
#endif
#if WINDOWS
		builder.Services.AddSingleton<IAudioOutputService, BodyCam.Platforms.Windows.WindowsAudioOutputService>();
#elif ANDROID
		builder.Services.AddSingleton<IAudioOutputService, BodyCam.Platforms.Android.AndroidAudioOutputService>();
#else
		builder.Services.AddSingleton<IAudioOutputService, AudioOutputService>();
#endif
#if WINDOWS
		builder.Services.AddSingleton<ICameraService, BodyCam.Platforms.Windows.WindowsCameraService>();
#elif ANDROID
		builder.Services.AddSingleton<ICameraService, BodyCam.Platforms.Android.AndroidCameraService>();
#else
		builder.Services.AddSingleton<ICameraService, CameraService>();
#endif
		builder.Services.AddSingleton<IRealtimeClient, RealtimeClient>();
		builder.Services.AddSingleton<IApiKeyService, ApiKeyService>();

		// Chat Completions client (deep_analysis tool)
		builder.Services.AddSingleton<IChatClient>(sp =>
		{
			var appSettings = sp.GetRequiredService<AppSettings>();
			var apiKeyService = sp.GetRequiredService<IApiKeyService>();

			if (appSettings.Provider == OpenAiProvider.Azure)
			{
				var credential = new Azure.AzureKeyCredential(
					apiKeyService.GetApiKeyAsync().GetAwaiter().GetResult()
					?? throw new InvalidOperationException("API key not configured."));
				var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(
					new Uri(appSettings.AzureEndpoint!), credential);
				return azureClient.GetChatClient(appSettings.AzureChatDeploymentName!).AsIChatClient();
			}
			else
			{
				var key = apiKeyService.GetApiKeyAsync().GetAwaiter().GetResult()
					?? throw new InvalidOperationException("API key not configured.");
				var openAiClient = new OpenAIClient(key);
				return openAiClient.GetChatClient(appSettings.ChatModel).AsIChatClient();
			}
		});

		// Agents
		builder.Services.AddSingleton<VoiceInputAgent>();
		builder.Services.AddSingleton<ConversationAgent>();
		builder.Services.AddSingleton<VoiceOutputAgent>();
		builder.Services.AddSingleton<VisionAgent>();

		// Orchestration
		builder.Services.AddSingleton<AgentOrchestrator>();

		// ViewModels
		builder.Services.AddTransient<MainViewModel>();
		builder.Services.AddTransient<ViewModels.SettingsViewModel>();

		// Pages
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<SettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
