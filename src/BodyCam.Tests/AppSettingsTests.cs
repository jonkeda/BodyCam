using FluentAssertions;

namespace BodyCam.Tests;

public class AppSettingsTests
{
    // --- GetRealtimeUri ---

    [Fact]
    public void GetRealtimeUri_OpenAi_ContainsModel()
    {
        var settings = new AppSettings
        {
            Provider = OpenAiProvider.OpenAi,
            RealtimeModel = "gpt-realtime-1.5"
        };

        var uri = settings.GetRealtimeUri();

        uri.Scheme.Should().Be("wss");
        uri.Host.Should().Be("api.openai.com");
        uri.Query.Should().Contain("model=gpt-realtime-1.5");
    }

    [Fact]
    public void GetRealtimeUri_Azure_ContainsDeploymentAndApiVersion()
    {
        var settings = new AppSettings
        {
            Provider = OpenAiProvider.Azure,
            AzureEndpoint = "https://myresource.cognitiveservices.azure.com",
            AzureRealtimeDeploymentName = "my-rt-deploy",
            AzureApiVersion = "2025-04-01-preview"
        };

        var uri = settings.GetRealtimeUri();

        uri.Scheme.Should().Be("wss");
        uri.Host.Should().Be("myresource.cognitiveservices.azure.com");
        uri.Query.Should().Contain("deployment=my-rt-deploy");
        uri.Query.Should().Contain("api-version=2025-04-01-preview");
    }

    // --- GetChatUri ---

    [Fact]
    public void GetChatUri_OpenAi_ReturnsChatCompletionsUrl()
    {
        var settings = new AppSettings { Provider = OpenAiProvider.OpenAi };

        var uri = settings.GetChatUri();

        uri.ToString().Should().Be("https://api.openai.com/v1/chat/completions");
    }

    [Fact]
    public void GetChatUri_Azure_ContainsChatDeployment()
    {
        var settings = new AppSettings
        {
            Provider = OpenAiProvider.Azure,
            AzureEndpoint = "https://myresource.cognitiveservices.azure.com",
            AzureChatDeploymentName = "my-chat",
            AzureApiVersion = "2025-04-01-preview"
        };

        var uri = settings.GetChatUri();

        uri.Scheme.Should().Be("https");
        uri.Host.Should().Be("myresource.cognitiveservices.azure.com");
        uri.AbsolutePath.Should().Contain("/deployments/my-chat/chat/completions");
        uri.Query.Should().Contain("api-version=2025-04-01-preview");
    }

    // --- GetVisionUri ---

    [Fact]
    public void GetVisionUri_OpenAi_ReturnsChatCompletionsUrl()
    {
        var settings = new AppSettings { Provider = OpenAiProvider.OpenAi };

        var uri = settings.GetVisionUri();

        uri.ToString().Should().Be("https://api.openai.com/v1/chat/completions");
    }

    [Fact]
    public void GetVisionUri_Azure_ContainsVisionDeployment()
    {
        var settings = new AppSettings
        {
            Provider = OpenAiProvider.Azure,
            AzureEndpoint = "https://myresource.cognitiveservices.azure.com",
            AzureVisionDeploymentName = "my-vision",
            AzureApiVersion = "2025-04-01-preview"
        };

        var uri = settings.GetVisionUri();

        uri.AbsolutePath.Should().Contain("/deployments/my-vision/chat/completions");
    }

    // --- Defaults ---

    [Fact]
    public void Defaults_TranscriptionModel_IsGpt4oMiniTranscribe()
    {
        var settings = new AppSettings();
        settings.TranscriptionModel.Should().Be("gpt-4o-mini-transcribe");
    }

    [Fact]
    public void Defaults_AzureApiVersion_Is2025Preview()
    {
        var settings = new AppSettings();
        settings.AzureApiVersion.Should().Be("2025-04-01-preview");
    }
}
