using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BodyCam.IntegrationTests.Fixtures;

public class OpenAiWireMockFixture : IAsyncLifetime
{
    public WireMockServer Server { get; private set; } = null!;
    public string BaseUrl => Server.Url!;

    public Task InitializeAsync()
    {
        Server = WireMockServer.Start();
        SetupDefaultStubs();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Server.Stop();
        Server.Dispose();
        return Task.CompletedTask;
    }

    private void SetupDefaultStubs()
    {
        // Chat Completions endpoint
        Server
            .Given(Request.Create()
                .WithPath("/v1/chat/completions")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                    "id": "chatcmpl-test",
                    "object": "chat.completion",
                    "choices": [{
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": "Mock response from WireMock"
                        },
                        "finish_reason": "stop"
                    }],
                    "usage": {
                        "prompt_tokens": 10,
                        "completion_tokens": 5,
                        "total_tokens": 15
                    }
                }
                """));
    }

    /// <summary>
    /// Reset server to default stubs. Call at start of each test for isolation.
    /// </summary>
    public void ResetToDefaults()
    {
        Server.Reset();
        SetupDefaultStubs();
    }

    /// <summary>
    /// Stub a 429 rate limit response.
    /// </summary>
    public void StubRateLimit()
    {
        Server.Reset();
        Server
            .Given(Request.Create()
                .WithPath("/v1/chat/completions")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(429)
                .WithHeader("Retry-After", "1")
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"error":{"message":"Rate limit exceeded","type":"rate_limit_error"}}"""));
    }

    /// <summary>
    /// Stub a 500 server error response.
    /// </summary>
    public void StubServerError()
    {
        Server.Reset();
        Server
            .Given(Request.Create()
                .WithPath("/v1/chat/completions")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"error":{"message":"Internal server error","type":"server_error"}}"""));
    }

    /// <summary>
    /// Stub a vision-specific response.
    /// </summary>
    public void StubVisionResponse(string description = "I see a desk with a laptop and coffee mug.")
    {
        Server.Reset();
        Server
            .Given(Request.Create()
                .WithPath("/v1/chat/completions")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($$"""
                {
                    "id": "chatcmpl-vision",
                    "object": "chat.completion",
                    "choices": [{
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": "{{description}}"
                        },
                        "finish_reason": "stop"
                    }],
                    "usage": {
                        "prompt_tokens": 200,
                        "completion_tokens": 12,
                        "total_tokens": 212
                    }
                }
                """));
    }
}
