using System.Text.Json;
using BodyCam.Services;
using BodyCam.Services.AiProviders;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services.AiProviders;

public class AiProviderInstanceStoreTests
{
    [Fact]
    public async Task GetInstancesAsync_CreatesDefaultInstancesFromActiveProviderSetting()
    {
        string? savedJson = null;
        var settings = CreateSettings(AiProviderIds.XaiGrok);
        var store = new AiProviderInstanceStore(
            new AiProviderRegistry(),
            settings,
            () => savedJson,
            json => savedJson = json);

        var instances = await store.GetInstancesAsync();

        instances.Should().Contain(instance => instance.ProviderId == AiProviderIds.OpenAi);
        instances.Should().Contain(instance => instance.ProviderId == AiProviderIds.AzureOpenAi);
        instances.Should().Contain(instance => instance.ProviderId == AiProviderIds.XaiGrok && instance.IsActive);
        savedJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetInstancesAsync_InvalidStoredJson_RecreatesDefaults()
    {
        var settings = CreateSettings(AiProviderIds.OpenAi);
        var store = new AiProviderInstanceStore(
            new AiProviderRegistry(),
            settings,
            () => "{not-json",
            _ => { });

        var instances = await store.GetInstancesAsync();

        instances.Should().NotBeEmpty();
        instances.Single(instance => instance.ProviderId == AiProviderIds.OpenAi).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureInstanceAsync_AddsFutureProviderWithoutSettingsPageChanges()
    {
        string? savedJson = JsonSerializer.Serialize(new List<AiProviderInstanceSettings>());
        var registry = new AiProviderRegistry([
            new FakeProviderAdapter("future-ai", AiProviderCapability.Chat | AiProviderCapability.ImageInput)
        ]);
        var store = new AiProviderInstanceStore(
            registry,
            CreateSettings("future-ai"),
            () => savedJson,
            json => savedJson = json);

        var instance = await store.EnsureInstanceAsync("future-ai");

        instance.ProviderId.Should().Be("future-ai");
        instance.DisplayName.Should().Be("Fake Provider");
        instance.CredentialMode.Should().Be(AiCredentialMode.ApiKey.ToString());
        savedJson.Should().Contain("future-ai");
    }

    [Fact]
    public async Task SetActiveAsync_PersistsActiveInstanceAndUpdatesSettings()
    {
        var settings = CreateSettings(AiProviderIds.OpenAi);
        string? savedJson = null;
        var store = new AiProviderInstanceStore(
            new AiProviderRegistry(),
            settings,
            () => savedJson,
            json => savedJson = json);
        await store.GetInstancesAsync();

        await store.SetActiveAsync(AiProviderIds.XaiGrok);

        settings.Received().ProviderId = AiProviderIds.XaiGrok;
        var instances = JsonSerializer.Deserialize<List<AiProviderInstanceSettings>>(savedJson!);
        instances.Should().Contain(instance => instance.ProviderId == AiProviderIds.XaiGrok && instance.IsActive);
    }

    private static ISettingsService CreateSettings(string providerId)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ProviderId.Returns(providerId);
        return settings;
    }
}
