using BodyCam.Services;
using Microsoft.Extensions.AI;
using OpenAI.Realtime;

#pragma warning disable OPENAI002
#pragma warning disable MEAI001

namespace BodyCam.RealTests.Fixtures;

internal static class RealtimeFixture
{
    internal static AppSettings LoadSettings()
    {
        var settings = new AppSettings();
        var provider = DotEnvReader.Read("OPENAI_PROVIDER");
        if (string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase))
        {
            settings.Provider = OpenAiProvider.Azure;
            settings.AzureEndpoint = DotEnvReader.Read("AZURE_OPENAI_ENDPOINT");
            settings.AzureRealtimeDeploymentName = DotEnvReader.Read("AZURE_OPENAI_DEPLOYMENT");
            settings.AzureChatDeploymentName = DotEnvReader.Read("AZURE_OPENAI_CHAT_DEPLOYMENT");
            settings.AzureVisionDeploymentName = DotEnvReader.Read("AZURE_OPENAI_VISION_DEPLOYMENT");
            var version = DotEnvReader.Read("AZURE_OPENAI_API_VERSION");
            if (version is not null) settings.AzureApiVersion = version;
        }
        return settings;
    }

    internal static string LoadApiKey(OpenAiProvider provider)
    {
        var key = provider == OpenAiProvider.Azure
            ? DotEnvReader.Read("AZURE_OPENAI_API_KEY")
            : DotEnvReader.Read("OPENAI_API_KEY");
        return key ?? throw new InvalidOperationException(
            $"API key not found. Set {(provider == OpenAiProvider.Azure ? "AZURE_OPENAI_API_KEY" : "OPENAI_API_KEY")}.");
    }

    internal static IRealtimeClient BuildClient(string apiKey, AppSettings settings)
    {
        if (settings.Provider == OpenAiProvider.Azure)
        {
            var rtOpts = new RealtimeClientOptions
            {
                Endpoint = new Uri($"{settings.AzureEndpoint!.TrimEnd('/')}/openai/v1/realtime")
            };
            var sdkClient = new AzureRealtimeClient(apiKey, rtOpts);
            return new OpenAIRealtimeClient(sdkClient, settings.AzureRealtimeDeploymentName!);
        }
        var openAiSdkClient = new RealtimeClient(apiKey);
        return new OpenAIRealtimeClient(openAiSdkClient, settings.RealtimeModel);
    }
}
