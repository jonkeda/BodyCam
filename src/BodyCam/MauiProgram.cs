using BodyCam.Services;
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
		builder.Services
			.AddAudioServices()
			.AddCameraServices()
			.AddAgents()
			.AddTools()
			.AddOrchestration()
			.AddViewModels();

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

		// Memory store
		builder.Services.AddSingleton<MemoryStore>(sp =>
			new MemoryStore(Path.Combine(FileSystem.AppDataDirectory, "memories.json")));

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
