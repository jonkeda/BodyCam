using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BodyCam.IntegrationTests.Fixtures;
using FluentAssertions;

namespace BodyCam.IntegrationTests.Services;

public class OpenAiStreamingClientTests : IClassFixture<OpenAiWireMockFixture>
{
    private readonly OpenAiWireMockFixture _fixture;
    private readonly HttpClient _httpClient;

    public OpenAiStreamingClientTests(OpenAiWireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetToDefaults();
        _httpClient = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
    }

    [Fact]
    public async Task ChatCompletions_ReturnsSuccessResponse()
    {
        // Arrange
        var request = new
        {
            model = "gpt-5.4-mini",
            messages = new[]
            {
                new { role = "user", content = "Hello" }
            }
        };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        content.Should().Be("Mock response from WireMock");
    }

    [Fact]
    public async Task ChatCompletions_ContainsUsageInfo()
    {
        // Arrange
        var request = new
        {
            model = "gpt-5.4-mini",
            messages = new[]
            {
                new { role = "user", content = "test" }
            }
        };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", request);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        // Assert
        var usage = doc.RootElement.GetProperty("usage");
        usage.GetProperty("prompt_tokens").GetInt32().Should().Be(10);
        usage.GetProperty("completion_tokens").GetInt32().Should().Be(5);
        usage.GetProperty("total_tokens").GetInt32().Should().Be(15);
    }

    [Fact]
    public async Task ChatCompletions_IncludesAuthorizationHeader()
    {
        // Arrange
        var request = new
        {
            model = "gpt-5.4-mini",
            messages = new[]
            {
                new { role = "user", content = "test" }
            }
        };

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer test-api-key");

        // Act
        var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var logEntry = _fixture.Server.LogEntries.Last();
        logEntry.RequestMessage.Headers?["Authorization"].Should().Contain("Bearer test-api-key");
    }

    [Fact]
    public async Task RateLimit_Returns429()
    {
        // Arrange
        _fixture.StubRateLimit();

        var request = new
        {
            model = "gpt-5.4-mini",
            messages = new[]
            {
                new { role = "user", content = "test" }
            }
        };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.GetValues("Retry-After").Should().Contain("1");
    }

    [Fact]
    public async Task ServerError_Returns500()
    {
        // Arrange
        _fixture.StubServerError();

        var request = new
        {
            model = "gpt-5.4-mini",
            messages = new[]
            {
                new { role = "user", content = "test" }
            }
        };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Internal server error");
    }

    [Fact]
    public async Task VisionEndpoint_ReturnsDescription()
    {
        // Arrange
        _fixture.StubVisionResponse("A red car parked on the street.");

        var request = new
        {
            model = "gpt-5.4",
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "What do you see?" },
                        new { type = "image_url", image_url = new { url = "data:image/jpeg;base64,/9j/4AAQ..." } }
                    }
                }
            }
        };

        // Act
        var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", request);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        content.Should().Be("A red car parked on the street.");
    }

    [Fact]
    public async Task WireMock_LogsAllRequests()
    {
        // Arrange — reset server to fresh state
        _fixture.Server.Reset();
        _fixture.Server
            .Given(WireMock.RequestBuilders.Request.Create()
                .WithPath("/v1/chat/completions")
                .UsingPost())
            .RespondWith(WireMock.ResponseBuilders.Response.Create()
                .WithStatusCode(200)
                .WithBody("{}"));

        // Act
        await _httpClient.PostAsJsonAsync("/v1/chat/completions", new { model = "gpt-5.4-mini" });
        await _httpClient.PostAsJsonAsync("/v1/chat/completions", new { model = "gpt-5.4" });

        // Assert
        _fixture.Server.LogEntries.Should().HaveCount(2);
    }
}
