using System.Net;
using System.Text;
using BodyCam.Services.AiProviders;
using FluentAssertions;

namespace BodyCam.Tests.Services.AiProviders;

public class GrokEphemeralTokenBrokerTests
{
    [Fact]
    public async Task CreateClientSecretAsync_ReadsNestedClientSecret()
    {
        var broker = new GrokEphemeralTokenBroker(() => new HttpClient(
            new StubHandler(HttpStatusCode.OK, """{"client_secret":{"value":"secret-123"},"expires_in":300}""")));

        var result = await broker.CreateClientSecretAsync(new Uri("https://broker.example/session"));

        result.Value.Should().Be("secret-123");
        result.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateClientSecretAsync_FailsWhenBrokerFails()
    {
        var broker = new GrokEphemeralTokenBroker(() => new HttpClient(
            new StubHandler(HttpStatusCode.BadGateway, """{"error":"down"}""")));

        var act = async () => await broker.CreateClientSecretAsync(new Uri("https://broker.example/session"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*502*");
    }

    [Fact]
    public async Task CreateClientSecretAsync_FailsWhenSecretMissing()
    {
        var broker = new GrokEphemeralTokenBroker(() => new HttpClient(
            new StubHandler(HttpStatusCode.OK, """{"expires_in":300}""")));

        var act = async () => await broker.CreateClientSecretAsync(new Uri("https://broker.example/session"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*client secret*");
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
