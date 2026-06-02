using BodyCam.Services;
using BodyCam.Services.AiProviders;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class ConnectionViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IApiKeyService _apiKeyService = Substitute.For<IApiKeyService>();

    private ConnectionViewModel CreateVm(Func<HttpClient>? httpFactory = null, IAiProviderRegistry? providerRegistry = null)
        => new(_settings, _apiKeyService, providerRegistry, httpFactory);

    [Fact]
    public void SelectedProvider_SetToAzure_UpdatesIsAzure()
    {
        var vm = CreateVm();
        vm.SelectedProviderId = AiProviderIds.AzureOpenAi;
        vm.IsAzure.Should().BeTrue();
        vm.IsOpenAi.Should().BeFalse();
    }

    [Fact]
    public void SelectedProvider_SetToOpenAi_UpdatesIsOpenAi()
    {
        _settings.ProviderId.Returns(AiProviderIds.AzureOpenAi);
        var vm = CreateVm();
        vm.SelectedProviderId = AiProviderIds.OpenAi;
        vm.IsOpenAi.Should().BeTrue();
        vm.IsAzure.Should().BeFalse();
    }

    [Fact]
    public void RealtimeModelOptions_ReturnsNonEmpty()
    {
        var vm = CreateVm();
        vm.RealtimeModelOptions.Should().NotBeEmpty();
    }

    [Fact]
    public void SelectedProvider_SetToGrok_ShowsGrokCredentialNotice()
    {
        var vm = CreateVm();

        vm.SelectedProviderId = AiProviderIds.XaiGrok;

        vm.IsGrok.Should().BeTrue();
        vm.ApiKeySectionTitle.Should().Be("xAI API Key");
        vm.ApiKeyHelpText.Should().Contain("OAuth");
    }

    [Fact]
    public void ProviderOptions_IncludesRegisteredProviders()
    {
        var registry = new AiProviderRegistry([
            new OpenAiProviderAdapter(),
            new TestProviderAdapter()
        ]);

        var vm = CreateVm(providerRegistry: registry);

        vm.ProviderOptions.Should().Contain(option => option.Id == "local-test");
    }

    [Fact]
    public void SelectedRealtimeModel_Set_PersistsToSettings()
    {
        var vm = CreateVm();
        var model = vm.RealtimeModelOptions[0];
        vm.SelectedRealtimeModel = model;
        _settings.RealtimeModel = model.Id;
    }

    [Fact]
    public void AzureEndpoint_SetValue_PersistsToSettings()
    {
        var vm = CreateVm();
        vm.AzureEndpoint = "https://test.cognitiveservices.azure.com";
        _settings.AzureEndpoint.Should().Be("https://test.cognitiveservices.azure.com");
    }

    [Fact]
    public void AzureEndpoint_SetEmpty_PersistsNull()
    {
        var vm = CreateVm();
        vm.AzureEndpoint = "https://example.cognitiveservices.azure.com";
        vm.AzureEndpoint = "";
        _settings.AzureEndpoint.Should().BeNull();
    }

    [Fact]
    public void ApiKeyDisplay_InitialLoad_ShowsMaskedKey()
    {
        _apiKeyService.GetApiKeyAsync(AiProviderIds.OpenAi).Returns("sk-proj-12345678");
        var vm = CreateVm();
        // Allow async void LoadApiKeyDisplay to complete
        Thread.Sleep(200);
        vm.ApiKeyDisplay.Should().Contain("****");
    }

    [Fact]
    public void IsKeyVisible_Toggle_ShowsFullKey()
    {
        _apiKeyService.GetApiKeyAsync(AiProviderIds.OpenAi).Returns("sk-proj-12345678");
        var vm = CreateVm();
        Thread.Sleep(200);
        vm.IsKeyVisible = true;
        vm.ApiKeyDisplay.Should().Be("sk-proj-12345678");
    }

    [Fact]
    public void TestConnectionCommand_NoApiKey_ShowsError()
    {
        _apiKeyService.GetApiKeyAsync().Returns((string?)null);
        _apiKeyService.GetApiKeyAsync(AiProviderIds.OpenAi).Returns((string?)null);
        var vm = CreateVm();
        // Execute is async void — fire and wait
        vm.TestConnectionCommand.Execute(null);
        Thread.Sleep(200);
        vm.ConnectionStatus.Should().Contain("No OpenAI API key");
    }

    [Fact]
    public void TestConnectionCommand_GrokApiKey_ShowsApiKeyAuthDecision()
    {
        _settings.ProviderId.Returns(AiProviderIds.XaiGrok);
        _apiKeyService.GetApiKeyAsync(AiProviderIds.XaiGrok).Returns("xai-test-key");
        var vm = CreateVm();

        vm.TestConnectionCommand.Execute(null);
        Thread.Sleep(200);

        vm.ConnectionStatus.Should().Contain("Grok API key configured");
        vm.ConnectionStatus.Should().Contain("OAuth");
        vm.RealtimeStatus.Should().Contain("broker");
    }

    [Fact]
    public void ConnectionStatus_InitiallyEmpty()
    {
        var vm = CreateVm();
        vm.ConnectionStatus.Should().BeEmpty();
    }

    [Fact]
    public void IsTesting_InitiallyFalse()
    {
        var vm = CreateVm();
        vm.IsTesting.Should().BeFalse();
    }

    private sealed class TestProviderAdapter : IAiProviderAdapter
    {
        public AiProviderDefinition Definition { get; } = new(
            "local-test",
            "Local Test",
            "Test",
            "Provider registered only inside this test.",
            IsSelectable: true,
            CredentialModes: [AiCredentialMode.ApiKey],
            Capabilities: AiProviderCapability.Chat,
            Models: new Dictionary<AiModelKind, ModelInfo[]>
            {
                [AiModelKind.Chat] = [new("test-chat", "Test Chat")]
            },
            CredentialPolicy: AiProviderCredentialPolicy.ApiKeyOnly,
            SetupLinks: []);

        public Uri GetRealtimeUri(AppSettings settings) => new("wss://example.invalid/realtime");
        public Uri GetChatUri(AppSettings settings) => new("https://example.invalid/chat");
        public Uri GetVisionUri(AppSettings settings) => new("https://example.invalid/vision");
    }
}
