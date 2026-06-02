using BodyCam.Services.AiProviders;
using FluentAssertions;

namespace BodyCam.Tests.Services.AiProviders;

public class AiProviderRegistryTests
{
    [Fact]
    public void DefaultRegistry_IncludesCurrentProvidersAndGrokPlaceholder()
    {
        var registry = new AiProviderRegistry();

        registry.TryGet(AiProviderIds.OpenAi).Should().NotBeNull();
        registry.TryGet(AiProviderIds.AzureOpenAi).Should().NotBeNull();
        registry.TryGet(AiProviderIds.XaiGrok).Should().NotBeNull();
    }

    [Fact]
    public void GetModels_OpenAi_ReturnsProviderSpecificModels()
    {
        var registry = new AiProviderRegistry();

        var models = registry.GetModels(AiProviderIds.OpenAi, AiModelKind.Realtime);

        models.Should().Contain(model => model.Id == ModelOptions.DefaultRealtime);
    }

    [Fact]
    public void Grok_UsesApiKeyAuthUntilOfficialOAuthExists()
    {
        var registry = new AiProviderRegistry();

        var grok = registry.GetRequired(AiProviderIds.XaiGrok);

        grok.IsSelectable.Should().BeTrue();
        grok.CredentialModes.Should().Contain(AiCredentialMode.ApiKey);
        grok.CredentialModes.Should().NotContain(AiCredentialMode.OAuthPkce);
        grok.CredentialPolicy.OAuthAvailable.Should().BeFalse();
        grok.CredentialPolicy.RequiresEphemeralRealtimeTokenBroker.Should().BeTrue();
    }

    [Fact]
    public void Grok_ExposesModelOptionsAndImageCapabilities()
    {
        var registry = new AiProviderRegistry();

        var grok = registry.GetRequired(AiProviderIds.XaiGrok);

        grok.Supports(AiProviderCapability.ImageGeneration).Should().BeTrue();
        grok.Supports(AiProviderCapability.ImageEditing).Should().BeTrue();
        registry.GetModels(AiProviderIds.XaiGrok, AiModelKind.Chat)
            .Should().Contain(model => model.Id == "grok-4.3");
        registry.GetModels(AiProviderIds.XaiGrok, AiModelKind.ImageGeneration)
            .Should().Contain(model => model.Id == "grok-imagine-image-quality");
    }
}
