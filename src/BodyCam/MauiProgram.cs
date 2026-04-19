using BodyCam.Services;
using BodyCam.Services.Logging;
using Azure.Monitor.OpenTelemetry.Exporter;
using CommunityToolkit.Maui;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

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

// Chat Completions client (deep_analysis + vision tools)
		builder.Services.AddSingleton<IChatClient, AppChatClient>();

		// Memory store
		builder.Services.AddSingleton<MemoryStore>(sp =>
			new MemoryStore(Path.Combine(FileSystem.AppDataDirectory, "memories.json")));

		// Analytics service (opt-in)
		if (settingsService.SendUsageData)
			builder.Services.AddSingleton<IAnalyticsService, OpenTelemetryAnalyticsService>();
		else
			builder.Services.AddSingleton<IAnalyticsService, NullAnalyticsService>();

		// In-app log sink (ring buffer for debug overlay)
		var logSink = new InAppLogSink();
		builder.Services.AddSingleton(logSink);
		builder.Logging.AddProvider(new InAppLoggerProvider(logSink, LogLevel.Debug));

		// OpenTelemetry + Azure Monitor (opt-in, Warning+ only)
		if (settingsService.SendDiagnosticData
			&& !string.IsNullOrEmpty(settingsService.AzureMonitorConnectionString))
		{
			builder.Logging.AddOpenTelemetry(otel =>
			{
				otel.SetResourceBuilder(ResourceBuilder.CreateDefault()
					.AddService("BodyCam", serviceVersion: AppInfo.VersionString)
					.AddAttributes(new Dictionary<string, object>
					{
						["device.platform"] = DeviceInfo.Platform.ToString(),
						["device.model"] = DeviceInfo.Model,
						["os.version"] = DeviceInfo.VersionString,
						["session.id"] = Guid.NewGuid().ToString("N")[..12]
					}));

				otel.AddAzureMonitorLogExporter(options =>
				{
					options.ConnectionString = settingsService.AzureMonitorConnectionString;
				});
			});

			builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("", LogLevel.Warning);
		}

		// Sentry crash reporting (opt-in)
		if (settingsService.SendCrashReports
			&& !string.IsNullOrEmpty(settingsService.SentryDsn))
		{
			builder.UseSentry(options =>
			{
				options.Dsn = settingsService.SentryDsn;
				options.IsGlobalModeEnabled = true;
				options.MinimumBreadcrumbLevel = LogLevel.Information;
				options.MinimumEventLevel = LogLevel.Error;
				options.SendDefaultPii = false;
				options.CacheDirectoryPath = FileSystem.CacheDirectory;
				options.TracesSampleRate = 0;
				options.Release = $"bodycam@{AppInfo.VersionString}";
				options.Environment =
#if DEBUG
					"development";
#else
					"production";
#endif
				options.SetBeforeSend((sentryEvent, _) =>
				{
					// Strip API keys from exception messages
					if (sentryEvent.Message?.Formatted?.Contains("sk-", StringComparison.Ordinal) == true)
						sentryEvent.Message = new Sentry.SentryMessage { Formatted = "[redacted - contained API key]" };

					return sentryEvent;
				});
			});
		}

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
