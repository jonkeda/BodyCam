using BodyCam.Services;
using BodyCam.ViewModels.Settings;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels.Settings;

public class ConnectionViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IApiKeyService _apiKeyService = Substitute.For<IApiKeyService>();

    private ConnectionViewModel CreateVm(Func<HttpClient>? httpFactory = null)
        => new(_settings, _apiKeyService, httpFactory);

    [Fact]
    public void SelectedProvider_SetToAzure_UpdatesIsAzure()
    {
        var vm = CreateVm();
        vm.SelectedProvider = OpenAiProvider.Azure;
        vm.IsAzure.Should().BeTrue();
        vm.IsOpenAi.Should().BeFalse();
    }

    [Fact]
    public void SelectedProvider_SetToOpenAi_UpdatesIsOpenAi()
    {
        _settings.Provider.Returns(OpenAiProvider.Azure);
        var vm = CreateVm();
        vm.SelectedProvider = OpenAiProvider.OpenAi;
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
        _apiKeyService.GetApiKeyAsync().Returns("sk-proj-12345678");
        var vm = CreateVm();
        // Allow async void LoadApiKeyDisplay to complete
        Thread.Sleep(200);
        vm.ApiKeyDisplay.Should().Contain("****");
    }

    [Fact]
    public void IsKeyVisible_Toggle_ShowsFullKey()
    {
        _apiKeyService.GetApiKeyAsync().Returns("sk-proj-12345678");
        var vm = CreateVm();
        Thread.Sleep(200);
        vm.IsKeyVisible = true;
        vm.ApiKeyDisplay.Should().Be("sk-proj-12345678");
    }

    [Fact]
    public void TestConnectionCommand_NoApiKey_ShowsError()
    {
        _apiKeyService.GetApiKeyAsync().Returns((string?)null);
        var vm = CreateVm();
        // Execute is async void — fire and wait
        vm.TestConnectionCommand.Execute(null);
        Thread.Sleep(200);
        vm.ConnectionStatus.Should().Contain("No API key");
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
}
