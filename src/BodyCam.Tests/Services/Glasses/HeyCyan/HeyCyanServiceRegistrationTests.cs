using BodyCam;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

public sealed class HeyCyanServiceRegistrationTests
{
    [Fact]
    public async Task AddGlassesServices_resolves_real_media_transfer_on_windows()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddGlassesServices();

        // Override platform session dependencies so the test only checks the
        // transfer registration decision.
        services.AddSingleton<IHeyCyanGlassesSession, FakeHeyCyanSession>();
        services.AddSingleton<IHeyCyanHttpClientFactory, ThrowingHttpClientFactory>();

        await using var provider = services.BuildServiceProvider();

        var transfer = provider.GetRequiredService<IHeyCyanMediaTransfer>();

        transfer.Should().BeOfType<HeyCyanMediaTransfer>();
        transfer.Should().NotBeAssignableTo<IHeyCyanStoredImageMediaTransfer>();
    }

    private sealed class ThrowingHttpClientFactory : IHeyCyanHttpClientFactory
    {
        public Task<IHeyCyanHttpClient> CreateAsync(Uri baseUri, CancellationToken ct)
            => throw new NotSupportedException("This registration test does not open HTTP.");
    }
}
